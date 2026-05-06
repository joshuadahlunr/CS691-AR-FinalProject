// Assets/ManoMotion/Scripts/Open XR Bridge/ManoToXRHandsAdapter.cs
using UnityEngine;
using UnityEngine.XR.Hands;
using Custom.XR;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    public class ManoToXRHandsAdapter : MonoBehaviour
    {
        public enum MappingProfile { Mano21_NoMetas, Mano21_NoTips } // <- match Bootstrap profile!

        [Header("Profile")]
        public MappingProfile profile = MappingProfile.Mano21_NoTips;

        [Header("Handedness")]
        public bool isRight = true;

        [Header("Required")]
        public Transform wrist;

        [Header("Thumb (up to 4 links)")]
        public Transform thumbMeta;     // link 1
        public Transform thumbProx;     // link 2
        public Transform thumbDist;     // link 3
        public Transform thumbTip;      // link 4 (if missing, we’ll mirror Distal)

        [Header("Index")]
        public Transform indexMeta;     // only used in NoTips
        public Transform indexProx;
        public Transform indexInter;
        public Transform indexDist;
        public Transform indexTip;      // NOT sent in NoTips

        [Header("Middle")]
        public Transform middleMeta;    // only used in NoTips
        public Transform middleProx;
        public Transform middleInter;
        public Transform middleDist;
        public Transform middleTip;     // NOT sent in NoTips

        [Header("Ring")]
        public Transform ringMeta;      // only used in NoTips
        public Transform ringProx;
        public Transform ringInter;
        public Transform ringDist;
        public Transform ringTip;       // NOT sent in NoTips

        [Header("Little")]
        public Transform littleMeta;    // only used in NoTips
        public Transform littleProx;
        public Transform littleInter;
        public Transform littleDist;
        public Transform littleTip;     // NOT sent in NoTips

        [Header("Radii")]
        public float defaultRadius = 0.015f;

        Handedness H => isRight ? Handedness.Right : Handedness.Left;

        void LateUpdate()
        {
            var ss = CustomHandSubsystem.Instance;
            if (ss == null || !ss.running) return;

            // Always feed Wrist (root)
            Feed(ss, XRHandJointID.Wrist, wrist);

            // ---------- Thumb (always 4 links) ----------
            // If thumbTip is missing, duplicate Distal to keep 4 segments consistent.
            Feed(ss, XRHandJointID.ThumbMetacarpal, thumbMeta);
            Feed(ss, XRHandJointID.ThumbProximal, thumbProx);
            Feed(ss, XRHandJointID.ThumbDistal, thumbDist);
            if (thumbTip != null)
            {
                Feed(ss, XRHandJointID.ThumbTip, thumbTip);
            }
            else if (thumbDist != null)
            {
                // duplicate distal as a placeholder “fourth link”
                FeedPose(ss, XRHandJointID.ThumbTip, new Pose(thumbDist.position, thumbDist.rotation));
            }

            if (profile == MappingProfile.Mano21_NoTips)
            {
                // ---------- Tipless with metas (21) ----------
                // Index
                Feed(ss, XRHandJointID.IndexMetacarpal, indexMeta);
                Feed(ss, XRHandJointID.IndexProximal, indexProx);
                Feed(ss, XRHandJointID.IndexIntermediate, indexInter);
                Feed(ss, XRHandJointID.IndexDistal, indexDist);
                // no IndexTip

                // Middle
                Feed(ss, XRHandJointID.MiddleMetacarpal, middleMeta);
                Feed(ss, XRHandJointID.MiddleProximal, middleProx);
                Feed(ss, XRHandJointID.MiddleIntermediate, middleInter);
                Feed(ss, XRHandJointID.MiddleDistal, middleDist);
                // no MiddleTip

                // Ring
                Feed(ss, XRHandJointID.RingMetacarpal, ringMeta);
                Feed(ss, XRHandJointID.RingProximal, ringProx);
                Feed(ss, XRHandJointID.RingIntermediate, ringInter);
                Feed(ss, XRHandJointID.RingDistal, ringDist);
                // no RingTip

                // Little
                Feed(ss, XRHandJointID.LittleMetacarpal, littleMeta);
                Feed(ss, XRHandJointID.LittleProximal, littleProx);
                Feed(ss, XRHandJointID.LittleIntermediate, littleInter);
                Feed(ss, XRHandJointID.LittleDistal, littleDist);
                // no LittleTip
            }
            else // MappingProfile.Mano21_NoMetas (legacy)
            {
                // ---------- Legacy 21 (no metas, HAS tips, and (optionally) Palm) ----------
                // Index
                Feed(ss, XRHandJointID.IndexProximal, indexProx);
                Feed(ss, XRHandJointID.IndexIntermediate, indexInter);
                Feed(ss, XRHandJointID.IndexDistal, indexDist);
                Feed(ss, XRHandJointID.IndexTip, indexTip);

                // Middle
                Feed(ss, XRHandJointID.MiddleProximal, middleProx);
                Feed(ss, XRHandJointID.MiddleIntermediate, middleInter);
                Feed(ss, XRHandJointID.MiddleDistal, middleDist);
                Feed(ss, XRHandJointID.MiddleTip, middleTip);

                // Ring
                Feed(ss, XRHandJointID.RingProximal, ringProx);
                Feed(ss, XRHandJointID.RingIntermediate, ringInter);
                Feed(ss, XRHandJointID.RingDistal, ringDist);
                Feed(ss, XRHandJointID.RingTip, ringTip);

                // Little
                Feed(ss, XRHandJointID.LittleProximal, littleProx);
                Feed(ss, XRHandJointID.LittleIntermediate, littleInter);
                Feed(ss, XRHandJointID.LittleDistal, littleDist);
                Feed(ss, XRHandJointID.LittleTip, littleTip);

                // NOTE: Palm is NOT fed here on purpose. If you really have a stable palm,
                // add a Transform and call Feed(ss, XRHandJointID.Palm, palmTransform).
            }
        }

        void Feed(CustomHandSubsystem ss, XRHandJointID id, Transform t)
        {
            if (!t) return;
            ss.SetJointPoseWorld(H, id, new Pose(t.position, t.rotation), defaultRadius);
        }

        void FeedPose(CustomHandSubsystem ss, XRHandJointID id, Pose p)
            => ss.SetJointPoseWorld(H, id, p, defaultRadius);
    }
}