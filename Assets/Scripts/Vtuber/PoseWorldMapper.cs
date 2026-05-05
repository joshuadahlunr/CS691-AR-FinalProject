using System.Collections.Generic;
using UnityEngine;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.PoseLandmarker;

/// <summary>
/// Converts MediaPipe normalised pose landmarks into Unity world-space positions.
///
/// MediaPipe gives you:
///   landmark.X  -- normalised [0,1] across the image width  (left=0, right=1)
///   landmark.Y  -- normalised [0,1] across the image height (top=0, bottom=1)
///   landmark.Z  -- relative depth, same scale as X. Negative = closer to camera.
///                  It is NOT a true depth value; it is only meaningful relative
///                  to the hip midpoint (landmark 23/24 average), which MediaPipe
///                  uses as the depth origin.
///
/// Two unprojection strategies are available via the Mode enum:
///
///   RaycastAtDepthPlane  (default, recommended for AR/overlay)
///     Fires a ray from the camera through the screen-space landmark coordinate.
///     The ray is intersected with a world-space plane at a fixed distance from
///     the camera. Landmark Z shifts points forward/back relative to that plane.
///     Good when the camera is the "real" camera in a mixed-reality scene.
///
///   FixedVolumeMapping  (recommended for puppet/avatar driving)
///     Maps the normalised X/Y coords into a configurable 3D box centred on a
///     target transform. Z is scaled independently. No camera required.
///     Good when you want the skeleton to "live" at a specific world location.
///
/// Attach to any GameObject. Feed it a PoseLandmarkerResult each frame by
/// calling UpdateLandmarks(). Read world positions from WorldPositions[].
/// Optionally assign JointMarkers to visualise them.
/// </summary>
public class PoseWorldMapper : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Public enums / settings
    // -----------------------------------------------------------------------

    public enum MappingMode
    {
        RaycastAtDepthPlane,
        FixedVolumeMapping,
    }

    [Header("Strategy")]
    public MappingMode mode = MappingMode.RaycastAtDepthPlane;

    // -- RaycastAtDepthPlane settings ----------------------------------------
    [Header("Raycast Mode")]
    [Tooltip("The scene camera whose frustum is used for unprojection. " +
             "Leave null to use Camera.main.")]
    public Camera sceneCamera;

    [Tooltip("World-space distance from the camera at which the depth plane sits.")]
    public float depthPlaneDistance = 2f;

    [Tooltip("How many Unity units the MediaPipe Z range maps to in world space. " +
             "MediaPipe Z is roughly [-0.5, 0.5] relative to the hip midpoint.")]
    public float depthScale = 1f;

    // -- FixedVolumeMapping settings -----------------------------------------
    [Header("Fixed Volume Mode")]
    [Tooltip("Centre of the world-space volume the skeleton is mapped into. " +
             "Leave null to use this transform.")]
    public Transform volumeCenter;

    [Tooltip("Width of the mapping volume in world units (maps X [0,1]).")]
    public float volumeWidth  = 1.8f;

    [Tooltip("Height of the mapping volume in world units (maps Y [0,1]).")]
    public float volumeHeight = 2.0f;

    [Tooltip("Depth of the mapping volume in world units (maps Z [-0.5, 0.5]).")]
    public float volumeDepth  = 0.5f;

    // -- Smoothing ------------------------------------------------------------
    [Header("Smoothing")]
    [Tooltip("Exponential moving average factor. 1 = no smoothing, 0.05 = heavy.")]
    [Range(0.01f, 1f)]
    public float smoothing = 0.35f;

    [Tooltip("Landmarks below this visibility threshold are not updated.")]
    [Range(0f, 1f)]
    public float visibilityThreshold = 0.5f;

    // -- Debug visualisation --------------------------------------------------
    [Header("Debug Visualisation")]
    [Tooltip("Optional: one marker per landmark (33 total). " +
             "Assign prefabs or scene objects; they will be moved each frame.")]
    public Transform[] jointMarkers = new Transform[33];

    [Tooltip("Draw Gizmo spheres in the editor for all visible landmarks.")]
    public bool drawGizmos = true;

    // -----------------------------------------------------------------------
    // Public output
    // -----------------------------------------------------------------------

    /// <summary>World-space position for each of the 33 MediaPipe landmarks.</summary>
    public Vector3[] WorldPositions { get; private set; } = new Vector3[33];

    /// <summary>Visibility score for each landmark (0-1). Check before using.</summary>
    public float[] Visibility { get; private set; } = new float[33];

    /// <summary>True when at least one pose result has been received.</summary>
    public bool HasData { get; private set; }

    // -----------------------------------------------------------------------
    // MediaPipe landmark indices (named for convenience)
    // -----------------------------------------------------------------------

    public const int NOSE           = 0;
    public const int LEFT_SHOULDER  = 11;
    public const int RIGHT_SHOULDER = 12;
    public const int LEFT_ELBOW     = 13;
    public const int RIGHT_ELBOW    = 14;
    public const int LEFT_WRIST     = 15;
    public const int RIGHT_WRIST    = 16;
    public const int LEFT_HIP       = 23;
    public const int RIGHT_HIP      = 24;
    public const int LEFT_KNEE      = 25;
    public const int RIGHT_KNEE     = 26;
    public const int LEFT_ANKLE     = 27;
    public const int RIGHT_ANKLE    = 28;

    // -----------------------------------------------------------------------
    // Private state
    // -----------------------------------------------------------------------

    private Vector3[] _smoothed = new Vector3[33];
    private bool[]    _hasSmoothed = new bool[33];

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Call this every frame after PoseLandmarkerManager produces a result.
    /// Safe to call with a null or empty result.
    /// </summary>
    public void UpdateLandmarks(PoseLandmarkerResult result)
    {
        if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
            return;

        // Use the first detected pose
        var landmarks = result.poseLandmarks[0].landmarks;
        if (landmarks == null || landmarks.Count == 0)
            return;

        HasData = true;

        Camera cam = ResolveCamera();
        Transform center = volumeCenter != null ? volumeCenter : transform;

        for (int i = 0; i < Mathf.Min(landmarks.Count, 33); i++)
        {
            var lm  = landmarks[i];
            float v = lm.visibility ?? 0f;
            Visibility[i] = v;

            if (v < visibilityThreshold)
                continue;

            Vector3 worldPos = mode == MappingMode.RaycastAtDepthPlane
                ? UnprojectRaycast(lm.x, lm.y, lm.z, cam)
                : UnprojectVolume(lm.x, lm.y, lm.z, center);

            // Exponential moving average smoothing
            if (_hasSmoothed[i])
                _smoothed[i] = Vector3.Lerp(_smoothed[i], worldPos, smoothing);
            else
            {
                _smoothed[i]    = worldPos;
                _hasSmoothed[i] = true;
            }

            WorldPositions[i] = _smoothed[i];
        }

        UpdateMarkers();
    }

    /// <summary>Returns the midpoint of the two hips in world space.</summary>
    public Vector3 HipCenter =>
        (WorldPositions[LEFT_HIP] + WorldPositions[RIGHT_HIP]) * 0.5f;

    /// <summary>Returns the midpoint of the two shoulders in world space.</summary>
    public Vector3 ShoulderCenter =>
        (WorldPositions[LEFT_SHOULDER] + WorldPositions[RIGHT_SHOULDER]) * 0.5f;

    /// <summary>
    /// Returns a rotation representing the facing direction of the torso.
    /// Forward is the cross product of the shoulder line and the spine line.
    /// </summary>
    public Quaternion TorsoRotation()
    {
        Vector3 shoulderL = WorldPositions[LEFT_SHOULDER];
        Vector3 shoulderR = WorldPositions[RIGHT_SHOULDER];
        Vector3 hipL      = WorldPositions[LEFT_HIP];
        Vector3 hipR      = WorldPositions[RIGHT_HIP];

        Vector3 right = (shoulderR - shoulderL).normalized;
        Vector3 up    = (ShoulderCenter - HipCenter).normalized;
        Vector3 fwd   = Vector3.Cross(right, up);

        if (fwd == Vector3.zero) return Quaternion.identity;
        return Quaternion.LookRotation(fwd, up);
    }

    // -----------------------------------------------------------------------
    // Unprojection strategies
    // -----------------------------------------------------------------------

    /// <summary>
    /// Strategy A: fire a ray through the screen-space (X,Y) coordinate,
    /// intersect it with a plane at <depthPlaneDistance>, then offset along
    /// the ray direction by the landmark's Z value.
    /// </summary>
    private Vector3 UnprojectRaycast(float nx, float ny, float nz, Camera cam)
    {
        if (cam == null)
        {
            Debug.LogWarning("[PoseWorldMapper] No camera found for RaycastAtDepthPlane mode.");
            return Vector3.zero;
        }

        // Convert normalised [0,1] image coords to screen pixels.
        // Flip Y because MediaPipe Y=0 is top, Unity screen Y=0 is bottom.
        float screenX = nx * cam.pixelWidth;
        float screenY = (1f - ny) * cam.pixelHeight;

        Ray ray = cam.ScreenPointToRay(new Vector3(screenX, screenY, 0f));

        // Intersect with the depth plane
        float t = depthPlaneDistance / Mathf.Max(Vector3.Dot(ray.direction, cam.transform.forward), 1e-6f);
        Vector3 planePoint = ray.origin + ray.direction * t;

        // Offset along the camera's forward axis by the relative Z.
        // MediaPipe Z is negative when closer to camera, so we negate it.
        planePoint -= cam.transform.forward * (nz * depthScale);

        return planePoint;
    }

    /// <summary>
    /// Strategy B: map normalised (X,Y,Z) directly into a world-space box
    /// centred on <center>. No camera required.
    ///   X [0,1]      -> [-volumeWidth/2,  +volumeWidth/2]  (left/right)
    ///   Y [0,1]      -> [+volumeHeight/2, -volumeHeight/2] (top/bottom, flipped)
    ///   Z [-0.5,0.5] -> [-volumeDepth/2,  +volumeDepth/2]  (forward/back)
    /// </summary>
    private Vector3 UnprojectVolume(float nx, float ny, float nz, Transform center)
    {
        float localX =  (nx - 0.5f) * volumeWidth;
        float localY = -(ny - 0.5f) * volumeHeight; // flip Y
        float localZ =  nz          * volumeDepth;

        // Transform from the volume's local space into world space so the
        // skeleton follows the center transform's rotation and position.
        return center.TransformPoint(new Vector3(localX, localY, localZ));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Camera ResolveCamera()
    {
        if (sceneCamera != null) return sceneCamera;
        return Camera.main;
    }

    private void UpdateMarkers()
    {
        if (jointMarkers == null) return;
        for (int i = 0; i < Mathf.Min(jointMarkers.Length, 33); i++)
        {
            if (jointMarkers[i] == null) continue;
            jointMarkers[i].position = WorldPositions[i];
            jointMarkers[i].gameObject.SetActive(Visibility[i] >= visibilityThreshold);
        }
    }

    // -----------------------------------------------------------------------
    // Gizmos
    // -----------------------------------------------------------------------

    private static readonly (int, int)[] GizmoConnections =
    {
        (11,12), (11,13), (13,15), (12,14), (14,16),   // arms
        (11,23), (12,24), (23,24),                      // torso
        (23,25), (25,27), (24,26), (26,28),             // legs
    };

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !HasData) return;

        Gizmos.color = new Color(0f, 1f, 0.5f, 0.9f);
        for (int i = 0; i < 33; i++)
        {
            if (Visibility[i] < visibilityThreshold) continue;
            Gizmos.DrawSphere(WorldPositions[i], 0.025f);
        }

        Gizmos.color = new Color(1f, 0.8f, 0f, 0.7f);
        foreach (var (a, b) in GizmoConnections)
        {
            if (Visibility[a] < visibilityThreshold || Visibility[b] < visibilityThreshold) continue;
            Gizmos.DrawLine(WorldPositions[a], WorldPositions[b]);
        }
    }
}