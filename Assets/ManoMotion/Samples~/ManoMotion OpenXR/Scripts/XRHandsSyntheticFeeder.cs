// Assets/Scripts/XRHandsSyntheticFeeder.cs
using UnityEngine;
using UnityEngine.XR.Hands;
using Custom.XR;
using Unity.XR.CoreUtils;   // for XROrigin
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    public class XRHandsSyntheticFeeder : MonoBehaviour
    {
        [Header("XR Origin / Tracking Space")]
        [Tooltip("If left empty, the first XROrigin in the scene will be used. " +
                 "All joint poses are converted from WORLD to ORIGIN space before feeding XR Hands.")]
        [SerializeField] XROrigin xrOrigin;

        [Header("Placement (world-space seed)")]
        [SerializeField] Vector3 leftOrigin = new Vector3(-0.25f, 1.25f, 0.6f);
        [SerializeField] Vector3 rightOrigin = new Vector3(0.25f, 1.25f, 0.6f);
        [SerializeField] Vector3 forward = new Vector3(0, 0, 1); // +Z out of palm
        [SerializeField] Vector3 up = new Vector3(0, 1, 0);

        [Header("Animation")]
        [SerializeField] bool animate = true;
        [SerializeField, Range(0f, 70f)] float fingerCurlAmplitudeDeg = 50f;
        [SerializeField, Range(0f, 30f)] float fingerSpreadDeg = 12f;
        [SerializeField, Range(0.2f, 4f)] float speed = 1.6f;
        [SerializeField] float wristBobHeight = 0.05f;
        [SerializeField] float wristYawDeg = 25f;

        [Header("Bone lengths (m)")]
        [SerializeField] float palmToWrist = 0.05f;
        [SerializeField] float palmWidth = 0.085f;
        [SerializeField] float proxLen = 0.04f;
        [SerializeField] float interLen = 0.03f;
        [SerializeField] float distalLen = 0.022f;
        [SerializeField] float thumbMetaLen = 0.032f;
        [SerializeField] float thumbProxLen = 0.03f;
        [SerializeField] float thumbDistalLen = 0.025f;

        [Header("XR Hands")]
        [SerializeField] float defaultRadius = 0.015f;
        [SerializeField] bool drawIndexForwardRay = true;

        float _t;
        Transform _origin; // the transform that defines XR "origin space" (XROrigin.Origin)

        void Awake()
        {
            if (xrOrigin == null)
            {
                // Try to find one at runtime; okay on mobile/editor too.
#if UNITY_2023_1_OR_NEWER
                xrOrigin = Object.FindFirstObjectByType<XROrigin>();
#else
            xrOrigin = FindObjectOfType<XROrigin>();
#endif
            }

            _origin = xrOrigin != null ? xrOrigin.Origin.transform : null;

#if UNITY_EDITOR
        if (_origin == null)
            Debug.LogWarning("[XRHandsSyntheticFeeder] No XROrigin found/assigned. " +
                             "Poses will be treated as origin-space already (no conversion).");
#endif
        }

        void OnEnable() => Application.onBeforeRender += OnBeforeRender;
        void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        void Update()
        {
            _t = animate ? Time.time * speed : 0f;
            EmitFrame("Dynamic");
        }

        void OnBeforeRender() => EmitFrame("BeforeRender");

        void EmitFrame(string phase)
        {
            var sub = CustomHandSubsystem.Instance;
            if (sub == null || !sub.running) return;

            float curlThumb = Mathf.Deg2Rad * (Mathf.Sin(_t) * fingerCurlAmplitudeDeg * 0.6f);
            float curlOther = Mathf.Deg2Rad * (Mathf.Sin(_t) * fingerCurlAmplitudeDeg);
            float bob = wristBobHeight * Mathf.Sin(_t * 0.7f);
            float yaw = wristYawDeg * Mathf.Sin(_t * 0.5f);

            EmitHand(sub, Handedness.Left, leftOrigin + new Vector3(0, bob, 0), false, yaw, curlThumb, curlOther);
            EmitHand(sub, Handedness.Right, rightOrigin + new Vector3(0, bob, 0), true, -yaw, curlThumb, curlOther);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Debug once per frame (Update only) to keep logs tidy
        if (phase == "Dynamic")
            Debug.Log($"[Feeder/{phase}] pushed frame {Time.frameCount}");
#endif
        }

        void EmitHand(CustomHandSubsystem sub, Handedness hand, Vector3 originWorld, bool isRight,
                      float yawDeg, float curlThumb, float curlOther)
        {
            // Build in WORLD space first (easier reasoning), then convert → ORIGIN space once per joint.
            var fwd = forward.normalized;
            var upv = up.normalized;
            var rotWorld = Quaternion.AngleAxis(yawDeg, upv) * Quaternion.LookRotation(fwd, upv);

            var wristPosWorld = originWorld - fwd * palmToWrist;
            var palmPosWorld = originWorld;

            // Wrist + Palm
            sub.SetJointPose(hand, XRHandJointID.Wrist, ToOriginSpace(new Pose(wristPosWorld, rotWorld)), defaultRadius);
            sub.SetJointPose(hand, XRHandJointID.Palm, ToOriginSpace(new Pose(palmPosWorld, rotWorld)), defaultRadius);

            float side = isRight ? 1f : -1f;
            Vector3 latWorld = Vector3.Cross(upv, fwd).normalized * side;
            float[] lateral = { 0.50f, 0.20f, 0.00f, -0.20f, -0.40f }; // thumb..little

            BuildThumb(sub, hand, palmPosWorld, rotWorld, latWorld, lateral[0] * palmWidth, curlThumb);
            BuildFinger(sub, hand, Finger.Index, palmPosWorld, rotWorld, latWorld, lateral[1] * palmWidth, curlOther);
            BuildFinger(sub, hand, Finger.Middle, palmPosWorld, rotWorld, latWorld, lateral[2] * palmWidth, curlOther);
            BuildFinger(sub, hand, Finger.Ring, palmPosWorld, rotWorld, latWorld, lateral[3] * palmWidth, curlOther);
            BuildFinger(sub, hand, Finger.Little, palmPosWorld, rotWorld, latWorld, lateral[4] * palmWidth, curlOther);

            if (drawIndexForwardRay)
            {
                var tipPosWorld = palmPosWorld
                                  + latWorld * (lateral[1] * palmWidth)
                                  + rotWorld * (Vector3.forward * (proxLen + interLen + distalLen + 0.012f));
                Debug.DrawRay(tipPosWorld, rotWorld * Vector3.forward * 0.12f, Color.cyan, 0f, false);
            }
        }

        enum Finger { Index, Middle, Ring, Little }

        void BuildThumb(CustomHandSubsystem sub, Handedness hand, Vector3 palmPosW, Quaternion palmRotW,
                        Vector3 latW, float lateral, float curl)
        {
            Vector3 basePosW = palmPosW + latW * lateral + palmRotW * (Vector3.forward * 0.018f);
            Quaternion baseRotW = palmRotW * Quaternion.AngleAxis(-28f, Vector3.up);

            Pose m = new Pose(basePosW, baseRotW);
            Pose p = Advance(m, thumbMetaLen, curl * 0.6f);
            Pose d = Advance(p, thumbProxLen, curl * 0.85f);
            Pose t = Advance(d, thumbDistalLen, curl);

            sub.SetJointPose(hand, XRHandJointID.ThumbMetacarpal, ToOriginSpace(m), defaultRadius);
            sub.SetJointPose(hand, XRHandJointID.ThumbProximal, ToOriginSpace(p), defaultRadius);
            sub.SetJointPose(hand, XRHandJointID.ThumbDistal, ToOriginSpace(d), defaultRadius);
            sub.SetJointPose(hand, XRHandJointID.ThumbTip, ToOriginSpace(t), defaultRadius);
        }

        void BuildFinger(CustomHandSubsystem sub, Handedness hand, Finger f,
                         Vector3 palmPosW, Quaternion palmRotW, Vector3 latW, float lateral, float curl)
        {
            float spread = f switch
            {
                Finger.Index => fingerSpreadDeg,
                Finger.Middle => 0f,
                Finger.Ring => -fingerSpreadDeg,
                _ => -fingerSpreadDeg * 1.6f
            };

            Pose m = new Pose(palmPosW + latW * lateral, palmRotW * Quaternion.AngleAxis(spread, Vector3.up));
            Pose p = Advance(m, proxLen, curl * 0.35f);
            Pose i = Advance(p, interLen, curl * 0.55f);
            Pose d = Advance(i, distalLen, curl * 0.75f);
            Pose t = Advance(d, 0.012f, curl);

            XRHandJointID mid, prox, inter, dist, tip;
            switch (f)
            {
                case Finger.Index:
                    mid = XRHandJointID.IndexMetacarpal; prox = XRHandJointID.IndexProximal;
                    inter = XRHandJointID.IndexIntermediate; dist = XRHandJointID.IndexDistal; tip = XRHandJointID.IndexTip; break;
                case Finger.Middle:
                    mid = XRHandJointID.MiddleMetacarpal; prox = XRHandJointID.MiddleProximal;
                    inter = XRHandJointID.MiddleIntermediate; dist = XRHandJointID.MiddleDistal; tip = XRHandJointID.MiddleTip; break;
                case Finger.Ring:
                    mid = XRHandJointID.RingMetacarpal; prox = XRHandJointID.RingProximal;
                    inter = XRHandJointID.RingIntermediate; dist = XRHandJointID.RingDistal; tip = XRHandJointID.RingTip; break;
                default:
                    mid = XRHandJointID.LittleMetacarpal; prox = XRHandJointID.LittleProximal;
                    inter = XRHandJointID.LittleIntermediate; dist = XRHandJointID.LittleDistal; tip = XRHandJointID.LittleTip; break;
            }

            sub.SetJointPose(hand, mid, ToOriginSpace(m), defaultRadius);
            sub.SetJointPose(hand, prox, ToOriginSpace(p), defaultRadius);
            sub.SetJointPose(hand, inter, ToOriginSpace(i), defaultRadius);
            sub.SetJointPose(hand, dist, ToOriginSpace(d), defaultRadius);
            sub.SetJointPose(hand, tip, ToOriginSpace(t), defaultRadius);
        }

        // Advance along the current +Z of "from" by len; curl about local +X (finger flexion).
        Pose Advance(Pose from, float len, float curlRad)
        {
            var rotW = from.rotation * Quaternion.AngleAxis(Mathf.Rad2Deg * curlRad, Vector3.right);
            var posW = from.position + (rotW * Vector3.forward) * len;
            return new Pose(posW, rotW);
        }

        // --------- Helpers ---------

        // Convert a WORLD-space pose to ORIGIN-space (tracking space) for XR Hands.
        Pose ToOriginSpace(Pose worldPose)
        {
            if (_origin == null) return worldPose; // no origin in scene ⇒ treat as origin space already
            var invRot = Quaternion.Inverse(_origin.rotation);
            var localPos = invRot * (worldPose.position - _origin.position);
            var localRot = invRot * worldPose.rotation;
            return new Pose(localPos, localRot);
        }
    }
}