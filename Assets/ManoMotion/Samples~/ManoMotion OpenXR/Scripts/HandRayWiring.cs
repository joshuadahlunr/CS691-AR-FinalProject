// Assets/Input/HandRayWiring.cs
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
///
/// HandRayWiring takes some source-of-truth pose for this hand,
/// stabilizes / biases it, and feeds that into the NearFarInteractor as the ray origin.
/// Now supports multiple aim modes (direct hand, camera+pinch, stable frame).
/// </summary>
namespace ManoMotion.OpenXR
{
    [DisallowMultipleComponent]
    public class HandRayWiring : MonoBehaviour
    {
        // ---------------------------
        //  Public config
        // ---------------------------

        [Header("References")]
        [Tooltip("NearFarInteractor on this hand. If left null, we'll search by tag.")]
        public NearFarInteractor interactor;

        [Tooltip("Transform whose forward is the raw aim (usually a palm/aim child). Used in HandPoseDirect mode.")]
        public Transform aimPose;

        // NEW: Hybrid pinch mode references
        [Header("CameraThumbHybrid refs")]
        [Tooltip("Main AR/VR camera (center eye). Used in CameraThumbHybrid mode.")]
        public Transform cameraRoot;

        [Tooltip("Thumb tip (world space tracked joint). Used in CameraThumbHybrid mode.")]
        public Transform thumbTip;

        [Tooltip("Index tip (world space tracked joint). Used in CameraThumbHybrid mode.")]
        public Transform indexTip;

        // NEW: StableSpace mode references
        [Header("StableSpace refs")]
        [Tooltip("Stable frame origin. Think: a root anchor you consider '0,0,0' in your app's logical space.")]
        public Transform stableOrigin;

        [Tooltip("A second point that defines 'forward' direction for that stable frame. (forward = (forwardRef - origin).normalized).")]
        public Transform stableForwardRef;

        [Tooltip("Optional explicit up for the stable frame. If null, Vector3.up is used.")]
        public Transform stableUpOverride;

        [Tooltip("Hand root (wrist/palm) in world space for StableSpace mode.")]
        public Transform handRoot;

        // NEW: runtime mode select
        public enum AimMode
        {
            HandPoseDirect,    // current behavior (aimPose local offsets)
            CameraThumbHybrid, // camera-forward laser from pinch cluster
            StableSpace        // 6DoF hand pose, but positional offset applied in a stable coordinate frame
        }

        [Header("Aim Mode")]
        public AimMode aimMode = AimMode.HandPoseDirect;

        [Header("Auto-discovery")]
        public bool findInteractorByTag = true;

        [Header("Auto-discovery Tags (optional)")]
        public string interactorTag = "LeftHandInteractor";  // already existed
        public string cameraTag = "MainCamera";
        public string thumbTipTag = "LeftThumbTip";
        public string indexTipTag = "LeftIndexTip";
        public string handRootTag = "LeftHandRoot";
        public string stableOriginTag = "StableOrigin";
        public string stableForwardRefTag = "StableForwardRef";
        public string stableUpTag = "StableUp";


        public bool retryUntilFound = true;
        public float retryTimeoutSeconds = 5f;

        [Header("Filtering (framerate-independent)")]
        [Tooltip("Position smoothing time-constant (seconds). 0 = off")]
        public float posTau = 0.06f;   // ~60 ms feels stable but responsive

        [Tooltip("Rotation smoothing time-constant (seconds). 0 = off")]
        public float rotTau = 0.06f;

        [Tooltip("Ignore tiny position changes (meters).")]
        public float posDeadzone = 0.0015f;   // 1.5 mm

        [Tooltip("Ignore tiny rotation changes (degrees).")]
        public float rotDeadzoneDeg = 0.8f;   // < 1 degree

        [Header("Forward normalization")]
        [Tooltip("Keep ray mostly horizontal and slightly tilted down.")]
        public bool clampPitchToWorld = true;

        [Tooltip("Downward bias added to pitch (degrees). Negative aims downward).")]
        public float pitchBiasDeg = -5f;

        [Tooltip("Max up tilt allowed (deg) relative to horizontal plane.")]
        public float maxPitchUpDeg = 8f;

