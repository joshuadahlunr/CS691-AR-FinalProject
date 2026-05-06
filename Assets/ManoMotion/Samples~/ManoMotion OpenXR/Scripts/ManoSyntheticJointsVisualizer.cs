using UnityEngine;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    /// Visual-only: drives Palm + 4 Metacarpals and orients ALL Mano joints so +Z looks
    /// toward the next joint. No spawning, no coloring, no provider calls.
    [ExecuteAlways]
    public class ManoSyntheticJointsVisualizer : MonoBehaviour
    {
        [Header("Handedness (affects palm 'up' construction)")]
        public bool isRight = true;

        // --- (kept) Unused container/prefab fields intentionally omitted per request ---

        [Header("Mano 21 (wire these)")]
        public Transform wrist;

        public Transform thumbMeta, thumbProx, thumbDist, thumbTip; // tip optional

        public Transform indexProx, indexInter, indexDist, indexTip;   // tip optional
        public Transform middleProx, middleInter, middleDist, middleTip;
        public Transform ringProx, ringInter, ringDist, ringTip;
        public Transform littleProx, littleInter, littleDist, littleTip;

        [Header("Synthetic outputs (drag & drop)")]
        public Transform palm;                    // visual palm
        public Transform indexMetacarpal;         // meta (index)
        public Transform middleMetacarpal;        // meta (middle)
        public Transform ringMetacarpal;          // meta (ring)
        public Transform littleMetacarpal;        // meta (little)

        [Header("Placement Tuning")]
        [Tooltip("Metacarpal distance back from proximal (fraction of proximal→wrist length).")]
        [Range(0.05f, 0.5f)] public float metaBackFactor = 0.25f;
        public float metaBackMin = 0.02f;

        [Tooltip("Smooth palm 'up' (0 = off, 1 = heavy).")]
        [Range(0f, 1f)] public float palmUpSmoothing = 0.2f;

        [Header("Palm depth/volume tweak")]
        [Tooltip("Push palm along -palmUp to give the hand a bit of thickness (meters).")]
        public float palmDownOffset = 0f;

        [Header("Optional axis offset AFTER LookRotation(dir, up)")]
        public bool applyAxisOffset = false;
        // you said left=0,0,0 and right=180,180,0 works on device.
        public Vector3 leftAxisEuler = new Vector3(0, 0, 0);
        public Vector3 rightAxisEuler = new Vector3(180, 180, 0);

        // smoothed palm up
        Vector3 _prevPalmUp; bool _havePrevUp;

        void OnEnable() { UpdateNow(); }
        void OnValidate() { if (!Application.isPlaying) UpdateNow(); }
        void LateUpdate() { UpdateNow(); }

        void UpdateNow()
        {
            if (!isActiveAndEnabled) return;

            // --- 1) Palm basis from positions ---
            Vector3 palmUp, palmFwd, palmRight;
            Pose palmPose = ComputePalmPose(out palmUp, out palmFwd, out palmRight);

            // NEW: depth/volume nudge along -palmUp
            if (palmDownOffset != 0f)
                palmPose.position += -palmUp * Mathf.Max(0f, palmDownOffset);

            if (palm)
                palm.SetPositionAndRotation(palmPose.position, palmPose.rotation);

            // --- 2) Place + orient synthetic metas (kept exactly as you had) ---
            PlaceMetacarpal(indexProx, indexMetacarpal, palmUp);
            PlaceMetacarpal(middleProx, middleMetacarpal, palmUp);
            PlaceMetacarpal(ringProx, ringMetacarpal, palmUp);
            PlaceMetacarpal(littleProx, littleMetacarpal, palmUp);

            // --- 3) Orient all real Mano joints so +Z points along the chain ---
            // Wrist (+Z toward middle proximal, else toward average knuckle)
            if (wrist)
            {
                Vector3 dir = palmFwd;
                if (middleProx) dir = (middleProx.position - wrist.position);
                else
                {
                    Vector3 sum = Vector3.zero; int n = 0;
                    if (indexProx) { sum += (indexProx.position - wrist.position); n++; }
                    if (ringProx) { sum += (ringProx.position - wrist.position); n++; }
                    if (littleProx) { sum += (littleProx.position - wrist.position); n++; }
                    if (n > 0) dir = sum / n;
                }
                wrist.rotation = Finalize(Quaternion.LookRotation(SafeDir(dir, palmFwd), SafeUp(palmUp)));
            }

            // Thumb chain
            OrientChain(thumbMeta, thumbProx, palmUp, palmFwd);
            OrientChain(thumbProx, thumbDist, palmUp, palmFwd);
            OrientChainWithTipFallback(thumbDist, thumbTip, thumbProx, palmUp, palmFwd);
            CopyTipRotationFromDistal(thumbDist, thumbTip);   // NEW

            // Index chain
            OrientChain(indexProx, indexInter, palmUp, palmFwd);
            OrientChain(indexInter, indexDist, palmUp, palmFwd);
            OrientChainWithTipFallback(indexDist, indexTip, indexInter, palmUp, palmFwd);
            CopyTipRotationFromDistal(indexDist, indexTip);   // NEW

            // Middle chain
            OrientChain(middleProx, middleInter, palmUp, palmFwd);
            OrientChain(middleInter, middleDist, palmUp, palmFwd);
            OrientChainWithTipFallback(middleDist, middleTip, middleInter, palmUp, palmFwd);
            CopyTipRotationFromDistal(middleDist, middleTip); // NEW

            // Ring chain
            OrientChain(ringProx, ringInter, palmUp, palmFwd);
            OrientChain(ringInter, ringDist, palmUp, palmFwd);
            OrientChainWithTipFallback(ringDist, ringTip, ringInter, palmUp, palmFwd);
            CopyTipRotationFromDistal(ringDist, ringTip);     // NEW

            // Little chain
            OrientChain(littleProx, littleInter, palmUp, palmFwd);
            OrientChain(littleInter, littleDist, palmUp, palmFwd);
            OrientChainWithTipFallback(littleDist, littleTip, littleInter, palmUp, palmFwd);
            CopyTipRotationFromDistal(littleDist, littleTip); // NEW
        }

        // ---------- math / helpers (unchanged except where noted) ----------

        Pose ComputePalmPose(out Vector3 up, out Vector3 fwd, out Vector3 right)
        {
            Vector3 w = wrist ? wrist.position : transform.position;

            if (!indexProx || !middleProx)
            {
                // fallback to local basis
                fwd = transform.forward;
                up = transform.up;
                right = transform.right;
                return new Pose(w, Finalize(Quaternion.LookRotation(fwd, up)));
            }

            Vector3 iP = indexProx.position;
            Vector3 mP = middleProx.position;
            Vector3 lP = littleProx ? littleProx.position : (iP + (mP - iP));

            // forward = average (prox -> inter), else wrist -> middle
            fwd = Vector3.zero; int cnt = 0;
            if (indexInter) { fwd += (indexInter.position - iP); cnt++; }
            if (middleInter) { fwd += (middleInter.position - mP); cnt++; }
            if (littleInter) { fwd += (littleInter.position - lP); cnt++; }
            if (cnt == 0) fwd = (mP - w);
            fwd = SafeDir(fwd, Vector3.forward);

            // right = index -> little
            right = SafeDir(lP - iP, Vector3.right);

            // right-handed: up = right × forward
            up = Vector3.Cross(right, fwd);
            up = SafeUp(up);

            // smooth up to avoid flips
            if (palmUpSmoothing > 0f)
            {
                if (!_havePrevUp) { _prevPalmUp = up; _havePrevUp = true; }
                float a = 1f - Mathf.Pow(1f - palmUpSmoothing, Mathf.Clamp01(Time.deltaTime * 60f));
                up = Vector3.Slerp(_prevPalmUp, up, a).normalized;
                _prevPalmUp = up;
            }

            // position near knuckles, nudged toward wrist
            Vector3 centroid = (iP + mP + lP) / 3f;
            Vector3 pos = Vector3.Lerp(centroid, w, 0.25f);

            Quaternion rot = Finalize(Quaternion.LookRotation(fwd, up));
            return new Pose(pos, rot);
        }

        void PlaceMetacarpal(Transform prox, Transform metaOut, Vector3 palmUp)
        {
            if (!metaOut || !prox || !wrist) return;

            // position: back from proximal toward wrist
            Vector3 toWrist = wrist.position - prox.position;
            float back = Mathf.Max(metaBackMin, toWrist.magnitude * metaBackFactor);
            Vector3 pos = prox.position +
                          (toWrist.sqrMagnitude > 1e-8f ? toWrist.normalized : -Vector3.forward) *
                          Mathf.Min(back, Mathf.Max(0f, toWrist.magnitude * 0.5f));

            // rotation: +Z aims at proximal
            Vector3 dir = prox.position - pos;
            Quaternion rot = Finalize(Quaternion.LookRotation(SafeDir(dir, Vector3.forward), SafeUp(palmUp)));

            metaOut.position = pos;
            metaOut.rotation = rot;
        }

        void OrientChain(Transform a, Transform b, Vector3 up, Vector3 palmFwd)
        {
            if (!a) return;
            Vector3 dir = (b ? (b.position - a.position) : palmFwd);
            a.rotation = Finalize(Quaternion.LookRotation(SafeDir(dir, palmFwd), SafeUp(up)));
        }

        // when 'tip' is missing, aim distal along its incoming direction (continue the line)
        void OrientChainWithTipFallback(Transform distal, Transform tip, Transform prev, Vector3 up, Vector3 palmFwd)
        {
            if (!distal) return;
            Vector3 dir;
            if (tip) dir = tip.position - distal.position;
            else if (prev) dir = distal.position - prev.position;
            else dir = palmFwd;

            distal.rotation = Finalize(Quaternion.LookRotation(SafeDir(dir, palmFwd), SafeUp(up)));
        }

        // NEW: copy tip rotation from distal if tip exists (keeps tips coherent even if source doesn’t supply orientation)
        void CopyTipRotationFromDistal(Transform distal, Transform tip)
        {
            if (tip && distal)
                tip.rotation = distal.rotation;
        }

        Quaternion Finalize(Quaternion lookRot)
        {
            if (!applyAxisOffset) return lookRot;
            Quaternion axis = Quaternion.Euler(isRight ? rightAxisEuler : leftAxisEuler);
            return lookRot * axis;
        }

        static Vector3 SafeDir(Vector3 v, Vector3 fallback)
            => (v.sqrMagnitude < 1e-8f ? fallback : v).normalized;

        static Vector3 SafeUp(Vector3 v)
            => (v.sqrMagnitude < 1e-8f ? Vector3.up : v).normalized;
    }
}