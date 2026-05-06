using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion
{
    // NOTE: assumes SkeletonModel enum exists elsewhere in your SDK

    /// <summary>
    /// Handles the visualization of the skeleton joints.
    /// Coexists with the original SkeletonManager (identical logic, different class name).
    /// </summary>
    public class SkeletonManagerOpenXR : MonoBehaviour
    {

        [Header("Depth Validation / Fallbacks")]
        [SerializeField, Range(0.08f, 0.30f)] float depthClampMin = 0.12f;  // m
        [SerializeField, Range(0.30f, 1.00f)] float depthClampMax = 0.60f;  // m
        [SerializeField, Range(0.01f, 0.50f)] float depthMaxJumpMeters = 0.25f;
        [SerializeField] bool androidPlanePointFallback = true;
        [SerializeField] bool probeNeighborhoodOnMiss = true;
        [SerializeField, Range(1, 8)] int probeStepPx = 2;                  // pixel stride for the tiny 3x3 probe

        static readonly Vector2Int[] PROBE_OFF = new Vector2Int[] {
    new(0,0), new(2,0), new(-2,0), new(0,2), new(0,-2), new(2,2), new(-2,2), new(2,-2), new(-2,-2)
};

        // Normalized (0..1) → screen px in *this* AR camera; honors optional Y-invert
        Vector2 ToScreen(Vector2 norm01)
        {
            if (invertY) norm01.y = 1f - norm01.y;
            return (Vector2)arCam.ViewportToScreenPoint(new Vector3(norm01.x, norm01.y, 0f));
        }

        /// <summary>
		/// Debug hitting depth raycast stuff.
		/// </summary>
        public GameObject depthHitDebugPrefab; // assign a tiny sphere in inspector
        [SerializeField] float debugSphereLifetime = 0.5f;


        // Keep only plausible depth and reject spikes
        bool AcceptDepth(float dz, float prev)
        {
            if (dz < depthClampMin || dz > depthClampMax) return false;
            if (prev > 0f && Mathf.Abs(dz - prev) > depthMaxJumpMeters) return false;
            return true;
        }
        #region Inspector
        [Header("Skeleton Visibility")]
        [SerializeField] bool shouldShowSkeleton = true;

        [Header("ManoMotion Joint Rotation Synthetisation enabled")]
        [SerializeField] bool driveRotations = false; // turn OFF

        [Tooltip("When true, the confidence check is bypassed while playing inside the Unity Editor so the skeleton is always visible for quick testing.")]
        [SerializeField] bool skipHidingInEditor = true;  // <-- New flag

        [Header("Smoothing & Prefabs")]
        [SerializeField] bool useSkeletonSmoothing = true;
        [SerializeField] GameObject skeletonPrefab2D;
        [SerializeField] GameObject skeletonPrefab3D;
        [SerializeField] GameObject skeletonPrefab3DSecond;
        #endregion

        // Internal data structures
        readonly List<GameObject> joints = new(new GameObject[21]);
        readonly List<GameObject> jointsSecond = new(new GameObject[21]);

        // Constants
        const float skeletonConfidenceThreshold = 0.0001f;
        const float FadeTime = 0.01f;          // Seconds to fade materials
        const float DisableThreshold = 0.01f;  // Alpha at which renderer.enabled = false
        const int JointsLength = 21;

        GameObject skeletonParent;
        GameObject skeletonModel, skeletonModelSecond;
        Renderer[] renderers, renderersSecond;

        readonly OneEuroFilterSetting positionFilterSetting = new(120, 0.0001f, 500f, 1f);
        readonly OneEuroFilterSetting depthFilterSetting = new(120, 0.0001f, 500f, 1f);
        readonly List<OneEuroFilter<Vector3>> positionFilter = new();
        readonly List<OneEuroFilter<Vector3>> positionFilterSecond = new();
        readonly OneEuroFilter<Vector2>[] handDepthFilters = new OneEuroFilter<Vector2>[2];
        readonly Quaternion[] handRotations = new Quaternion[2];


        // ---- External Depth Priming ----
        [Header("Depth Priming (per hand)")]
        [SerializeField] public bool hasEnvDepth = false;          // true on LiDAR iOS or ARCore Android
        [SerializeField, Range(0.1f, 1.5f)] public float rightHandDepthMeters = 0.25f;
        [SerializeField, Range(0.1f, 1.5f)] public float leftHandDepthMeters = 0.25f;
        [SerializeField, Range(0.05f, 1f)] float depthStaleTimeout = 0.25f; // secs before we fall back to default
                                                                            // ---- Depth Raycast Hook ----
        [SerializeField] ARRaycastManager raycastManager;
        [SerializeField] Camera arCam;
        [SerializeField] bool useDepthRaycast = true;  // only used if hasEnvDepth = true
        [SerializeField] bool invertY = true;          // set true if your 2D joints use top-left origin
        [SerializeField] int wristJointIndex = 0;      // you said wrist = index 0
        static readonly List<ARRaycastHit> _hits = new();



        // Internal book-keeping for externally fed depths
        readonly bool[] _hasDepthThisFrame = new bool[2];
        readonly float[] _lastDepthStamp = new float[2];

        // Optional: default for devices without environment depth (e.g., your 12 mini)
        [SerializeField, Range(0.1f, 1.5f)] float defaultDepthNoEnv = 0.25f;

        // Singleton (scoped to this class so it won't collide with the original)
        static SkeletonManagerOpenXR instance;
        public static SkeletonManagerOpenXR Instance => instance;

        public static UnityEvent OnSkeletonChanged = new();

        [Header("Platform Depth Offsets (depth detection only)")]
        [SerializeField] bool applyPlatformDepthOffsets = true;

        [Tooltip("Meters added when running on iOS builds (ARKit depth).")]
        [SerializeField, Range(-0.15f, 0.15f)] float iosDepthOffsetMeters = 0f;

        [Tooltip("Meters added when running on Android builds (ARCore depth).")]
        [SerializeField, Range(-0.15f, 0.15f)] float androidDepthOffsetMeters = 0.02f;

        [Tooltip("Clamp after offset is applied (min/max meters).")]
        [SerializeField] Vector2 platformDepthClampMeters = new Vector2(0.1f, 1.5f);

        [Header("Depth Diagnostics")]
        [SerializeField] bool enableDepthDiagnostics = true;
        [SerializeField, Range(0.1f, 5f)] float logCooldownSeconds = 1f;
        [SerializeField] bool drawDepthHitGizmo = false;


        [SerializeField] AROcclusionManager occlusionManager; // assign your AR Camera’s OcclusionManager
        float _nextDiagLogTime;
        Vector3 _lastDepthHitPos;

        [Header("Depth Update Policy")]
        [SerializeField] bool depthEveryFrame = false;          // set true if you want the old behavior
        [SerializeField, Range(1f, 60f)] float depthRefreshHz = 15f; // how often to query depth
        [SerializeField, Range(0f, 32f)] float minScreenMovePx = 3f; // only refresh if wrist moved this many pixels

        // per-hand bookkeeping
        float[] _lastDepthMeters = new float[2];
        float[] _nextDepthSampleAt = new float[2];
        Vector2[] _lastWristPx = new Vector2[2];



        #region Public properties
        public bool ShouldShowSkeleton
        {
            get => shouldShowSkeleton;
            set => shouldShowSkeleton = value;
        }
        #endregion


        bool ShouldRefreshDepth(int handIdx, Vector2 wristPx)
        {
            if (depthEveryFrame) return true;

            // time budget
            if (Time.unscaledTime < _nextDepthSampleAt[handIdx]) return false;

            // motion budget: avoid re-sampling if wrist barely moved on screen
            var last = _lastWristPx[handIdx];
            if (minScreenMovePx > 0f && last != Vector2.zero)
            {
                if ((wristPx - last).sqrMagnitude < (minScreenMovePx * minScreenMovePx))
                    return false;
            }
            return true;
        }

        void NoteDepthSampled(int handIdx, Vector2 wristPx)
        {
            _lastWristPx[handIdx] = wristPx;
            if (!depthEveryFrame && depthRefreshHz > 0f)
                _nextDepthSampleAt[handIdx] = Time.unscaledTime + (1f / depthRefreshHz);
        }



        void OnDrawGizmos()
        {
            if (!drawDepthHitGizmo) return;
            if (!Application.isPlaying) return;
            if (_lastDepthHitPos == Vector3.zero) return;
            global::UnityEngine.Gizmos.DrawWireSphere(_lastDepthHitPos, 0.01f);
        }

        void LogDepthIssue(string where, string reason, string fallback)
        {
            if (!enableDepthDiagnostics) return;
            if (Time.unscaledTime < _nextDiagLogTime) return;

            Debug.LogError($"[Skeleton/OpenXR][{where}] Depth unavailable: {reason}. Fallback → {fallback}");
            _nextDiagLogTime = Time.unscaledTime + logCooldownSeconds;
        }

        #region Unity lifecycle
        void Awake()
        {
            if (instance == null) instance = this;
            else { gameObject.SetActive(false); Debug.LogWarning("Multiple SkeletonManagerOpenXR detected – disabling duplicate."); return; }

            CreateSkeletonParent();
            ChangeSkeletonModel(SkeletonModel.SKEL_3D);
            CreateOneEuroFilters();
            if (hasEnvDepth)
            {
                var occ = arCam ? arCam.GetComponent<AROcclusionManager>() : null;
                var desc = raycastManager ? raycastManager.descriptor : null;
                bool depthModeOn = occ && occ.requestedEnvironmentDepthMode != EnvironmentDepthMode.Disabled;
                bool typeSupported = desc != null && (desc.supportedTrackableTypes & TrackableType.Depth) != 0;

                if (!depthModeOn || !typeSupported)
                    Debug.LogWarning("[EnvDepth scene] Depth not actually available/enabled. Falling back to per-hand meters.");
            }
            else
            {
                // Optional: warn if Occlusion is accidentally enabled in the fixed-depth scene
                var occ = arCam ? arCam.GetComponent<AROcclusionManager>() : null;
                if (occ && occ.requestedEnvironmentDepthMode != EnvironmentDepthMode.Disabled)
                    Debug.LogWarning("[FixedDepth scene] OcclusionManager is enabled but hasEnvDepth=false. Depth rays are off by design.");
            }
        }

        void OnEnable() => ManoMotionManager.OnSkeletonActivated += ChangeSkeletonModel;
        void OnDisable() => ManoMotionManager.OnSkeletonActivated -= ChangeSkeletonModel;
        #endregion

        #region Initialisation helpers
        void CreateSkeletonParent()
        {
            skeletonParent = new GameObject("SkeletonParent_OpenXR"); // only name differs
        }

        void CreateOneEuroFilters()
        {
            for (int i = 0; i < handDepthFilters.Length; i++)
                handDepthFilters[i] = new OneEuroFilter<Vector2>(depthFilterSetting);

            for (int i = 0; i < JointsLength; i++)
            {
                positionFilter.Add(new OneEuroFilter<Vector3>(positionFilterSetting));
                positionFilterSecond.Add(new OneEuroFilter<Vector3>(positionFilterSetting));
            }
        }

        void ChangeSkeletonModel(SkeletonModel skeleton)
        {
            GameObject prefab = skeleton == SkeletonModel.SKEL_3D ? skeletonPrefab3D : skeletonPrefab2D;

            joints.Clear();
            jointsSecond.Clear();
            Destroy(skeletonModel);
            Destroy(skeletonModelSecond);

            skeletonModel = Instantiate(prefab, skeletonParent.transform);
            skeletonModel.name = skeletonModel.tag = "Right";

            skeletonModelSecond = Instantiate(skeletonPrefab3DSecond, skeletonParent.transform);
            skeletonModelSecond.name = skeletonModelSecond.tag = "Left";

            for (int i = 0; i < skeletonModel.transform.childCount; i++)
            {
                joints.Add(skeletonModel.transform.GetChild(i).gameObject);
                jointsSecond.Add(skeletonModelSecond.transform.GetChild(i).gameObject);
            }

            renderers = skeletonModel.GetComponentsInChildren<Renderer>();
            renderersSecond = skeletonModelSecond.GetComponentsInChildren<Renderer>();

            OnSkeletonChanged?.Invoke();
        }
        #endregion

        #region Update loop
        void Update()
        {
            HandInfo[] info = ManoMotionManager.Instance.HandInfos;
            for (int i = 0; i < info.Length; i++)
            {
                Renderer[] rends = (i == 0) ? renderers : renderersSecond;
                List<GameObject> list = GetJoints(i);
                List<OneEuroFilter<Vector3>> pFilt = (i == 0) ? positionFilter : positionFilterSecond;

                FadeSkeletonJoints(rends, info[i]);
                UpdateJointsPosition(ref list, info[i], pFilt, handDepthFilters[i]);
                if (driveRotations) UpdateJointsOrientation(ref list, info[i], i); // unchanged
            }
        }
        #endregion
        #region Joint updates
        /*OLD JOINT UPDATER WITHOUT REALTIME DEPTH OPTION
        void UpdateJointsPosition(ref List<GameObject> skelJoints, HandInfo hInfo, List<OneEuroFilter<Vector3>> filters, OneEuroFilter<Vector2> depthFilt)
        {
            SkeletonInfo s = hInfo.trackingInfo.skeleton;
            if (s.jointPositions == null) return;

            WorldSkeletonInfo ws = hInfo.trackingInfo.worldSkeleton;
            // Fixed distance allows us perfect depth Estimation.
            // Blunt but works very well, and the hand should be at that distanc either way.
            float depth = 0.25f;// Mathf.Clamp(hInfo.trackingInfo.depthEstimation, 0.1f, 1f);
            //if (useSkeletonSmoothing) depth = depthFilt.Filter(new Vector2(depth, 0)).x;

            for (int i = 0; i < s.jointPositions.Length; i++)
            {
                Vector3 jp = s.jointPositions[i];
                jp.z = ws.jointPositions[i].z * 0.3f;
                Vector3 pos = ManoUtils.Instance.CalculateNewPositionWithDepth(jp, depth);
                if (useSkeletonSmoothing) pos = filters[i].Filter(pos);
                skelJoints[i].transform.position = pos;
            }
        }*/

        /*void UpdateJointsPosition(ref List<GameObject> skelJoints, HandInfo hInfo,
    List<OneEuroFilter<Vector3>> filters, OneEuroFilter<Vector2> depthFilt)
        {
            SkeletonInfo s = hInfo.trackingInfo.skeleton;
            if (s.jointPositions == null) return;

            WorldSkeletonInfo ws = hInfo.trackingInfo.worldSkeleton;

            // ---- Decide depth for this hand (simple, per your new inspector vars) ----
            float depth;
            if (!hasEnvDepth)
            {
                // Lidless iPhones etc. -> fixed default (0.25 m)
                depth = defaultDepthNoEnv;                         // keep your current behavior
            }
            else
            {
                // Prefer SDK's hand side; fallback to which joint list we're updating
                string side = hInfo.gestureInfo.handSide.ToString(); // "Right"/"Left"/"Unknown"
                bool isRight =
                    side.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || object.ReferenceEquals(skelJoints, joints);   // fallback if Unknown
                depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;
                
                // Optional: smooth the per-hand depth you just selected
                if (useSkeletonSmoothing && depthFilt != null)  //kk
                    depth = depthFilt.Filter(new Vector2(depth, 1f)).x;
            }

            // ---- Position joints (unchanged, plus your ws influence) ----
            for (int i = 0; i < s.jointPositions.Length; i++)
            {
                Vector3 jp = s.jointPositions[i];
                jp.z = ws.jointPositions[i].z * 0.3f;

                Vector3 pos = ManoUtils.Instance.CalculateNewPositionWithDepth(jp, depth);
                if (useSkeletonSmoothing) pos = filters[i].Filter(pos);

                skelJoints[i].transform.position = pos;
            }
        }
        //V3
        void UpdateJointsPosition(ref List<GameObject> skelJoints, HandInfo hInfo,
    List<OneEuroFilter<Vector3>> filters, OneEuroFilter<Vector2> depthFilt)
        {
            SkeletonInfo s = hInfo.trackingInfo.skeleton;
            if (s.jointPositions == null) return;

            WorldSkeletonInfo ws = hInfo.trackingInfo.worldSkeleton;

            // --- Decide depth for THIS hand ---
            float depth = defaultDepthNoEnv; // your 0.25f default for lidless devices

            if (hasEnvDepth)
            {
                // Try depth-only raycast at the wrist pixel
                if (TryDepthRaycastAtWrist(hInfo, out var dz))
                {
                    depth = dz;
                    if (useSkeletonSmoothing && depthFilt != null)
                        depth = depthFilt.Filter(new Vector2(depth, 1f)).x; // optional smoothing
                }
                else
                {
                    // If you want per-hand manual override while testing:
                    bool isRight =
                        hInfo.gestureInfo.handSide.ToString().IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || object.ReferenceEquals(skelJoints, joints); // fallback if Unknown

                    depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;
                }
            }

            // --- Position joints (same as before) ---
            for (int i = 0; i < s.jointPositions.Length; i++)
            {
                Vector3 jp = s.jointPositions[i];
                jp.z = ws.jointPositions[i].z * 0.3f; // keep your influence if you like

                Vector3 pos = ManoUtils.Instance.CalculateNewPositionWithDepth(jp, depth);
                if (useSkeletonSmoothing) pos = filters[i].Filter(pos);
                skelJoints[i].transform.position = pos;
            }
        }*/
        /*
        void UpdateJointsPosition(ref List<GameObject> skelJoints, HandInfo hInfo,
    List<OneEuroFilter<Vector3>> filters, OneEuroFilter<Vector2> depthFilt) // depthFilt unused now
        {
            SkeletonInfo s = hInfo.trackingInfo.skeleton;
            if (s.jointPositions == null) return;

            WorldSkeletonInfo ws = hInfo.trackingInfo.worldSkeleton;
            /*V1
            // ---- Pick depth for THIS hand ----
            float depth = defaultDepthNoEnv; // e.g., 0.25 m for lidless iPhones

            if (hasEnvDepth)
            {
                // 1) Try depth raycast at the wrist pixel
                if (TryDepthRaycastAtWrist(hInfo, out var dz))
                {
                    depth = dz;
                }
                else
                {
                    // 2) Fallback: per-hand inspector floats
                    var sideStr = hInfo.gestureInfo.handSide.ToString(); // "Right"/"Left"/"Unknown"
                    bool isRight = sideStr.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;
                }
            }
            */
        /*V2
        float depth = defaultDepthNoEnv; // fixed default for non-depth scene

        if (hasEnvDepth)
        {
            var res = TryDepthRaycastAtWrist(hInfo, out var dz, out var err);
            if (res == DepthResult.Success)
            {
                depth = dz;
            }
            else
            {
                // Pick the per-hand inspector meters if env depth scene but no hit this frame
                var sideStr = hInfo.gestureInfo.handSide.ToString();
                bool isRight = sideStr.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;

                // Log once in a while so you know *why* it failed and what fallback was used
                LogDepthIssue("UpdateJointsPosition", $"{err} [{res}]", isRight ? "rightHandDepthMeters" : "leftHandDepthMeters");
            }
        }*//*

        //V3 (Custom depth update frequency)
        float depth = defaultDepthNoEnv; // fixed default for non-depth scene

        if (hasEnvDepth)
        {
            // compute wrist screen pos once to drive policy + raycast
            Vector3 wristNorm = s.jointPositions[wristJointIndex];
            Vector2 wristPx = ManoUtils.Instance.CalculateScreenPosition(wristNorm);

            int handIdx = ReferenceEquals(skelJoints, joints) ? 0 : 1;

            bool trySample = ShouldRefreshDepth(handIdx, wristPx);

            if (trySample)
            {
                var res = TryDepthRaycastAtWrist(hInfo, out var dz, out var err);
                if (res == DepthResult.Success)
                {
                    _lastDepthMeters[handIdx] = dz; // cache last good depth
                    depth = dz;
                }
                else
                {
                    // fall back to per-hand meters for this frame
                    var sideStr = hInfo.gestureInfo.handSide.ToString();
                    bool isRight = sideStr.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;

                    LogDepthIssue("UpdateJointsPosition", $"{err} [{res}]",
                        isRight ? "rightHandDepthMeters" : "leftHandDepthMeters");
                }
                NoteDepthSampled(handIdx, wristPx);
            }
            else
            {
                // reuse last known depth; if none yet, fall back to per-hand meters
                float cached = _lastDepthMeters[handIdx];
                if (cached > 0f)
                {
                    depth = cached;
                }
                else
                {
                    var sideStr = hInfo.gestureInfo.handSide.ToString();
                    bool isRight = sideStr.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;
                }
            }
        }




        // ---- Apply to joints (unchanged) ----
        for (int i = 0; i < s.jointPositions.Length; i++)
        {
            Vector3 jp = s.jointPositions[i];
            jp.z = ws.jointPositions[i].z * 0.3f;

            Vector3 pos = ManoUtils.Instance.CalculateNewPositionWithDepth(jp, depth);
            if (useSkeletonSmoothing) pos = filters[i].Filter(pos);
            skelJoints[i].transform.position = pos;
        }
    }*/

        void UpdateJointsPosition(
    ref List<GameObject> skelJoints,
    HandInfo hInfo,
    List<OneEuroFilter<Vector3>> filters,
    OneEuroFilter<Vector2> depthFilt // unused
)
        {
            // Structs aren’t null; guard by data presence
            SkeletonInfo s = hInfo.trackingInfo.skeleton;
            if (s.jointPositions == null || s.jointPositions.Length == 0) return;

            WorldSkeletonInfo ws = hInfo.trackingInfo.worldSkeleton;
            bool hasWorldZ = ws.jointPositions != null && ws.jointPositions.Length >= s.jointPositions.Length;

            // default until we get a valid sample
            float depth = defaultDepthNoEnv;

            if (hasEnvDepth)
            {
                if (wristJointIndex >= 0 && wristJointIndex < s.jointPositions.Length)
                {
                    // wrist → screen pixel (camera-aware + optional Y flip)
                    Vector3 wristNorm = s.jointPositions[wristJointIndex];
                    Vector2 wristPx = ToScreen(new Vector2(wristNorm.x, wristNorm.y));

                    int handIdx = ReferenceEquals(skelJoints, joints) ? 0 : 1;

                    bool trySample = ShouldRefreshDepth(handIdx, wristPx);
                    if (trySample)
                    {
                        var res = TryDepthRaycastAtWrist(hInfo, out var dz, out var err);
                        if (res == DepthResult.Success && AcceptDepth(dz, _lastDepthMeters[handIdx]))
                        {
                            _lastDepthMeters[handIdx] = dz; // cache
                            depth = dz;
                        }
                        else
                        {
                            // per-hand inspector fallback for this frame
                            var sideStr = hInfo.gestureInfo.handSide.ToString();
                            bool isRight = sideStr.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                            depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;

                            LogDepthIssue(
                                "UpdateJointsPosition",
                                (res == DepthResult.Success) ? $"Rejected depth {dz:0.000} m" : $"{err} [{res}]",
                                isRight ? "rightHandDepthMeters" : "leftHandDepthMeters"
                            );
                        }
                        NoteDepthSampled(handIdx, wristPx);
                    }
                    else
                    {
                        // reuse last known depth; if none yet, per-hand fallback
                        float cached = _lastDepthMeters[handIdx];
                        if (cached > 0f) depth = cached;
                        else
                        {
                            var sideStr = hInfo.gestureInfo.handSide.ToString();
                            bool isRight = sideStr.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                            depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;
                        }
                    }
                }
                else
                {
                    // invalid wrist index → per-hand fallback
                    var sideStr = hInfo.gestureInfo.handSide.ToString();
                    bool isRight = sideStr.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    depth = isRight ? rightHandDepthMeters : leftHandDepthMeters;
                    LogDepthIssue("UpdateJointsPosition", "Wrist index invalid", isRight ? "rightHandDepthMeters" : "leftHandDepthMeters");
                }
            }

            // safety clamp before applying to joints
            depth = Mathf.Clamp(depth, depthClampMin, depthClampMax);

            // apply to joints
            int n = Mathf.Min(s.jointPositions.Length, skelJoints.Count);
            for (int i = 0; i < n; i++)
            {
                Vector3 jp = s.jointPositions[i];
                if (hasWorldZ) jp.z = ws.jointPositions[i].z * 0.3f; // only if world Z available

                Vector3 pos = ManoUtils.Instance.CalculateNewPositionWithDepth(jp, depth);
                if (useSkeletonSmoothing) pos = filters[i].Filter(pos);
                skelJoints[i].transform.position = pos;
            }
        }





        void UpdateJointsOrientation(ref List<GameObject> list, HandInfo hInfo, int handIdx)
        {
            if (hInfo.trackingInfo.skeleton.confidence == 0) return;
            Quaternion handRot = RotationUtility.GetHandRotation(hInfo);
            handRotations[handIdx] = handRot;

            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].TryGetComponent(out LookAtJoint joint)) continue;
                Quaternion rot = handRot;
                if (i > 0) rot *= RotationUtility.GetFingerJointRotation(hInfo, i);
                joint.UpdateRotation(rot, hInfo.gestureInfo.handSide);
            }
        }


        // ---------------
        // Fade & disable
        // ---------------
        void FadeSkeletonJoints(Renderer[] rends, HandInfo hInfo)
        {
            bool hasConf = hInfo.trackingInfo.skeleton.confidence > skeletonConfidenceThreshold;

#if UNITY_EDITOR
            if (skipHidingInEditor) hasConf = true; // Ignore confidence in editor if flag set
#endif

            float alphaStep = (1f / FadeTime) * Time.deltaTime * (hasConf && shouldShowSkeleton ? 1f : -1f);

            foreach (Renderer r in rends)
            {
                if (hasConf && !r.enabled) r.enabled = true; // ensure visible when we regain conf

                // If material has no _Color skip alpha dance and just use enable/disable
                if (!r.material.HasProperty("_Color"))
                {
                    if (!hasConf && r.enabled) r.enabled = false;
                    continue;
                }

                Color c = r.material.color;
                c.a = Mathf.Clamp01(c.a + alphaStep);
                r.material.color = c;
                if (c.a <= DisableThreshold && r.enabled) r.enabled = false;
            }
        }
        #endregion

        #region Public helpers
        public Quaternion GetHandRotation(int idx) => handRotations[idx];
        public GameObject GetJoint(int handIdx, int jointIdx) => handIdx switch { 0 => joints[jointIdx], 1 => jointsSecond[jointIdx], _ => null };
        public List<GameObject> GetJoints(int handIdx) => handIdx switch { 0 => joints, 1 => jointsSecond, _ => null };
        #endregion

        /*bool TryDepthRaycastAtWrist(HandInfo hInfo, out float depthMeters)
        {
            depthMeters = 0f;

            if (!hasEnvDepth || !useDepthRaycast || raycastManager == null || arCam == null)
                return false;

            var desc = raycastManager.descriptor;
            if (desc == null || (desc.supportedTrackableTypes & TrackableType.Depth) == 0)
                return false;

            var s = hInfo.trackingInfo.skeleton;
            if (s.jointPositions == null || wristJointIndex < 0 || wristJointIndex >= s.jointPositions.Length)
                return false;

            // ManoMotion joints are typically normalized (0..1). If yours are already pixels, skip the scaling.
            Vector3 wristNorm = s.jointPositions[wristJointIndex];
            _hits.Clear();
            if (!raycastManager.Raycast(ManoUtils.Instance.CalculateScreenPosition(wristNorm), _hits, TrackableType.Depth))
                return false;



            var hit = _hits[0];
            depthMeters = Vector3.Distance(arCam.transform.position, hit.pose.position);

            // --- Platform-specific tweak for depth detection ---
            // No LiDAR capability check: devs choose the mode; we just offset by platform.
            if (applyPlatformDepthOffsets)
            {
#if UNITY_IOS
                depthMeters = Mathf.Clamp(
                    depthMeters + iosDepthOffsetMeters,
                    platformDepthClampMeters.x,
                    platformDepthClampMeters.y
                );
#elif UNITY_ANDROID
    depthMeters = Mathf.Clamp(
        depthMeters + androidDepthOffsetMeters,
        platformDepthClampMeters.x,
        platformDepthClampMeters.y
    );
#endif
            }


            return depthMeters > 0f && !float.IsNaN(depthMeters);
        }*/
        enum DepthResult
        {
            Success,
            Disabled,                // useDepthRaycast false or hasEnvDepth false
            NoCamera,
            NoRaycastManager,
            NoDescriptorOrType,
            NoOcclusionOrModeOff,    // Occlusion missing or depth mode Disabled
            NoSkeletonOrIndex,       // wrist index invalid
            RaycastMiss,
            DepthInactive,      // NEW: current mode disabled (e.g., non-LiDAR iPhone)
            NoDepthTexture,     // NEW: provider gave no texture this frame
            InvalidDistance

        }
        /*
        DepthResult TryDepthRaycastAtWrist(HandInfo hInfo, out float depthMeters, out string error)
        {
            depthMeters = 0f;
            error = null;

            // Non-LiDAR iPhones will sit here:
            if (occlusionManager.currentEnvironmentDepthMode == EnvironmentDepthMode.Disabled)
            { error = "currentEnvironmentDepthMode is Disabled (device lacks env depth)"; return DepthResult.DepthInactive; }

            // Depth not produced this frame (can happen intermittently)
            if (occlusionManager.environmentDepthTexture == null)
            { error = "environmentDepthTexture is null this frame"; return DepthResult.NoDepthTexture; }

            if (!hasEnvDepth || !useDepthRaycast) { error = "Depth raycast disabled by inspector"; return DepthResult.Disabled; }
            if (!arCam) { error = "AR camera not assigned"; return DepthResult.NoCamera; }
            if (!raycastManager) { error = "ARRaycastManager missing"; return DepthResult.NoRaycastManager; }

            // Provider/type support
            var desc = raycastManager.descriptor;
            if (desc == null || (desc.supportedTrackableTypes & TrackableType.Depth) == 0)
            {
                error = "Provider does not report TrackableType.Depth support";
                return DepthResult.NoDescriptorOrType;
            }

            // Occlusion / depth mode check (prevents common misconfig)
            if (!occlusionManager || occlusionManager.requestedEnvironmentDepthMode == EnvironmentDepthMode.Disabled)
            {
                error = "AROcclusionManager missing or depth mode Disabled";
                return DepthResult.NoOcclusionOrModeOff;
            }

            var s = hInfo.trackingInfo.skeleton;
            if (s.jointPositions == null || wristJointIndex < 0 || wristJointIndex >= s.jointPositions.Length)
            {
                error = "Skeleton null or wrist index out of range";
                return DepthResult.NoSkeletonOrIndex;
            }

            Vector3 wristNorm = s.jointPositions[wristJointIndex]; // assumed normalized 0..1 -> ManoUtils converts
            _hits.Clear();
            if (!raycastManager.Raycast(ManoUtils.Instance.CalculateScreenPosition(wristNorm), _hits, TrackableType.Depth))
            {
                error = "Raycast(Depth) returned no hits";
                return DepthResult.RaycastMiss;
            }

            var hit = _hits[0];
            //_lastDepthHitPos = hit.pose.position;
            //depthMeters = Vector3.Distance(arCam.transform.position, hit.pose.position);
            _lastDepthHitPos = hit.pose.position;

            // preferred: camera-space Z depth
            depthMeters = arCam.transform.InverseTransformPoint(hit.pose.position).z;
            // (equivalent to: Vector3.Dot(hit.pose.position - arCam.transform.position, arCam.transform.forward))



            if (float.IsNaN(depthMeters) || depthMeters <= 0f)
            {
                error = $"Computed distance invalid ({depthMeters})";
                return DepthResult.InvalidDistance;
            }

            // Platform-specific offset & clamp (optional; as in your code)
#if UNITY_IOS
            if (applyPlatformDepthOffsets)
                depthMeters = Mathf.Clamp(depthMeters + iosDepthOffsetMeters, platformDepthClampMeters.x, platformDepthClampMeters.y);
#elif UNITY_ANDROID
            if (applyPlatformDepthOffsets)
                depthMeters = Mathf.Clamp(depthMeters + androidDepthOffsetMeters, platformDepthClampMeters.x, platformDepthClampMeters.y);
#endif

            return DepthResult.Success;
        }
        DepthResult TryDepthRaycastAtWrist(HandInfo hInfo, out float depthMeters, out string error)
{
    depthMeters = 0f; error = null;

    // quick guards
    if (!hasEnvDepth || !useDepthRaycast) { error = "Depth raycast disabled by inspector"; return DepthResult.Disabled; }
    if (!arCam) { error = "AR camera not assigned"; return DepthResult.NoCamera; }
    if (!raycastManager) { error = "ARRaycastManager missing"; return DepthResult.NoRaycastManager; }
    if (!occlusionManager || occlusionManager.requestedEnvironmentDepthMode == EnvironmentDepthMode.Disabled)
    { error = "AROcclusionManager missing or depth mode Disabled"; return DepthResult.NoOcclusionOrModeOff; }

    var desc = raycastManager.descriptor;
    if (desc == null || (desc.supportedTrackableTypes & TrackableType.Depth) == 0)
    { error = "Provider does not report TrackableType.Depth support"; return DepthResult.NoDescriptorOrType; }

    var s = hInfo.trackingInfo.skeleton;
    if (s.jointPositions == null || wristJointIndex < 0 || wristJointIndex >= s.jointPositions.Length)
    { error = "Skeleton null or wrist index out of range"; return DepthResult.NoSkeletonOrIndex; }

    // build screen pixel in the AR camera’s space
    Vector3 wristNorm3 = s.jointPositions[wristJointIndex];
    Vector2 screenPos  = ToScreen(new Vector2(wristNorm3.x, wristNorm3.y));

    _hits.Clear();
    bool gotHit = raycastManager.Raycast(screenPos, _hits, TrackableType.Depth);

#if UNITY_ANDROID
    // small 3x3 neighborhood probe to dodge depth holes
    if (!gotHit && probeNeighborhoodOnMiss)
    {
        for (int i = 1; i < PROBE_OFF.Length && !gotHit; i++)
        {
            var p = screenPos + (Vector2)PROBE_OFF[i] * probeStepPx;
            gotHit = raycastManager.Raycast(p, _hits, TrackableType.Depth);
        }
    }

    // optional plane/feature fallback
    if (!gotHit && androidPlanePointFallback)
        gotHit = raycastManager.Raycast(screenPos, _hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint);
#endif

    if (!gotHit) { error = "Raycast miss (Depth/Planes/Points)"; return DepthResult.RaycastMiss; }

    var hit = _hits[0];
    _lastDepthHitPos = hit.pose.position;

    // camera-space Z is stable across providers
    depthMeters = arCam.transform.InverseTransformPoint(hit.pose.position).z;
    if (!(depthMeters > 0f) || float.IsNaN(depthMeters))
    { error = $"Invalid depth: {depthMeters}"; return DepthResult.InvalidDistance; }

    // optional platform offset + clamp (broad)
#if UNITY_IOS
    if (applyPlatformDepthOffsets)
        depthMeters = Mathf.Clamp(depthMeters + iosDepthOffsetMeters, platformDepthClampMeters.x, platformDepthClampMeters.y);
#elif UNITY_ANDROID
    if (applyPlatformDepthOffsets)
        depthMeters = Mathf.Clamp(depthMeters + androidDepthOffsetMeters, platformDepthClampMeters.x, platformDepthClampMeters.y);
#endif

    return DepthResult.Success;
}*/
        DepthResult TryDepthRaycastAtWrist(HandInfo hInfo, out float depthMeters, out string error)
        {
            depthMeters = 0f;
            error = null;

            // quick guards (you already had these)
            if (!hasEnvDepth || !useDepthRaycast)
            {
                error = "Depth raycast disabled by inspector";
                return DepthResult.Disabled;
            }
            if (!arCam)
            {
                error = "AR camera not assigned";
                return DepthResult.NoCamera;
            }
            if (!raycastManager)
            {
                error = "ARRaycastManager missing";
                return DepthResult.NoRaycastManager;
            }
            if (!occlusionManager)
            {
                error = "AROcclusionManager reference missing";
                return DepthResult.NoOcclusionOrModeOff;
            }

            // NEW: distinguish requested vs current + check texture actually exists
            if (occlusionManager.requestedEnvironmentDepthMode == EnvironmentDepthMode.Disabled)
            {
                error = "requestedEnvironmentDepthMode is Disabled";
                return DepthResult.NoOcclusionOrModeOff;
            }

            if (occlusionManager.currentEnvironmentDepthMode == EnvironmentDepthMode.Disabled)
            {
                error = "currentEnvironmentDepthMode is Disabled (device not providing env depth)";
                return DepthResult.DepthInactive;
            }

            if (occlusionManager.environmentDepthTexture == null)
            {
                error = "environmentDepthTexture is null this frame";
                return DepthResult.NoDepthTexture;
            }

            // Provider/type support (you already had this)
            var desc = raycastManager.descriptor;
            if (desc == null || (desc.supportedTrackableTypes & TrackableType.Depth) == 0)
            {
                error = "Provider does not report TrackableType.Depth support";
                return DepthResult.NoDescriptorOrType;
            }

            // Skeleton / wrist check (you already had this)
            var s = hInfo.trackingInfo.skeleton;
            if (s.jointPositions == null || wristJointIndex < 0 || wristJointIndex >= s.jointPositions.Length)
            {
                error = "Skeleton null or wrist index out of range";
                return DepthResult.NoSkeletonOrIndex;
            }

            // build screen pixel in the AR camera’s space
            Vector3 wristNorm3 = s.jointPositions[wristJointIndex];

            // NEW: use Mano mapping on Android to rule out ToScreen issues
            Vector2 screenPos;
#if UNITY_ANDROID
    screenPos = ManoUtils.Instance.CalculateScreenPosition(wristNorm3);
#else
            screenPos = ToScreen(new Vector2(wristNorm3.x, wristNorm3.y));
#endif

            _hits.Clear();
            bool gotHit = raycastManager.Raycast(screenPos, _hits, TrackableType.Depth);

#if UNITY_ANDROID
    // your existing small 3x3 neighborhood probe to dodge depth holes
    if (!gotHit && probeNeighborhoodOnMiss)
    {
        for (int i = 1; i < PROBE_OFF.Length && !gotHit; i++)
        {
            var p = screenPos + (Vector2)PROBE_OFF[i] * probeStepPx;
            gotHit = raycastManager.Raycast(p, _hits, TrackableType.Depth);
        }
    }

    // your existing plane/feature fallback
    if (!gotHit && androidPlanePointFallback)
        gotHit = raycastManager.Raycast(screenPos, _hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint);
#endif

            if (!gotHit)
            {
                error = $"Raycast miss (Depth/Planes/Points) at {screenPos}";
                return DepthResult.RaycastMiss;
            }

            var hit = _hits[0];
            _lastDepthHitPos = hit.pose.position;

            // NEW: visual debug sphere at hit position (optional)
            if (depthHitDebugPrefab != null)
            {
                var go = Instantiate(depthHitDebugPrefab, hit.pose.position, Quaternion.identity);
                if (debugSphereLifetime > 0f)
                    Destroy(go, debugSphereLifetime);
            }

            // camera-space Z is stable across providers (you already had this)
            depthMeters = arCam.transform.InverseTransformPoint(hit.pose.position).z;
            if (!(depthMeters > 0f) || float.IsNaN(depthMeters))
            {
                error = $"Invalid depth: {depthMeters}";
                return DepthResult.InvalidDistance;
            }

            // optional platform offset + clamp (broad, you already had this)
#if UNITY_IOS
    if (applyPlatformDepthOffsets)
        depthMeters = Mathf.Clamp(
            depthMeters + iosDepthOffsetMeters,
            platformDepthClampMeters.x,
            platformDepthClampMeters.y
        );
#elif UNITY_ANDROID
    if (applyPlatformDepthOffsets)
        depthMeters = Mathf.Clamp(
            depthMeters + androidDepthOffsetMeters,
            platformDepthClampMeters.x,
            platformDepthClampMeters.y
        );
#endif

            return DepthResult.Success;
        }
    }




}