        [Tooltip("Max down tilt allowed (deg) relative to horizontal plane.")]
        public float maxPitchDownDeg = 25f;

        [Tooltip("Optional up reference. If null we use world up. Often your HMD/Camera root.")]
        public Transform upReference; // e.g., Camera.main.transform

        [Header("Offsets")]
        [Tooltip("Pos offset applied in local hand space (HandPoseDirect), or in stable frame space (StableSpace), or none (CameraThumbHybrid uses pinch directly).")]
        public Vector3 localPositionOffset = Vector3.zero;

        [Tooltip("Euler rot offset applied around the raw source rotation (HandPoseDirect, StableSpace).")]
        public Vector3 localEulerOffset = Vector3.zero;

        [Header("Debug")]
        public bool logOnceWhenWired = true;
        public bool drawGizmos = false;
        public float gizmoLength = 0.25f;

        // ---- runtime ----
        Transform _rayAnchor;               // smoothed output we give to the interactor
        Vector3 _vel;                       // SmoothDamp velocity for position
        Quaternion _smoothedRot;
        bool _wired;


        [Header("Pinch Stability (CameraThumbHybrid)")]
        [Tooltip("Distance (meters) between thumb/index to start 'locked' click.")]
        public float pinchStartDistance = 0.03f; // ~3 cm, tune

        [Tooltip("Distance (meters) to release lock (should be a bit larger than start).")]
        public float pinchReleaseDistance = 0.04f; // hysteresis

        // runtime lock state
        bool _pinchHeld;
        Vector3 _pinchLockedPos;
        Quaternion _pinchLockedRot;

        [Header("Smoothing")]
        public bool smoothInCustomSpace = false; // default off to keep current behavior

        void Reset()
        {
            TryGetComponent(out interactor);
            if (!aimPose) aimPose = transform;
        }

        void Start()
        {
            StartCoroutine(EnsureWiredRoutine());
        }


        void TryAutoResolveRefs()
        {
            // Interactor
            if (interactor == null && !string.IsNullOrEmpty(interactorTag))
            {
                var go = GameObject.FindWithTag(interactorTag);
                if (go) interactor = go.GetComponent<NearFarInteractor>();
                if (interactor == null)
                {
#if UNITY_2022_2_OR_NEWER
                    var all = Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<NearFarInteractor>(true);
#endif
                    foreach (var cand in all)
                    {
                        if (cand && cand.gameObject.CompareTag(interactorTag))
                        {
                            interactor = cand;
                            break;
                        }
                    }
                }
            }

            // Camera root
            if (!cameraRoot && !string.IsNullOrEmpty(cameraTag))
            {
                var camObj = GameObject.FindWithTag(cameraTag);
                if (camObj) cameraRoot = camObj.transform;
            }

            // Thumb tip
            if (!thumbTip && !string.IsNullOrEmpty(thumbTipTag))
            {
                var tObj = GameObject.FindWithTag(thumbTipTag);
                if (tObj) thumbTip = tObj.transform;
            }

            // Index tip
            if (!indexTip && !string.IsNullOrEmpty(indexTipTag))
            {
                var iObj = GameObject.FindWithTag(indexTipTag);
                if (iObj) indexTip = iObj.transform;
            }

            // Hand root
            if (!handRoot && !string.IsNullOrEmpty(handRootTag))
            {
                var hObj = GameObject.FindWithTag(handRootTag);
                if (hObj) handRoot = hObj.transform;
            }

            // Stable origin
            if (!stableOrigin && !string.IsNullOrEmpty(stableOriginTag))
            {
                var so = GameObject.FindWithTag(stableOriginTag);
                if (so) stableOrigin = so.transform;
            }

            // Stable fwd ref
            if (!stableForwardRef && !string.IsNullOrEmpty(stableForwardRefTag))
            {
                var sf = GameObject.FindWithTag(stableForwardRefTag);
                if (sf) stableForwardRef = sf.transform;
            }

            // Stable up override
            if (!stableUpOverride && !string.IsNullOrEmpty(stableUpTag))
            {
                var su = GameObject.FindWithTag(stableUpTag);
                if (su) stableUpOverride = su.transform;
            }

            // upReference (for clampPitch) fallback: we can just reuse cameraRoot
            if (!upReference && cameraRoot)
            {
                upReference = cameraRoot;
            }

            // aimPose safety fallback
            if (!aimPose)
            {
                aimPose = transform;
            }
        }

