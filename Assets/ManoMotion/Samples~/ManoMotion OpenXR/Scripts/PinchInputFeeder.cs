// Assets/Input/PinchForceSelect.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]
    public class PinchInputFeeder : MonoBehaviour
    {
        [Header("Interactor lookup")]
        public NearFarInteractor interactor;      // optional manual assign
        public bool findInteractorByTag = true;   // auto-find at runtime
        public string interactorTag = "LeftNearFarInteractor";
        public bool retryUntilFound = true;
        public float retryTimeoutSeconds = 5f;

        [Header("Hand")]
        public HandChannel channel = HandChannel.Left; // set Right on right-hand

        [Header("Pinch source (thumb/index)")]
        public Transform thumbTip;
        public Transform indexTip;

        [Header("Thresholds (meters)")]
        public float pinchOnDistance = 0.025f;
        public float pinchOffDistance = 0.035f;

        [Header("Yield to UI when pinching over UI")]
        public bool preferUI = true;

        IXRSelectInteractable current;
        readonly List<IXRInteractable> cache = new();

        bool pressedPrev;
        Coroutine finder;

        void OnEnable()
        {
            if (!interactor) TryGetComponent(out interactor);
            if (findInteractorByTag && (!interactor || !HasExpectedTag(interactor)))
                finder = StartCoroutine(FindInteractorRoutine());
        }

        void OnDisable()
        {
            if (finder != null) { StopCoroutine(finder); finder = null; }
        }

        bool HasExpectedTag(NearFarInteractor i) =>
            i && i.gameObject.CompareTag(interactorTag);

        IEnumerator FindInteractorRoutine()
        {
            float t0 = Time.unscaledTime;

            while (true)
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
                        yield break;
                    }
                }

                if (!retryUntilFound || (Time.unscaledTime - t0) > retryTimeoutSeconds)
                {
                    if (!interactor)
                        Debug.LogError($"[PinchForceSelect] Interactor with tag '{interactorTag}' not found.", this);
                    yield break;
                }

                yield return null;
            }
        }

        void Update()
        {
            if (!interactor || interactor.interactionManager == null) return;
            if (!thumbTip || !indexTip) return;

            int ch = (int)channel;

            // Hysteresis pinch state from distance
            float d = Vector3.Distance(thumbTip.position, indexTip.position);
            bool pressed = pressedPrev ? d <= pinchOffDistance : d <= pinchOnDistance;

            // Optional UI gating
            bool uiBusy = preferUI && PinchExternalBus.uiIsHolding[ch];

            // Rising edge → start
            if (pressed && !pressedPrev && !uiBusy && !interactor.hasSelection)
                StartSelect();

            // Falling edge → end
            if (!pressed && pressedPrev && interactor.hasSelection)
                EndSelect();

            pressedPrev = pressed;
        }

        void StartSelect()
        {
            cache.Clear();
            interactor.GetValidTargets(cache);
            if (cache.Count > 0 && cache[0] is IXRSelectInteractable sel)
            {
                interactor.StartManualInteraction(sel);
                current = sel;
#if UNITY_EDITOR
            Debug.Log($"[PinchForceSelect] StartManualInteraction → {sel.transform.name}");
#endif
            }
#if UNITY_EDITOR
        else
        {
            Debug.Log("[PinchForceSelect] No valid 3D target under ray.");
        }
#endif
        }

        void EndSelect()
        {
            if (interactor.hasSelection)
            {
                interactor.EndManualInteraction();
#if UNITY_EDITOR
            Debug.Log($"[PinchForceSelect] EndManualInteraction → {(current != null ? current.transform.name : "null")}");
#endif
                current = null;
            }
        }
    }
}