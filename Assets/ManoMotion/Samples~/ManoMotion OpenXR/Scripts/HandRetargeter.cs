/*
 * HandRetargeter.cs  —  Unity 2021+ / 6.x
 * Two retarget modes:
 *   1) Muscles: copy finger muscles from a source Humanoid to the target Humanoid
 *   2) Bone Rotations: copy local rotations for all finger bones (with optional offsets)
 *
 * Attach this script to the TARGET avatar (same GameObject as its Animator).
 * Source must be a Humanoid Animator in the scene (or provide its Avatar + root explicitly).
 *
 * Runtime cost is tiny; no per-frame allocations.
 */

using System;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DefaultExecutionOrder(10000)] // run late; still choose where to apply via applyInLateUpdate
    public class HandRetargeter : MonoBehaviour
    {
        public enum RetargetMode { Muscles, BoneRotations }

        [Header("Mode")]
        public RetargetMode mode = RetargetMode.Muscles;
        [Range(0f, 1f)] public float weight = 1f;
        public bool leftHand = true;
        public bool rightHand = true;

        [Tooltip("Apply in LateUpdate (use if your Animation Rigging graph runs after OnAnimatorIK). Else applies in OnAnimatorIK.")]
        public bool applyInLateUpdate = true;

        [Header("Source (Humanoid)")]
        [Tooltip("Humanoid Animator that currently has the desired hand animation/pose.")]
        public Animator sourceAnimator;
        [Tooltip("Optional override. If null, sourceAnimator.transform is used.")]
        public Transform sourceRootOverride;

        [Header("Target (Humanoid)")]
        [Tooltip("Leave empty to auto-grab Animator on this GameObject.")]
        public Animator targetAnimator;

        [Header("Bone Rotation mode")]
        [Tooltip("If true, compute per-bone offsets once so rotations line up across different rigs/rest-poses.")]
        public bool maintainOffsets = true;
        [Tooltip("If true, assumes rigs are identical; uses identity offsets for maximal fidelity.")]
        public bool assumeIdenticalSkeleton = false;
        [Tooltip("Also copy the wrist local rotation (LeftHand/RightHand). Usually leave OFF if arm IK drives wrist.")]
        public bool driveWristRotation = false;

        // --- internal caches ---
        HumanPoseHandler _srcHPH, _dstHPH;
        HumanPose _srcPose, _dstPose;
        int[] _leftFingerMuscles, _rightFingerMuscles;

        // Bone lists (Humanoid enums) for each hand
        static readonly HumanBodyBones[] LeftHandBones =
        {
        HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
        HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal
    };

        static readonly HumanBodyBones[] RightHandBones =
        {
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
    };

        // Cached bone transforms & offsets
        readonly Dictionary<HumanBodyBones, Transform> _srcBoneT = new();
        readonly Dictionary<HumanBodyBones, Transform> _dstBoneT = new();
        readonly Dictionary<HumanBodyBones, Quaternion> _offset = new(); // dstLocal = offset * srcLocal

        Transform _srcRoot;

        void Reset()
        {
            targetAnimator = GetComponent<Animator>();
        }

        void OnEnable()
        {
            if (!targetAnimator) targetAnimator = GetComponent<Animator>();
            if (!ValidateHumanoid(targetAnimator?.avatar, "Target")) { enabled = false; return; }

            if (!sourceAnimator) { Debug.LogError("HandRetargeter: Source Animator not assigned."); enabled = false; return; }
            if (!ValidateHumanoid(sourceAnimator.avatar, "Source")) { enabled = false; return; }

            _srcRoot = sourceRootOverride ? sourceRootOverride : sourceAnimator.transform;

            BuildMuscleIndexLists();     // for muscle mode
            CacheBoneTransforms();       // for bone mode
            BuildPoseHandlers();         // for muscle mode
            BuildOffsets();              // for bone mode
        }

        void OnDisable()
        {
            DisposePoseHandlers();
        }

        // In OnAnimatorIK / LateUpdate keep the timing split:
        void OnAnimatorIK(int layerIndex)
        {
            if (mode == RetargetMode.Muscles && !applyInLateUpdate) Apply();
        }
        void LateUpdate()
        {
            if (mode == RetargetMode.BoneRotations || applyInLateUpdate) Apply();
        }

        void Apply()
        {
            if (weight <= 0f) return;

            switch (mode)
            {
                case RetargetMode.Muscles:
                    ApplyMuscles();
                    break;

                case RetargetMode.BoneRotations:
                    ApplyBoneRotations();
                    break;
            }
        }

        // ---------------------------
        // Mode 1: Finger Muscle copy
        // ---------------------------
        void BuildPoseHandlers()
        {
            DisposePoseHandlers();
            _srcHPH = new HumanPoseHandler(sourceAnimator.avatar, _srcRoot);
            _dstHPH = new HumanPoseHandler(targetAnimator.avatar, targetAnimator.transform);
            _srcPose = default; _dstPose = default;
        }

        void DisposePoseHandlers()
        {
            _srcHPH?.Dispose(); _srcHPH = null;
            _dstHPH?.Dispose(); _dstHPH = null;
        }

        void BuildMuscleIndexLists()
        {
            var names = HumanTrait.MuscleName;
            var leftList = new List<int>(20);
            var rightList = new List<int>(20);

            for (int i = 0; i < names.Length; i++)
            {
                var n = names[i];
                // We want fingers only: spreads + 1/2/3 Stretched
                bool isFinger = (n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"));
                bool isFingerDOF = isFinger && (n.Contains("Stretched") || n.Contains("Spread"));
                if (!isFingerDOF) continue;

                if (n.StartsWith("Left")) leftList.Add(i);
                else if (n.StartsWith("Right")) rightList.Add(i);
            }

            _leftFingerMuscles = leftList.ToArray();
            _rightFingerMuscles = rightList.ToArray();
        }

        void ApplyMuscles()
        {
            if (_srcHPH == null || _dstHPH == null) return;

            _srcHPH.GetHumanPose(ref _srcPose);
            _dstHPH.GetHumanPose(ref _dstPose);

            if (leftHand)
                BlendMuscles(_leftFingerMuscles, ref _dstPose.muscles, _srcPose.muscles, weight);
            if (rightHand)
                BlendMuscles(_rightFingerMuscles, ref _dstPose.muscles, _srcPose.muscles, weight);



            // In ApplyMuscles(), just before SetHumanPose:
            var hipsHB = HumanBodyBones.Hips;
            var hipsT = targetAnimator.GetBoneTransform(hipsHB);
            Vector3 hipsLocalPos = Vector3.zero;
            Quaternion hipsLocalRot = Quaternion.identity;
            if (hipsT)
            {
                hipsLocalPos = hipsT.localPosition;
                hipsLocalRot = hipsT.localRotation;
            }

            _dstHPH.SetHumanPose(ref _dstPose);

            if (hipsT)
            {
                hipsT.localPosition = hipsLocalPos;
                hipsT.localRotation = hipsLocalRot;
            }



            // Optional: also drive wrist rotation in muscle mode (rarely needed)
            if (driveWristRotation)
            {
                if (leftHand)
                    CopySingleBoneLocalRotation(HumanBodyBones.LeftHand);
                if (rightHand)
                    CopySingleBoneLocalRotation(HumanBodyBones.RightHand);
            }
        }

        static void BlendMuscles(int[] idx, ref float[] dst, float[] src, float w)
        {
            if (idx == null) return;
            for (int i = 0; i < idx.Length; i++)
            {
                int m = idx[i];
                dst[m] = Mathf.Lerp(dst[m], src[m], w);
            }
        }

        // --------------------------------
        // Mode 2: Bone local rotation copy
        // --------------------------------
        void CacheBoneTransforms()
        {
            _srcBoneT.Clear();
            _dstBoneT.Clear();

            // Cache both hands
            CacheForHand(LeftHandBones);
            CacheForHand(RightHandBones);

            void CacheForHand(HumanBodyBones[] list)
            {
                foreach (var hb in list)
                {
                    var srcT = sourceAnimator.GetBoneTransform(hb);
                    var dstT = targetAnimator.GetBoneTransform(hb);
                    if (srcT) _srcBoneT[hb] = srcT;
                    if (dstT) _dstBoneT[hb] = dstT;
                }
                // Wrist (optional)
                var wrist = (list == LeftHandBones) ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                var sW = sourceAnimator.GetBoneTransform(wrist);
                var dW = targetAnimator.GetBoneTransform(wrist);
                if (sW) _srcBoneT[wrist] = sW;
                if (dW) _dstBoneT[wrist] = dW;
            }
        }

        void BuildOffsets()
        {
            _offset.Clear();

            // If we assume identical skeletons, every offset = identity.
            if (assumeIdenticalSkeleton)
            {
                foreach (var hb in _dstBoneT.Keys)
                    _offset[hb] = Quaternion.identity;
                return;
            }

            if (!maintainOffsets)
            {
                foreach (var hb in _dstBoneT.Keys)
                    _offset[hb] = Quaternion.identity;
                return;
            }

            // Compute per-bone offset from current local pose
            foreach (var kv in _dstBoneT)
            {
                var hb = kv.Key;
                var dstT = kv.Value;
                if (!_srcBoneT.TryGetValue(hb, out var srcT) || dstT == null || srcT == null)
                    continue;

                // dstLocal = offset * srcLocal  =>  offset = dstLocal * inverse(srcLocal)
                _offset[hb] = dstT.localRotation * Quaternion.Inverse(srcT.localRotation);
            }
        }

        void ApplyBoneRotations()
        {
            if (leftHand) CopyHandBones(LeftHandBones);
            if (rightHand) CopyHandBones(RightHandBones);

            if (driveWristRotation)
            {
                if (leftHand) CopySingleBoneLocalRotation(HumanBodyBones.LeftHand);
                if (rightHand) CopySingleBoneLocalRotation(HumanBodyBones.RightHand);
            }
        }

        void CopyHandBones(HumanBodyBones[] list)
        {
            for (int i = 0; i < list.Length; i++)
            {
                var hb = list[i];
                CopySingleBoneLocalRotation(hb);
            }
        }

        void CopySingleBoneLocalRotation(HumanBodyBones hb)
        {
            if (!_dstBoneT.TryGetValue(hb, out var dst) || dst == null) return;
            if (!_srcBoneT.TryGetValue(hb, out var src) || src == null) return;

            // Apply: dstLocal = Lerp(dstLocal, offset * srcLocal, weight)
            var srcLocal = src.localRotation;
            if (!_offset.TryGetValue(hb, out var off)) off = Quaternion.identity;

            var targetLocal = off * srcLocal;
            if (weight >= 1f) dst.localRotation = targetLocal;
            else dst.localRotation = Quaternion.Slerp(dst.localRotation, targetLocal, weight);
        }

        // --------------------------------
        // Utilities / Validation / Editor
        // --------------------------------
        static bool ValidateHumanoid(Avatar avatar, string label)
        {
            if (!avatar)
            {
                Debug.LogError($"HandRetargeter: {label} Avatar is missing.");
                return false;
            }
            if (!avatar.isHuman || !avatar.isValid)
            {
                Debug.LogError($"HandRetargeter: {label} Avatar must be a valid Humanoid.");
                return false;
            }
            return true;
        }

        [ContextMenu("Rebuild Caches Now")]
        public void RebuildCaches()
        {
            CacheBoneTransforms();
            BuildOffsets();
            BuildPoseHandlers();
            Debug.Log("HandRetargeter: Caches rebuilt.");
        }

        [ContextMenu("Force Identity Offsets (1:1 skeletons)")]
        public void ForceIdentityOffsets()
        {
            assumeIdenticalSkeleton = true;
            maintainOffsets = false;
            BuildOffsets();
            Debug.Log("HandRetargeter: All bone offsets set to identity.");
        }
    }
}