        IEnumerator EnsureWiredRoutine()
        {
            float t0 = Time.unscaledTime;

            // 1) Find interactor if needed
            while (interactor == null)
            {
                if (!findInteractorByTag || string.IsNullOrEmpty(interactorTag))
                {
                    TryGetComponent(out interactor);
                }
                else
                {
                    var go = GameObject.FindWithTag(interactorTag);
                    if (go) interactor = go.GetComponent<NearFarInteractor>();

                    if (interactor == null)
                    {
#if UNITY_2022_2_OR_NEWER
                        var all = Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                        var all = Object.FindObjectsOfType<NearFarInteractor>(true);
#endif
                        foreach (var cand in all)
                        {
                            if (cand && cand.gameObject.CompareTag(interactorTag))
                            {
                                interactor = cand;
                                break;
                            }
                        }
                    }
                }

                if (interactor != null) break;
                if (!retryUntilFound || (Time.unscaledTime - t0) > retryTimeoutSeconds)
                {
                    Debug.LogError($"HandRayWiring: Could not find NearFarInteractor with tag '{interactorTag}'.", this);
                    yield break;
                }
                yield return null;
            }

            // sanity for HandPoseDirect fallback
            if (!aimPose && aimMode == AimMode.HandPoseDirect)
            {
                Debug.LogError("HandRayWiring: aimPose not assigned (HandPoseDirect).", this);
                yield break;
            }

            // 2) Create/update an anchor we control
            if (_rayAnchor == null)
            {
                var go = new GameObject($"RayAnchor_{gameObject.name}");
                _rayAnchor = go.transform;

                // Seed with something valid
                Vector3 seedPos;
                Quaternion seedRot;
                GetRawTargetPose(out seedPos, out seedRot);
                _rayAnchor.SetPositionAndRotation(seedPos, seedRot);
            }

            // 3) Give anchor to interactor's ray provider
            var rp = (IXRRayProvider)interactor;
            rp.SetRayOrigin(_rayAnchor);
            // If you want attach to follow same transform, uncomment:
            // rp.SetAttachTransform(_rayAnchor);

            _smoothedRot = _rayAnchor.rotation;
            _wired = true;
            if (logOnceWhenWired)
                Debug.Log($"HandRayWiring: Wired ray origin to filtered anchor '{_rayAnchor.name}' for '{interactor.name}'.", this);
        }

