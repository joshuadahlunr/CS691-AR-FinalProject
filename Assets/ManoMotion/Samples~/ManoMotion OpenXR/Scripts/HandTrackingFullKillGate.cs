using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using ManoMotion;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)] // run after providers/feeders
    public class HandTrackingFullKillGate : MonoBehaviour
    {
        [System.Serializable]
        public class HandSlot
        {
            [Header("Hand Slot")]
            public string label = "Right";         // For inspector clarity
            [Tooltip("ManoMotion HandInfos index. Typically 0=Right, 1=Left.")]
            public int handIndex = 0;

            [Header("Interactor (must stay active)")]
            public NearFarInteractor interactor;

            [Header("Tracking joint for 'behind camera' test")]
            public Transform trackingJoint;

            [Header("Roots to disable (max perf)")]
            [Tooltip("ManoMotion visual/skeleton root (often spawned at runtime).")]
            public GameObject manoHandRoot;
            [Tooltip("XR Hands/OpenXR hand root (optional).")]
            public GameObject xrHandsRoot;

            [Header("Auto-find Mano root by tag")]
            public bool autoFindManoRootByTag = true;
            public string tagName = "Right";       // "Right" or "Left"
            public float findTimeoutSeconds = 5f;

            [Header("Also silence these scripts when untracked")]
            public List<MonoBehaviour> feeders = new(); // PinchInputFeeder, gesture drivers, etc.

            [Header("Optional visuals")]
            public CurveVisualController curveVisual;

            // --- internal state ---
            [HideInInspector] public InteractionLayerMask savedLayers;
            [HideInInspector] public bool muted;
            [HideInInspector] public float timer;
            [HideInInspector] public Coroutine finder;
        }

        [Header("Camera")]
        public Camera xrCamera;

        [Header("Right Hand")]
        public HandSlot right = new HandSlot
        {
            label = "Right",
            handIndex = 0,
            tagName = "Right"
        };

        [Header("Left Hand")]
        public HandSlot left = new HandSlot
        {
            label = "Left",
            handIndex = 1,
            tagName = "Left"
        };

        [Header("Tracking thresholds & smoothing")]
        public float minConfidence = 0.001f;
        public bool hideWhenBehindCamera = true;
        [Tooltip("Delay to confirm lost/acquired (avoid flicker).")]
        public float lostHysteresis = 0.05f, acquiredHysteresis = 0.05f;

        [Header("Diagnostics")]
        public bool warnIfGateInsideDeactivatedRoots = true;

        void Reset()
        {
            if (!xrCamera) xrCamera = Camera.main;
        }

        void Awake()
        {
            if (!xrCamera) xrCamera = Camera.main;

            // Nice-to-have: warn if this gate lives under a root it will disable.
            if (warnIfGateInsideDeactivatedRoots)
            {
                WarnIfInsideRoot(right);
                WarnIfInsideRoot(left);
            }
        }

        void OnEnable()
        {
            CacheLayers(right);
            CacheLayers(left);

            // Optional auto-find by tag for Mano roots
            if (right.autoFindManoRootByTag && !right.manoHandRoot)
                right.finder = StartCoroutine(FindManoRootByTagRoutine(right));
            if (left.autoFindManoRootByTag && !left.manoHandRoot)
                left.finder = StartCoroutine(FindManoRootByTagRoutine(left));
        }

        void OnDisable()
        {
            if (right.finder != null) { StopCoroutine(right.finder); right.finder = null; }
            if (left.finder != null) { StopCoroutine(left.finder); left.finder = null; }
        }

        void LateUpdate()
        {
            Step(right);
            Step(left);
        }

        // ---------- Core loop per hand ----------
        void Step(HandSlot h)
        {
            if (h.interactor == null || xrCamera == null) return;

            bool tracked = IsManoTracked(h);
            bool targetMuted = !tracked;

            if (targetMuted != h.muted)
            {
                h.timer += Time.deltaTime;
                float need = targetMuted ? lostHysteresis : acquiredHysteresis;
                if (h.timer >= need)
                {
                    h.timer = 0f;
                    if (targetMuted) MuteAndDeactivate(h);
                    else ReactivateAndUnmute(h);
                    h.muted = targetMuted;
                }
                return;
            }

            h.timer = 0f;
        }

        bool IsManoTracked(HandSlot h)
        {
            var mm = ManoMotionManager.Instance;
            if (!mm) return false;

            var infos = mm.HandInfos;
            if (infos == null || infos.Length <= h.handIndex) return false;

            // 1) Confidence gate
            if (infos[h.handIndex].trackingInfo.skeleton.confidence < minConfidence)
                return false;

            // 2) Optional behind-camera gate
            if (hideWhenBehindCamera && h.trackingJoint)
            {
                Vector3 toJoint = h.trackingJoint.position - xrCamera.transform.position;
                if (Vector3.Dot(xrCamera.transform.forward, toJoint) <= 0f)
                    return false;
            }

            return true;
        }

        void MuteAndDeactivate(HandSlot h)
        {
            if (!h.interactor) return;

            // Snapshot references/state BEFORE we touch any flags/layers/roots
            var mgr = h.interactor.interactionManager;
            var sel = h.interactor as IXRSelectInteractor;
            bool hadSelection = h.interactor.hasSelection;

            // 1) End any manual interaction first
            if (h.interactor.isPerformingManualInteraction)
                h.interactor.EndManualInteraction();

            // 2) Cancel selection while the interactor is still "normal"
            if (hadSelection && mgr != null && sel != null)
            {
                try { mgr.CancelInteractorSelection(sel); }
                catch (System.Exception ex)
                {
                    // Guard against “unselecting non-selected” spam on edge frames
                    Debug.LogWarning($"[HandTrackingFullKillGate] Safe cancel guard: {ex.Message}", this);
                }
            }

            // 3) Now block the interactor
            h.interactor.allowHover = false;
            h.interactor.allowSelect = false;
            h.interactor.enableNearCasting = false;
            h.interactor.enableFarCasting = false;
            h.interactor.interactionLayers = (InteractionLayerMask)0;

            if (h.curveVisual) h.curveVisual.enabled = false;

            // 4) Silence feeders
            foreach (var f in h.feeders) if (f) f.enabled = false;

            // 5) Finally, disable hand roots (after XRI state is settled)
            if (h.manoHandRoot && h.manoHandRoot.activeSelf) h.manoHandRoot.SetActive(false);
            if (h.xrHandsRoot && h.xrHandsRoot.activeSelf) h.xrHandsRoot.SetActive(false);
        }

        void ReactivateAndUnmute(HandSlot h)
        {
            // 1) Reactivate roots first
            if (h.manoHandRoot && !h.manoHandRoot.activeSelf) h.manoHandRoot.SetActive(true);
            if (h.xrHandsRoot && !h.xrHandsRoot.activeSelf) h.xrHandsRoot.SetActive(true);

            // 2) Reopen the interactor
            h.interactor.interactionLayers = h.savedLayers;
            h.interactor.enableNearCasting = true;
            h.interactor.enableFarCasting = true;
            h.interactor.allowHover = true;
            h.interactor.allowSelect = true;

            // 3) Wake feeders/visuals
            foreach (var f in h.feeders) if (f) f.enabled = true;
            if (h.curveVisual) h.curveVisual.enabled = true;
        }

        // ---------- Helpers ----------
        void CacheLayers(HandSlot h)
        {
            if (h.interactor != null)
                h.savedLayers = h.interactor.interactionLayers;
        }

        void WarnIfInsideRoot(HandSlot h)
        {
            if (h.manoHandRoot && transform.IsChildOf(h.manoHandRoot.transform))
                Debug.LogWarning($"[HandTrackingFullKillGate] Script is inside Mano root '{h.manoHandRoot.name}' you plan to disable. Move it to an always-active object.", this);

            if (h.xrHandsRoot && transform.IsChildOf(h.xrHandsRoot.transform))
                Debug.LogWarning($"[HandTrackingFullKillGate] Script is inside XR Hands root '{h.xrHandsRoot.name}' you plan to disable. Move it to an always-active object.", this);
        }

        IEnumerator FindManoRootByTagRoutine(HandSlot h)
        {
            float deadline = Time.time + Mathf.Max(0.1f, h.findTimeoutSeconds);
            while (!h.manoHandRoot && Time.time < deadline)
            {
                h.manoHandRoot = FindByTagSafe(h.tagName);
                yield return null;
            }

            if (!h.manoHandRoot)
                Debug.LogWarning($"[HandTrackingFullKillGate] Could not auto-find Mano root with tag '{h.tagName}'. Assign it in the Inspector.", this);
        }

        static GameObject FindByTagSafe(string tag)
        {
            try { return GameObject.FindGameObjectWithTag(tag); }
            catch { return null; } // Tag doesn't exist -> avoid exception
        }
    }
}