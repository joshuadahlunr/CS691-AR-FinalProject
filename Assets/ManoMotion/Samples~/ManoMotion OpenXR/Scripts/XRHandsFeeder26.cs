using UnityEngine;
using UnityEngine.XR.Hands;
using Custom.XR;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    /// Feeds **all 26** XR joints into CustomHandSubsystem.
    /// - Can take rotations from Mano DebugGizmo (Right/Up/Forward) per joint
    /// - Per-hand extra Euler offset to align Mano axes to XR skeleton axes
    /// - Optional rotation rebuild for finger chains from positions (stable look)
    public class ManoToXRHandsFeeder26 : MonoBehaviour
    {
        [Header("Handedness")]
        public bool isRight = true;
        Handedness H => isRight ? Handedness.Right : Handedness.Left;

        [Header("Source axes (Mano DebugGizmo)")]
        public bool useGizmoAxes = true;
        public string gizmoRootName = "DebugGizmo";
        public string gizmoRightName = "Right";
        public string gizmoUpName = "Up";
        public string gizmoForwardName = "Forward";

        [Header("Axis correction (applied after gizmo)")]
        [Tooltip("Applied to RIGHT hand after reading gizmo/transform rotation.")]
        public Vector3 extraEulerOffsetRight = Vector3.zero;
        [Tooltip("Applied to LEFT hand after reading gizmo/transform rotation.")]
        public Vector3 extraEulerOffsetLeft = Vector3.zero;

        Quaternion ExtraOffset =>
            Quaternion.Euler(isRight ? extraEulerOffsetRight : extraEulerOffsetLeft);

        [Header("Optional rotation rebuild (helps fingers stay sane)")]
        public bool rebuildFingerRotationsFromPositions = true;

        [Header("Radii")]
        public float defaultRadius = 0.012f;
        public float wristRadius = 0.020f;
        public float palmRadius = 0.018f;

        // ------------ 26 JOINTS ------------
        [Header("Required roots")]
        public Transform wrist;
        public Transform palm; // your synthesized palm (or leave null; we compute fallback)

        [Header("Thumb (4)")]
        public Transform thumbMetacarpal, thumbProximal, thumbDistal, thumbTip;

        [Header("Index (5)")]
        public Transform indexMetacarpal, indexProximal, indexIntermediate, indexDistal, indexTip;

        [Header("Middle (5)")]
        public Transform middleMetacarpal, middleProximal, middleIntermediate, middleDistal, middleTip;

        [Header("Ring (5)")]
        public Transform ringMetacarpal, ringProximal, ringIntermediate, ringDistal, ringTip;

        [Header("Little (5)")]
        public Transform littleMetacarpal, littleProximal, littleIntermediate, littleDistal, littleTip;

        void LateUpdate()
        {
            var ss = CustomHandSubsystem.Instance;
            if (ss == null || !ss.running) return;

            // 1) Feed wrist first (drives the root in provider)
            Feed(ss, XRHandJointID.Wrist, wrist, wristRadius);

            // 2) Palm: use provided if you have it; otherwise compute a fallback
            if (palm != null)
                Feed(ss, XRHandJointID.Palm, palm, palmRadius);
            else
                FeedPalmFallback(ss);

            // 3) Fingers (feed everything we have)
            // Thumb
            Feed(ss, XRHandJointID.ThumbMetacarpal, thumbMetacarpal);
            Feed(ss, XRHandJointID.ThumbProximal, thumbProximal);
            Feed(ss, XRHandJointID.ThumbDistal, thumbDistal);
            Feed(ss, XRHandJointID.ThumbTip, thumbTip);

            // Index
            Feed(ss, XRHandJointID.IndexMetacarpal, indexMetacarpal);
            Feed(ss, XRHandJointID.IndexProximal, indexProximal);
            Feed(ss, XRHandJointID.IndexIntermediate, indexIntermediate);
            Feed(ss, XRHandJointID.IndexDistal, indexDistal);
            Feed(ss, XRHandJointID.IndexTip, indexTip);

            // Middle
            Feed(ss, XRHandJointID.MiddleMetacarpal, middleMetacarpal);
            Feed(ss, XRHandJointID.MiddleProximal, middleProximal);
            Feed(ss, XRHandJointID.MiddleIntermediate, middleIntermediate);
            Feed(ss, XRHandJointID.MiddleDistal, middleDistal);
            Feed(ss, XRHandJointID.MiddleTip, middleTip);

            // Ring
            Feed(ss, XRHandJointID.RingMetacarpal, ringMetacarpal);
            Feed(ss, XRHandJointID.RingProximal, ringProximal);
            Feed(ss, XRHandJointID.RingIntermediate, ringIntermediate);
            Feed(ss, XRHandJointID.RingDistal, ringDistal);
            Feed(ss, XRHandJointID.RingTip, ringTip);

            // Little
            Feed(ss, XRHandJointID.LittleMetacarpal, littleMetacarpal);
            Feed(ss, XRHandJointID.LittleProximal, littleProximal);
            Feed(ss, XRHandJointID.LittleIntermediate, littleIntermediate);
            Feed(ss, XRHandJointID.LittleDistal, littleDistal);
            Feed(ss, XRHandJointID.LittleTip, littleTip);

            // 4) Optional: rebuild finger rotations from positions (kept the wrist/palm as-is)
            if (rebuildFingerRotationsFromPositions)
                RebuildFingerRotations(ss);
        }

        // -------- feeding helpers --------

        void Feed(CustomHandSubsystem ss, XRHandJointID id, Transform t, float rOverride = -1f)
        {
            if (!t) return;
            var pose = PoseFromTransformOrGizmo(t);
            ss.SetJointPoseWorld(H, id, pose, rOverride > 0f ? rOverride : defaultRadius);
        }

        Pose PoseFromTransformOrGizmo(Transform t)
        {
            Quaternion rot = t.rotation;

            if (useGizmoAxes)
            {
                var giz = t.Find(gizmoRootName);
                if (giz)
                {
                    var r = giz.Find(gizmoRightName);
                    var u = giz.Find(gizmoUpName);
                    var f = giz.Find(gizmoForwardName);
                    if (r && u && f)
                    {
                        // Build rotation whose X/Y/Z world axes match the gizmo arrows.
                        // (columns of a rotation matrix are Right/Up/Forward in Unity)
                        Vector3 right = (r.position - giz.position).normalized;
                        Vector3 up = (u.position - giz.position).normalized;
                        Vector3 forward = (f.position - giz.position).normalized;

                        // Orthonormalize just in case
                        right = Vector3.Normalize(right - Vector3.Project(right, up));
                        forward = Vector3.Normalize(Vector3.Cross(right, up));
                        up = Vector3.Normalize(Vector3.Cross(forward, right));

                        rot = Quaternion.LookRotation(forward, up);
                    }
                }
            }

            rot = ExtraOffset * rot;
            return new Pose(t.position, rot);
        }

        // Fallback palm if you didn’t wire a synthesized palm node.
        void FeedPalmFallback(CustomHandSubsystem ss)
        {
            if (!wrist || !indexProximal || !middleProximal) return;

            Vector3 w = wrist.position;
            Vector3 iP = indexProximal.position;
            Vector3 mP = middleProximal.position;
            Vector3 lP = littleProximal ? littleProximal.position : (iP + (mP - iP));

            // forward: average knuckle->intermediate where present
            Vector3 fwd = Vector3.zero; int c = 0;
            if (indexIntermediate) { fwd += (indexIntermediate.position - iP); c++; }
            if (middleIntermediate) { fwd += (middleIntermediate.position - mP); c++; }
            if (littleIntermediate) { fwd += (littleIntermediate.position - lP); c++; }
            if (c == 0) fwd = (mP - w);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
            fwd.Normalize();

            // across: index -> little
            Vector3 across = (lP - iP);
            if (across.sqrMagnitude < 1e-8f) across = Vector3.right;
            across.Normalize();

            // up with handedness
            Vector3 up = isRight ? Vector3.Cross(across, fwd).normalized
                                 : Vector3.Cross(fwd, across).normalized;
            if (up.sqrMagnitude < 1e-8f) up = Vector3.up;

            Vector3 knuckleCentroid = (iP + mP + lP) / 3f;
            Vector3 pos = Vector3.Lerp(knuckleCentroid, w, 0.25f);
            Quaternion rot = ExtraOffset * Quaternion.LookRotation(fwd, up);

            ss.SetJointPoseWorld(H, XRHandJointID.Palm, new Pose(pos, rot), palmRadius);
        }

        // Rebuilds only finger links (keeps your wrist/palm as fed).
        void RebuildFingerRotations(CustomHandSubsystem ss)
        {
            // We compute “palmUp” from the fed Palm if present; else from a quick fallback.
            Vector3 palmUp, palmFwd;

            if (palm)
            {
                var p = PoseFromTransformOrGizmo(palm);
                palmUp = p.rotation * Vector3.up;
                palmFwd = p.rotation * Vector3.forward;
            }
            else
            {
                // Derive lightweight basis from wrist + knuckles
                Vector3 w = wrist ? wrist.position : transform.position;
                Vector3 iP = indexProximal ? indexProximal.position : (w + transform.right);
                Vector3 mP = middleProximal ? middleProximal.position : (w + transform.forward);
                Vector3 lP = littleProximal ? littleProximal.position : (iP + (mP - iP));
                Vector3 fwd = (mP - w); if (fwd.sqrMagnitude < 1e-8f) fwd = transform.forward; fwd.Normalize();
                Vector3 across = (lP - iP); if (across.sqrMagnitude < 1e-8f) across = transform.right; across.Normalize();
                palmUp = (isRight ? Vector3.Cross(across, fwd) : Vector3.Cross(fwd, across)).normalized;
                if (palmUp.sqrMagnitude < 1e-8f) palmUp = Vector3.up;
                palmFwd = fwd;
            }

            // Helper: orient A to look at B (fallback = palmFwd)
            void ReOrient(XRHandJointID a, Transform A, Transform B)
            {
                if (!A) return;
                Vector3 dir = B ? (B.position - A.position) : palmFwd;
                if (dir.sqrMagnitude < 1e-8f) dir = palmFwd;
                Quaternion rot = Quaternion.LookRotation(dir.normalized, palmUp);
                var pose = new Pose(A.position, rot);
                ss.SetJointPoseWorld(H, a, pose, defaultRadius);
            }

            // Thumb
            ReOrient(XRHandJointID.ThumbMetacarpal, thumbMetacarpal, thumbProximal);
            ReOrient(XRHandJointID.ThumbProximal, thumbProximal, thumbDistal);
            ReOrient(XRHandJointID.ThumbDistal, thumbDistal, thumbTip);

            // Index
            ReOrient(XRHandJointID.IndexMetacarpal, indexMetacarpal, indexProximal);
            ReOrient(XRHandJointID.IndexProximal, indexProximal, indexIntermediate);
            ReOrient(XRHandJointID.IndexIntermediate, indexIntermediate, indexDistal);
            ReOrient(XRHandJointID.IndexDistal, indexDistal, indexTip);

            // Middle
            ReOrient(XRHandJointID.MiddleMetacarpal, middleMetacarpal, middleProximal);
            ReOrient(XRHandJointID.MiddleProximal, middleProximal, middleIntermediate);
            ReOrient(XRHandJointID.MiddleIntermediate, middleIntermediate, middleDistal);
            ReOrient(XRHandJointID.MiddleDistal, middleDistal, middleTip);

            // Ring
            ReOrient(XRHandJointID.RingMetacarpal, ringMetacarpal, ringProximal);
            ReOrient(XRHandJointID.RingProximal, ringProximal, ringIntermediate);
            ReOrient(XRHandJointID.RingIntermediate, ringIntermediate, ringDistal);
            ReOrient(XRHandJointID.RingDistal, ringDistal, ringTip);

            // Little
            ReOrient(XRHandJointID.LittleMetacarpal, littleMetacarpal, littleProximal);
            ReOrient(XRHandJointID.LittleProximal, littleProximal, littleIntermediate);
            ReOrient(XRHandJointID.LittleIntermediate, littleIntermediate, littleDistal);
            ReOrient(XRHandJointID.LittleDistal, littleDistal, littleTip);
        }
    }
}