        void LateUpdate()
        {
            // If we lost critical refs (like handRoot got destroyed when the hand despawned),
            // try to recover once here.
            if (_wired)
            {
                bool missingNow = false;
                switch (aimMode)
                {
                    case AimMode.HandPoseDirect:
                        if (!aimPose) missingNow = true;
                        break;
                    case AimMode.CameraThumbHybrid:
                        if (!cameraRoot || !thumbTip || !indexTip) missingNow = true;
                        break;
                    case AimMode.StableSpace:
                        if (!stableOrigin || !stableForwardRef || !handRoot) missingNow = true;
                        break;
                }
                if (!interactor) missingNow = true;

                if (missingNow)
                {
                    // try 1 frame of re-resolving
                    TryAutoResolveRefs();

                    // re-check
                    missingNow = false;
                    switch (aimMode)
                    {
                        case AimMode.HandPoseDirect:
                            if (!aimPose) missingNow = true;
                            break;
                        case AimMode.CameraThumbHybrid:
                            if (!cameraRoot || !thumbTip || !indexTip) missingNow = true;
                            break;
                        case AimMode.StableSpace:
                            if (!stableOrigin || !stableForwardRef || !handRoot) missingNow = true;
                            break;
                    }
                    if (!interactor) missingNow = true;

                    if (missingNow)
                    {
                        // can't drive ray this frame
                        return;
                    }
                }
            }


            if (!_wired || _rayAnchor == null)
                return;

            // 1) Figure out the raw, unsmoothed, unclamped pose based on aiming mode
            Vector3 targetPos;
            Quaternion targetRot;
            GetRawTargetPose(out targetPos, out targetRot);

            // 2) Normalize forward (clamp pitch & apply bias) if requested
            if (clampPitchToWorld)
            {
                Vector3 upVec = (upReference ? upReference.up : Vector3.up);
                targetRot = ClampPitch(targetRot, pitchBiasDeg, maxPitchUpDeg, maxPitchDownDeg, upVec);
            }
            /*
            // 3) Position smoothing (critically damped, framerate independent)
            Vector3 currentPos = _rayAnchor.position;
            Vector3 delta = targetPos - currentPos;
            if (posTau > 0f && delta.sqrMagnitude > (posDeadzone * posDeadzone))
            {
                _rayAnchor.position = Vector3.SmoothDamp(
                    currentPos,
                    targetPos,
                    ref _vel,
                    posTau,
                    Mathf.Infinity,
                    Time.unscaledDeltaTime
                );
            }
            else if (posTau <= 0f)
            {
                _rayAnchor.position = targetPos;
            }

            // 4) Rotation smoothing (τ → per-frame alpha, framerate independent)
            float ang = Quaternion.Angle(_smoothedRot, targetRot);
            float dt = Time.unscaledDeltaTime;
            if (rotTau > 0f && ang > rotDeadzoneDeg)
            {
                float alpha = 1f - Mathf.Exp(-dt / rotTau); // exponential smoothing
                _smoothedRot = Quaternion.Slerp(_smoothedRot, targetRot, alpha);
            }
            else if (rotTau <= 0f)
            {
                _smoothedRot = targetRot;
            }

            _rayAnchor.rotation = _smoothedRot;*/
            // 3) Position smoothing
            if (posTau > 0f)
            {
                if (smoothInCustomSpace && CustomCoordinateSpace.Ready)
                {
                    Vector3 curS = CustomCoordinateSpace.WorldToStablePos(_rayAnchor.position);
                    Vector3 tgtS = CustomCoordinateSpace.WorldToStablePos(targetPos);
                    Vector3 velS = Vector3.zero;

                    // SmoothDamp in stable space
                    Vector3 nextS = Vector3.SmoothDamp(curS, tgtS, ref velS, posTau, Mathf.Infinity, Time.unscaledDeltaTime);

                    // Persist velocity back in world (approximate by re-mapping)
                    _vel = CustomCoordinateSpace.StableToWorldPos(curS + velS) - CustomCoordinateSpace.StableToWorldPos(curS);

                    _rayAnchor.position = CustomCoordinateSpace.StableToWorldPos(nextS);
                }
                else
                {
                    Vector3 currentPos = _rayAnchor.position;
                    Vector3 delta = targetPos - currentPos;
                    if (delta.sqrMagnitude > (posDeadzone * posDeadzone))
                    {
                        _rayAnchor.position = Vector3.SmoothDamp(currentPos, targetPos, ref _vel, posTau, Mathf.Infinity, Time.unscaledDeltaTime);
                    }
                    else
                    {
                        _rayAnchor.position = targetPos;
                    }
                }
            }
            else
            {
                _rayAnchor.position = targetPos;
            }

            // 4) Rotation smoothing
            if (rotTau > 0f)
            {
                if (smoothInCustomSpace && CustomCoordinateSpace.Ready)
                {
                    Quaternion curS = CustomCoordinateSpace.WorldToStableRot(_smoothedRot);
                    Quaternion tgtS = CustomCoordinateSpace.WorldToStableRot(targetRot);

                    float dt = Time.unscaledDeltaTime;
                    float alpha = 1f - Mathf.Exp(-dt / rotTau);

                    Quaternion nextS = Quaternion.Slerp(curS, tgtS, alpha);
                    _smoothedRot = CustomCoordinateSpace.StableToWorldRot(nextS);
                }
                else
                {
                    float ang = Quaternion.Angle(_smoothedRot, targetRot);
                    float dt = Time.unscaledDeltaTime;
                    if (ang > rotDeadzoneDeg)
                    {
                        float alpha = 1f - Mathf.Exp(-dt / rotTau);
                        _smoothedRot = Quaternion.Slerp(_smoothedRot, targetRot, alpha);
                    }
                    else
                    {
                        _smoothedRot = targetRot;
                    }
                }
            }
            else
            {
                _smoothedRot = targetRot;
            }

            _rayAnchor.rotation = _smoothedRot;
        }

