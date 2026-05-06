// Assets/ManoMotion/Scripts/Open XR Bridge/CustomHandSubsystem.cs
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.ProviderImplementation;
using SubsystemUpdater = UnityEngine.XR.Hands.ProviderImplementation.XRHandProviderUtility.SubsystemUpdater;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace Custom.XR
{
    /// <summary>
    /// XR Hands subsystem:
    /// - Profiles:
    ///     Full26            → all 26 joints
    ///     Mano21_NoMetas    → legacy (no metas for index..little, had Palm)
    ///     Mano21_NoTips     → NEW (metacarpals present; NO tips for index..little; thumb’s 4th link uses ThumbTip as a placeholder)
    /// - Optional WORLD→device-origin conversion for SetJointPoseWorld(...)
    /// - Wrist root for both Mano21 profiles
    /// </summary>
    public class CustomHandSubsystem : XRHandSubsystem
    {
        const string k_Id = "custom-hand-subsystem";
        public static readonly bool k_Log = true;

        public enum HandLayoutProfile
        {
            Full26,
            Mano21_NoMetas,
            Mano21_NoTips   // <- NEW profile
        }

        public void SetHandLayoutProfile(HandLayoutProfile profile, bool restartSubsystem = false)
        {
            if (provider is Provider p)
            {
                p.SetLayoutProfile(profile);
                if (restartSubsystem && running) { Stop(); Start(); }
            }
#if UNITY_EDITOR
            else if (k_Log) Debug.LogWarning("[CustomHandSubsystem] Provider not ready; call SetHandLayoutProfile after subsystem is created.");
#endif
        }

        static CustomHandSubsystem() { RegisterDescriptor(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void RegisterDescriptor()
        {
            var cinfo = new XRHandSubsystemDescriptor.Cinfo
            {
                id = k_Id,
                providerType = typeof(Provider),
                subsystemTypeOverride = typeof(CustomHandSubsystem)
            };
            XRHandSubsystemDescriptor.Register(cinfo);
#if UNITY_EDITOR
            if (k_Log) Debug.Log("[CustomHandSubsystem] Descriptor registered.");
#endif
        }

        static CustomHandSubsystem s_Instance;
        public static CustomHandSubsystem Instance => s_Instance;

        SubsystemUpdater m_Updater;

        protected override void OnStart()
        {
            base.OnStart();
            m_Updater ??= new SubsystemUpdater(this);
            m_Updater.Start();
            s_Instance = this;
        }

        protected override void OnStop() { base.OnStop(); m_Updater?.Stop(); }
        protected override void OnDestroy() { base.OnDestroy(); m_Updater?.Destroy(); s_Instance = null; }

        // ---------- Public API for feeders ----------
        public void SetJointPose(Handedness hand, XRHandJointID id, Pose pose, float radius = 0.015f)
            => (provider as Provider)?.SetJointPose(hand, id, pose, radius);

        public void SetJointPoseWorld(Handedness hand, XRHandJointID id, Pose worldPose, float radius = 0.015f)
            => (provider as Provider)?.SetJointPoseWorld(hand, id, worldPose, radius);

        public void ConfigureRoot(bool usePalmAsRoot, Pose rootPoseOffset)
            => (provider as Provider)?.ConfigureRoot(usePalmAsRoot, rootPoseOffset);

        public void OverrideRoot(Handedness hand, Pose? rootPose)
            => (provider as Provider)?.OverrideRoot(hand, rootPose);

        public void ConfigureWorldConversion(Transform originRef, Transform backRef, bool keepWorldUp = true)
            => (provider as Provider)?.ConfigureWorldConversion(originRef, backRef, keepWorldUp);

        // ---------- Provider ----------
        class Provider : XRHandSubsystemProvider
        {
            // Profile (default your old one to keep scenes stable; pick NoTips in the inspector)
            CustomHandSubsystem.HandLayoutProfile m_Profile = CustomHandSubsystem.HandLayoutProfile.Mano21_NoMetas;
            public void SetLayoutProfile(CustomHandSubsystem.HandLayoutProfile p) => m_Profile = p;

            // Root configuration (Wrist-root forced for Mano21 profiles)
            bool m_UsePalmAsRoot = false; // ignored for Mano21 profiles
            Pose m_RootOffset = Pose.identity;
            bool m_LeftRootOverrideEnabled = false, m_RightRootOverrideEnabled = false;
            Pose m_LeftRootOverride = Pose.identity, m_RightRootOverride = Pose.identity;

            // WORLD→custom converter (unchanged / known-good)
            Transform m_OriginRef, m_BackRef;
            bool m_KeepWorldUp = true;

            // Cached wrist roots
            readonly Dictionary<Handedness, Pose> m_RootPose = new()
            {
                { Handedness.Left, Pose.identity },
                { Handedness.Right, Pose.identity }
            };

            // Per-hand joints
            readonly Dictionary<Handedness, Dictionary<XRHandJointID, (Pose pose, float r)>> m_Joints = new()
            {
                { Handedness.Left, new() },
                { Handedness.Right, new() }
            };

            // ---- Config / feed ----
            internal void ConfigureRoot(bool usePalmAsRoot, Pose rootOffset)
            {
                m_UsePalmAsRoot = usePalmAsRoot; // ignored for Mano21
                m_RootOffset = rootOffset;
            }

            internal void OverrideRoot(Handedness hand, Pose? rootPose)
            {
                bool enable = rootPose.HasValue;
                if (hand == Handedness.Left)
                {
                    m_LeftRootOverrideEnabled = enable;
                    if (enable) m_LeftRootOverride = rootPose.Value;
                }
                else
                {
                    m_RightRootOverrideEnabled = enable;
                    if (enable) m_RightRootOverride = rootPose.Value;
                }
            }

            internal void ConfigureWorldConversion(Transform originRef, Transform backRef, bool keepWorldUp)
            {
                m_OriginRef = originRef; m_BackRef = backRef; m_KeepWorldUp = keepWorldUp;
            }

            internal void SetJointPose(Handedness hand, XRHandJointID id, Pose providerSpacePose, float r)
            {
                if (id == XRHandJointID.Wrist) m_RootPose[hand] = providerSpacePose;
                m_Joints[hand][id] = (providerSpacePose, r);
            }

            internal void SetJointPoseWorld(Handedness hand, XRHandJointID id, Pose worldPose, float r)
            {
                var p = ConvertWorldToProviderSpace(worldPose);
                SetJointPose(hand, id, p, r);
            }

            // ---- Layout ----
            static void SetTrue(NativeArray<bool> layout, params XRHandJointID[] ids)
            {
                for (int i = 0; i < ids.Length; ++i)
                    layout[ids[i].ToIndex()] = true;
            }

            public override void GetHandLayout(NativeArray<bool> layout)
            {
                // default all false
                for (int i = XRHandJointID.BeginMarker.ToIndex() + 1; i < XRHandJointID.EndMarker.ToIndex(); ++i)
                    layout[i] = false;

                if (m_Profile == CustomHandSubsystem.HandLayoutProfile.Full26)
                {
                    for (int i = XRHandJointID.BeginMarker.ToIndex() + 1; i < XRHandJointID.EndMarker.ToIndex(); ++i)
                        layout[i] = true;
                    return;
                }

                if (m_Profile == CustomHandSubsystem.HandLayoutProfile.Mano21_NoMetas)
                {
                    // Legacy layout (unchanged) – had Palm, no metas on index..little
                    SetTrue(layout,
                        XRHandJointID.Wrist, XRHandJointID.Palm,
                        XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
                        XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
                        XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
                        XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
                        XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip
                    );
                    return;
                }

                // NEW: Mano21_NoTips (metacarpals present; NO tips for index..little; thumb has 4 links → last link uses ThumbTip as placeholder)
                SetTrue(layout,
                    XRHandJointID.Wrist,

                    // Thumb: 4 links (use ThumbTip to carry the 4th NON-tip segment)
                    XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,

                    // Index/Middle/Ring/Little: Metacarpal + Prox + Inter + Distal (NO tips)
                    XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal,
                    XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal,
                    XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal,
                    XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal
                );
                // Palm + index/middle/ring/little tips remain FALSE
            }

            public override XRHandSubsystem.UpdateSuccessFlags TryUpdateHands(
                XRHandSubsystem.UpdateType type,
                ref Pose lRoot, NativeArray<XRHandJoint> lJ,
                ref Pose rRoot, NativeArray<XRHandJoint> rJ)
            {
                var flags = XRHandSubsystem.UpdateSuccessFlags.None;

                if (Populate(Handedness.Left, ref lRoot, lJ))
                    flags |= XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose |
                             XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints;

                if (Populate(Handedness.Right, ref rRoot, rJ))
                    flags |= XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose |
                             XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;

                return flags;
            }

            bool Populate(Handedness hand, ref Pose root, NativeArray<XRHandJoint> dst)
            {
                var src = m_Joints[hand];

                // Require wrist to consider the hand tracked
                if (!src.ContainsKey(XRHandJointID.Wrist))
                    return false;

                // ---- Root selection ----
                Pose baseRoot;
                if (hand == Handedness.Left && m_LeftRootOverrideEnabled)
                    baseRoot = m_LeftRootOverride;
                else if (hand == Handedness.Right && m_RightRootOverrideEnabled)
                    baseRoot = m_RightRootOverride;
                else
                {
                    if (m_Profile == CustomHandSubsystem.HandLayoutProfile.Full26 &&
                        m_UsePalmAsRoot && src.TryGetValue(XRHandJointID.Palm, out var palm))
                        baseRoot = palm.pose;
                    else
                        baseRoot = m_RootPose[hand]; // Wrist-root for Mano21 profiles
                }

                root = baseRoot.GetTransformedBy(m_RootOffset);

                // Write wrist
                int wristIdx = XRHandJointID.Wrist.ToIndex();
                var wp = src[XRHandJointID.Wrist];
                dst[wristIdx] = XRHandProviderUtility.CreateJoint(
                    hand,
                    XRHandJointTrackingState.Pose | XRHandJointTrackingState.Radius,
                    XRHandJointID.Wrist, wp.pose, wp.r);

                // Write others
                for (int i = XRHandJointID.BeginMarker.ToIndex() + 1; i < XRHandJointID.EndMarker.ToIndex(); ++i)
                {
                    if (i == wristIdx) continue;

                    var id = XRHandJointIDUtility.FromIndex(i);

                    // Mano21_NoTips: explicitly disable Palm and index/middle/ring/little tips
                    if (m_Profile == CustomHandSubsystem.HandLayoutProfile.Mano21_NoTips)
                    {
                        if (id == XRHandJointID.Palm ||
                            id == XRHandJointID.IndexTip ||
                            id == XRHandJointID.MiddleTip ||
                            id == XRHandJointID.RingTip ||
                            id == XRHandJointID.LittleTip)
                        {
                            dst[i] = XRHandProviderUtility.CreateJoint(hand, XRHandJointTrackingState.None, id, default);
                            continue;
                        }
                    }

                    // Mano21_NoMetas: (legacy) allow Palm; no special handling here
                    if (src.TryGetValue(id, out var p))
                    {
                        dst[i] = XRHandProviderUtility.CreateJoint(
                            hand, XRHandJointTrackingState.Pose | XRHandJointTrackingState.Radius, id, p.pose, p.r);
                    }
                    else
                    {
                        dst[i] = XRHandProviderUtility.CreateJoint(hand, XRHandJointTrackingState.None, id, default);
                    }
                }

                return true;
            }

            public override void Start() { }
            public override void Stop() { }
            public override void Destroy() { }

            // ---- WORLD → provider-space conversion (unchanged & stable) ----
            // If BackRef is assigned, forward = (Origin - BackRef), optionally flattened to yaw.
            // Else use Origin rotation (yaw-only if KeepWorldUp).
            Pose ConvertWorldToProviderSpace(Pose worldPose)
            {
                if (m_OriginRef == null) return worldPose;

                Quaternion basisRot;
                if (m_BackRef != null)
                {
                    Vector3 fwd = (m_OriginRef.position - m_BackRef.position);
                    if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
                    if (m_KeepWorldUp) fwd.y = 0f;
                    fwd.Normalize();
                    Vector3 up = m_KeepWorldUp ? Vector3.up : m_OriginRef.up;
                    basisRot = Quaternion.LookRotation(fwd, up);
                }
                else
                {
                    if (m_KeepWorldUp)
                    {
                        Vector3 fwd = m_OriginRef.rotation * Vector3.forward;
                        if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
                        fwd.y = 0f;
                        if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
                        basisRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                    }
                    else
                    {
                        basisRot = m_OriginRef.rotation;
                    }
                }

                var inv = Quaternion.Inverse(basisRot);
                Vector3 localPos = inv * (worldPose.position - m_OriginRef.position);
                Quaternion localRot = inv * worldPose.rotation;
                return new Pose(localPos, localRot);
            }
        }
    }
}