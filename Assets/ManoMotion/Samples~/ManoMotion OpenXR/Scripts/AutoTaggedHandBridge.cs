// Assets/ManoMotion/Scripts/Open XR Bridge/AutoTaggedHandBridgeDebug.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using Custom.XR;

using ManoMotion.OpenXR;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    public class AutoTaggedHandBridgeDebug : MonoBehaviour
    {
        [SerializeField] Camera worldCamera;
        [SerializeField] Vector3 axisCorrectionEuler;
        [SerializeField] float defaultRadius = 0.015f;

        static readonly XRHandJointID[] k_Mano21 =
        {
            XRHandJointID.Wrist,
            XRHandJointID.ThumbMetacarpal,  XRHandJointID.ThumbProximal,
            XRHandJointID.ThumbDistal,      XRHandJointID.ThumbTip,
            XRHandJointID.IndexProximal,    XRHandJointID.IndexIntermediate,
            XRHandJointID.IndexDistal,      XRHandJointID.IndexTip,
            XRHandJointID.MiddleProximal,   XRHandJointID.MiddleIntermediate,
            XRHandJointID.MiddleDistal,     XRHandJointID.MiddleTip,
            XRHandJointID.RingProximal,     XRHandJointID.RingIntermediate,
            XRHandJointID.RingDistal,       XRHandJointID.RingTip,
            XRHandJointID.LittleProximal,   XRHandJointID.LittleIntermediate,
            XRHandJointID.LittleDistal,     XRHandJointID.LittleTip
        };

        Transform[] m_Left = new Transform[k_Mano21.Length];
        Transform[] m_Right = new Transform[k_Mano21.Length];
        Quaternion m_AxisFix;

        void Awake()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            m_AxisFix = Quaternion.Euler(axisCorrectionEuler);
            CacheHands();
        }

        void Update()
        {
            if (m_Left[0] == null || m_Right[0] == null) CacheHands();

            var sub = CustomHandSubsystem.Instance;
            if (sub == null || !sub.running)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[Bridge] Subsystem not running (frame {Time.frameCount})");
#endif
                return;
            }

            Push(Handedness.Left, m_Left, sub);
            Push(Handedness.Right, m_Right, sub);
        }

        // --------------- helpers ---------------
        void CacheHands()
        {
            GameObject left = GameObject.FindGameObjectWithTag("Left");
            GameObject right = GameObject.FindGameObjectWithTag("Right");

            if (left) Fill(left.transform, m_Left, "Left");
            if (right) Fill(right.transform, m_Right, "Right");
        }

        static void Fill(Transform root, Transform[] dst, string label)
        {
            for (int i = 0; i < dst.Length; ++i)
                dst[i] = i < root.childCount ? root.GetChild(i) : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int miss = 0; foreach (var t in dst) if (t == null) ++miss;
            Debug.Log($"[Bridge] {label} missing children: {miss}");
#endif
        }

        void Push(Handedness hand, Transform[] src, CustomHandSubsystem sub)
        {
            if (src[0] == null) return; // no wrist → hand assumed lost

            for (int i = 0; i < src.Length; ++i)
            {
                var tf = src[i]; if (!tf) continue;
                var pose = new Pose(tf.position, tf.rotation * m_AxisFix);
                sub.SetJointPose(hand, k_Mano21[i], pose, defaultRadius);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Bridge] pushed {hand} frame {Time.frameCount}");
#endif
        }
    }
}