        // -------------------------------------------------
        // NEW: unified pose source
        // -------------------------------------------------
        void GetRawTargetPose(out Vector3 pos, out Quaternion rot)
        {
            switch (aimMode)
            {
                case AimMode.CameraThumbHybrid:
                    {
                        // Safety fallback
                        if (!cameraRoot || !thumbTip || !indexTip)
                        {
                            FallbackFromAimPose(out pos, out rot);
                            break;
                        }

                        /*
                        Vector3 pinchPosLive = 0.5f * (thumbTip.position + indexTip.position);
                        Vector3 camFwd = cameraRoot.forward;*/
                        // 1) Compute live pinch info
                        Vector3 pinchPosLive =
                            0.5f * (
                                CustomCoordinateSpace.PositionCustomCoordinateSystem(thumbTip.position) +
                                CustomCoordinateSpace.PositionCustomCoordinateSystem(indexTip.position)
                            );

                        Vector3 camFwd =
                            CustomCoordinateSpace.RotationCustomCoordinateSystem(cameraRoot.forward);

                        Vector3 fingerDir = (indexTip.position - thumbTip.position);
                        float pinchDist = fingerDir.magnitude;
                        if (fingerDir.sqrMagnitude < 1e-6f) fingerDir = camFwd;
                        fingerDir.Normalize();

                        // Blend (mostly camera forward, a bit of finger flavor)
                        Vector3 blendedDir = Vector3.Slerp(camFwd, fingerDir, 0.3f);
                        if (blendedDir.sqrMagnitude < 1e-6f) blendedDir = camFwd;
                        blendedDir.Normalize();

                        Quaternion baseRot = Quaternion.LookRotation(blendedDir, cameraRoot.up);
                        Quaternion liveRot = baseRot * Quaternion.Euler(localEulerOffset);

                        // 2) Update pinch lock state
                        if (!_pinchHeld && pinchDist < pinchStartDistance)
                        {
                            // just started pinching -> lock current pose
                            _pinchHeld = true;
                            _pinchLockedPos = pinchPosLive;
                            _pinchLockedRot = liveRot;
                        }
                        else if (_pinchHeld && pinchDist > pinchReleaseDistance)
                        {
                            // released -> unlock
                            _pinchHeld = false;
                        }

                        // 3) Output either locked or live
                        if (_pinchHeld)
                        {
                            pos = _pinchLockedPos;
                            rot = _pinchLockedRot;
                        }
                        else
                        {
                            pos = pinchPosLive;
                            rot = liveRot;
                        }

                        break;
                    }

                case AimMode.StableSpace:
                    {
                        // We treat stableOrigin/stableForwardRef like a "virtual parent space"
                        // to apply localPositionOffset consistently, independent of XR Origin drift.
                        if (!stableOrigin || !stableForwardRef || !handRoot)
                        {
                            FallbackFromAimPose(out pos, out rot);
                            break;
                        }

                        BuildStableBasis(
                            stableOrigin,
                            stableForwardRef,
                            stableUpOverride ? stableUpOverride.up : Vector3.up,
                            out Vector3 stOriginPos,
                            out Vector3 stRight,
                            out Vector3 stUp,
                            out Vector3 stFwd
                        );

                        // -- POSITION --
                        // Convert handRoot world pos -> stable coordinates
                        Vector3 relWorld = handRoot.position - stOriginPos;
                        float sx = Vector3.Dot(relWorld, stRight);
                        float sy = Vector3.Dot(relWorld, stUp);
                        float sz = Vector3.Dot(relWorld, stFwd);
                        Vector3 stableHandPos = new Vector3(sx, sy, sz);

                        // Apply offset IN STABLE SPACE (not XR Origin space)
                        Vector3 stableWithOffset = stableHandPos + localPositionOffset;

                        // Convert back to world
                        pos = stOriginPos
                            + stRight * stableWithOffset.x
                            + stUp * stableWithOffset.y
                            + stFwd * stableWithOffset.z;

                        // -- ROTATION --
                        // We still want true 6DoF hand pointing.
                        // So use the real handRoot.rotation, just plus localEulerOffset.
                        rot = handRoot.rotation * Quaternion.Euler(localEulerOffset);
                        break;
                    }

                case AimMode.HandPoseDirect:
                default:
                    {
                        FallbackFromAimPose(out pos, out rot);
                        break;
                    }
            }
        }

