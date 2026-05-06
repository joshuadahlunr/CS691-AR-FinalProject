// Assets/Scripts/HandRetargeter_WristPins.cs
// Forces the humanoid wrist bones to match two reference Transforms (with offsets).
// Does NOT modify any other bones/muscles. Final-authority write (LateUpdate + EndOfFrame).
using UnityEngine;
using System.Collections;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DefaultExecutionOrder(11000)]
    [DisallowMultipleComponent]
    public class HandRetargeter_WristPins : MonoBehaviour
    {
        [Header("Target (Humanoid)")]
        [Tooltip("Leave empty to auto-grab Animator on this GameObject.")]
        public Animator targetAnimator;

        [Header("Reference targets (world-space)")]
        public Transform leftReference;   // required to drive left wrist
        public Transform rightReference;  // required to drive right wrist

        [Header("Offsets (defined in REFERENCE LOCAL space)")]
        public Vector3 leftPosOffset = Vector3.zero;
        public Vector3 leftRotOffsetEuler = Vector3.zero;
        public Vector3 rightPosOffset = Vector3.zero;
        public Vector3 rightRotOffsetEuler = Vector3.zero;

        [Header("Apply toggles")]
        public bool driveLeft = true;
        public bool driveRight = true;

        [Header("Write space")]
        [Tooltip("Recommended ON. Writes in the wrist's parent local space (same space Animator uses).")]
        public bool writeLocal = true;

        [Header("Smoothing (0 = snap)")]
        [Range(0, 60)] public float posDamp = 0f;
        [Range(0, 60)] public float rotDamp = 0f;

        [Header("Extra timing")]
        [Tooltip("Write at EndOfFrame so nothing later can override it.")]
        public bool alsoAtEndOfFrame = true;
        [Tooltip("Write just before render to reduce latency.")]
        public bool alsoOnBeforeRender = true;

        // --- internals ---
        Transform _leftWrist, _rightWrist;
        Quaternion _leftRotOffsetQ = Quaternion.identity;
        Quaternion _rightRotOffsetQ = Quaternion.identity;

        static readonly WaitForEndOfFrame s_waitEOF = new WaitForEndOfFrame();
        Coroutine _eofWriter;

        void Reset()
        {
            targetAnimator = GetComponent<Animator>();
        }

        void Awake()
        {
            RebuildOffsets();
        }

        void OnValidate()
        {
            RebuildOffsets();
        }

        void OnEnable()
        {
            if (!targetAnimator) targetAnimator = GetComponent<Animator>();
            if (!ValidateHumanoid(targetAnimator?.avatar)) { enabled = false; return; }

            CacheWristBones();

            if (alsoOnBeforeRender) Application.onBeforeRender += OnBeforeRenderHook;
            if (alsoAtEndOfFrame) _eofWriter = StartCoroutine(EndOfFrameWriter());
        }

        void OnDisable()
        {
            if (alsoOnBeforeRender) Application.onBeforeRender -= OnBeforeRenderHook;
            if (_eofWriter != null) { StopCoroutine(_eofWriter); _eofWriter = null; }
        }

        void LateUpdate()
        {
            ApplyFrame(Time.deltaTime);
        }

        void OnBeforeRenderHook()
        {
            ApplyFrame(Time.unscaledDeltaTime);
        }

        IEnumerator EndOfFrameWriter()
        {
            while (enabled)
            {
                yield return s_waitEOF; // after LateUpdate/IK/animation jobs
                ApplyFrame(Time.unscaledDeltaTime);
            }
        }

        void ApplyFrame(float dt)
        {
            float tPos = posDamp <= 0f ? 1f : 1f - Mathf.Exp(-posDamp * dt);
            float tRot = rotDamp <= 0f ? 1f : 1f - Mathf.Exp(-rotDamp * dt);

            if (driveLeft && _leftWrist && leftReference)
                DriveOne(_leftWrist, leftReference, leftPosOffset, _leftRotOffsetQ, tPos, tRot, writeLocal);

            if (driveRight && _rightWrist && rightReference)
                DriveOne(_rightWrist, rightReference, rightPosOffset, _rightRotOffsetQ, tPos, tRot, writeLocal);
        }

        static void DriveOne(Transform wrist, Transform reference, Vector3 posOffsetLocal, Quaternion rotOffsetLocal, float tPos, float tRot, bool writeLocal)
        {
            // Desired world pose = reference * offsets(in reference local)
            Quaternion desiredWorldRot = reference.rotation * rotOffsetLocal;
            Vector3 desiredWorldPos = reference.position + reference.rotation * posOffsetLocal;

            if (writeLocal && wrist.parent)
            {
                Transform p = wrist.parent;
                Vector3 desiredLocalPos = p.InverseTransformPoint(desiredWorldPos);
                Quaternion desiredLocalRot = Quaternion.Inverse(p.rotation) * desiredWorldRot;

                if (tPos >= 0.999f && tRot >= 0.999f)
                {
                    wrist.localPosition = desiredLocalPos;
                    wrist.localRotation = desiredLocalRot;
                }
                else
                {
                    wrist.localPosition = Vector3.Lerp(wrist.localPosition, desiredLocalPos, tPos);
                    wrist.localRotation = Quaternion.Slerp(wrist.localRotation, desiredLocalRot, tRot);
                }
            }
            else
            {
                if (tPos >= 0.999f && tRot >= 0.999f)
                    wrist.SetPositionAndRotation(desiredWorldPos, desiredWorldRot);
                else
                    wrist.SetPositionAndRotation(
                        Vector3.Lerp(wrist.position, desiredWorldPos, tPos),
                        Quaternion.Slerp(wrist.rotation, desiredWorldRot, tRot)
                    );
            }
        }

        void CacheWristBones()
        {
            _leftWrist = targetAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightWrist = targetAnimator.GetBoneTransform(HumanBodyBones.RightHand);

            if (!_leftWrist) Debug.LogWarning("HandRetargeter_WristPins: Left wrist bone not found.");
            if (!_rightWrist) Debug.LogWarning("HandRetargeter_WristPins: Right wrist bone not found.");
        }

        void RebuildOffsets()
        {
            _leftRotOffsetQ = Quaternion.Euler(leftRotOffsetEuler);
            _rightRotOffsetQ = Quaternion.Euler(rightRotOffsetEuler);
        }

        static bool ValidateHumanoid(Avatar avatar)
        {
            if (!avatar || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("HandRetargeter_WristPins: Target Animator must have a valid Humanoid Avatar.");
                return false;
            }
            return true;
        }

        [ContextMenu("Re-cache Wrist Bones")]
        public void RecacheBones()
        {
            CacheWristBones();
        }
    }
}