        // Used for HandPoseDirect and as a fallback for other modes if refs are missing.
        void FallbackFromAimPose(out Vector3 pos, out Quaternion rot)
        {
            if (!aimPose)
            {
                // super last resort: use our own transform
                pos = transform.position;
                rot = transform.rotation;
                return;
            }

            // position offset in aimPose local
            pos = aimPose.TransformPoint(localPositionOffset);

            // rotation offset in aimPose local
            rot = aimPose.rotation * Quaternion.Euler(localEulerOffset);
        }

        // Build an orthonormal frame we call "stable space"
        // right / up / fwd will behave like axes of a fake parent transform.
        static void BuildStableBasis(
            Transform origin,
            Transform forwardRef,
            Vector3 upHint,
            out Vector3 originPos,
            out Vector3 right,
            out Vector3 up,
            out Vector3 fwd
        )
        {
            originPos = origin.position;

            // forwardDir = (forwardRef - origin).normalized
            Vector3 rawFwd = (forwardRef.position - originPos);
            if (rawFwd.sqrMagnitude < 1e-8f)
                rawFwd = origin.forward;
            rawFwd.Normalize();

            // Pick an up vector
            up = upHint.sqrMagnitude < 1e-8f ? Vector3.up : upHint.normalized;

            // Build right from up x fwd, then rebuild fwd to ensure orthogonality.
            right = Vector3.Cross(up, rawFwd);
            if (right.sqrMagnitude < 1e-8f)
            {
                // degenerate case: up ~ parallel to fwd
                // pick any horizontal-ish right
                right = Vector3.Cross(Vector3.up, rawFwd);
            }
            right.Normalize();

            fwd = Vector3.Cross(right, up).normalized;
            // right is already perp to up and fwd now
        }

        // Pitch clamp helper is unchanged
        static Quaternion ClampPitch(Quaternion rot, float biasDeg, float maxUpDeg, float maxDownDeg, Vector3 up)
        {
            up = (up.sqrMagnitude < 1e-6f) ? Vector3.up : up.normalized;

            Vector3 fwd = rot * Vector3.forward;
            Vector3 planar = Vector3.ProjectOnPlane(fwd, up);
            if (planar.sqrMagnitude < 1e-8f)
                planar = Vector3.ProjectOnPlane(Vector3.forward, up); // fallback

            planar.Normalize();

            // Horizontal right axis
            Vector3 right = Vector3.Cross(up, planar);
            if (right.sqrMagnitude < 1e-8f) right = Vector3.right;
            right.Normalize();

            // Signed pitch (+up, -down)
            float signed = Vector3.SignedAngle(planar, fwd, right);

            // Bias and clamp
            float desired = Mathf.Clamp(
                signed + biasDeg,
                -Mathf.Abs(maxDownDeg),
                Mathf.Abs(maxUpDeg)
            );

            Vector3 clampedFwd = Quaternion.AngleAxis(desired, right) * planar;

            // Rebuild rotation with minimal roll
            return Quaternion.LookRotation(clampedFwd, up);
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            var origin = _rayAnchor ? _rayAnchor.position : (aimPose ? aimPose.position : transform.position);
            var dir = _rayAnchor ? _rayAnchor.forward : (aimPose ? aimPose.forward : transform.forward);

            //Gizmos.color = Color.cyan;
            //Gizmos.DrawLine(origin, origin + dir * gizmoLength);
            //Gizmos.DrawSphere(origin, 0.005f);
        }
    }
}