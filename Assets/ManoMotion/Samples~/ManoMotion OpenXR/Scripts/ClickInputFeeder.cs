// Assets/Input/PinchForceUI_ClickOnly.cs
using System.Collections;
using System.Reflection; // for safe XRUI drag-threshold lookup
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public class ClickInputFeeder : MonoBehaviour
    {
        [Header("Interactor lookup")]
        public NearFarInteractor interactor;
        public bool findInteractorByTag = true;
        public string interactorTag = "LeftNearFarInteractor";
        public bool retryUntilFound = true;
        public float retryTimeoutSeconds = 5f;

        [Header("Hand channel (bus)")]
        public HandChannel channel = HandChannel.Left;

        [Header("Pinch source (thumb/index)")]
        public Transform thumbTip;
        public Transform indexTip;

        [Header("Pinch thresholds (meters)")]
        public float pinchOnDistance = 0.025f;
        public float pinchOffDistance = 0.035f;

        [Header("Click semantics")]
        [Tooltip("Require releasing over the same clickable to fire pointerClick.")]
        public bool requireReleaseOverSame = true;

        [Header("Drag (XR-friendly)")]
        [Tooltip("Enable drag gestures for ScrollRect/Slider, etc.")]
        public bool enableDrag = true;

        [Tooltip("Multiply base drag threshold (pixels). Base comes from XRUIInputModule if present, otherwise EventSystem.pixelDragThreshold.")]
        public float dragThresholdMultiplier = 1.0f;

        [Tooltip("Prefer click: require a small hold before allowing drag.")]
        public bool preferClickOverDrag = true;

        [Tooltip("Minimum hold time (seconds) after press before drag may begin.")]
        public float minHoldBeforeDrag = 0.10f; // 100 ms feels good

        [Tooltip("Extra stability: require being over the pixel threshold for this many consecutive frames before starting drag.")]
        public int overThresholdFramesRequired = 2;

        // ---- state ----
        bool pressedPrev;
        bool isHolding;
        bool isDragging;

        GameObject pressedTarget;
        GameObject dragTarget;
        GameObject lastDropTarget;
        RaycastResult pressRaycast;
        Vector2 pressPos;
        Vector2 lastPos;

        bool pendingHoldDown;
        bool pendingHoldUp;

        float pressTime;
        int overThreshFrames;

        Coroutine finder;

        void OnEnable()
        {
            if (!interactor) TryGetComponent(out interactor);
            if (findInteractorByTag && (!interactor || !interactor.gameObject.CompareTag(interactorTag)))
                finder = StartCoroutine(FindInteractorRoutine());

            if (!EventSystem.current)
                Debug.LogWarning("[ClickInputFeeder] No EventSystem present.", this);
        }

        void OnDisable()
        {
            if (finder != null) { StopCoroutine(finder); finder = null; }
            if (isHolding) ForceEndHold();
        }

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
                    if (cand && cand.gameObject.CompareTag(interactorTag)) { interactor = cand; yield break; }

                if (!retryUntilFound || (Time.unscaledTime - t0) > retryTimeoutSeconds)
                {
                    if (!interactor)
                        Debug.LogError($"[ClickInputFeeder] Interactor with tag '{interactorTag}' not found.", this);
                    yield break;
                }
                yield return null;
            }
        }

        void Update()
        {
            if (!thumbTip || !indexTip) return;

            float d = Vector3.Distance(thumbTip.position, indexTip.position);
            bool pressed = pressedPrev ? d <= pinchOffDistance : d <= pinchOnDistance;

            if (pressed && !pressedPrev && !isHolding) pendingHoldDown = true;
            if (!pressed && pressedPrev && isHolding) pendingHoldUp = true;

            pressedPrev = pressed;

            PinchExternalBus.uiIsHolding[(int)channel] = isHolding;
        }

        void LateUpdate()
        {
            if (!EventSystem.current || !interactor)
            {
                pendingHoldDown = pendingHoldUp = false;
                return;
            }

            bool hasHit = interactor.TryGetCurrentUIRaycastResult(out var curRay) &&
                          curRay.isValid && curRay.gameObject;

            if (pendingHoldDown)
            {
                if (hasHit) BeginHold(curRay);
#if UNITY_EDITOR
                else Debug.Log("[ClickInputFeeder] Pinch start but no UI under ray");
#endif
            }

            if (isHolding)
            {
                UpdateDrag(enableDrag, hasHit ? curRay : default);
            }

            if (pendingHoldUp)
            {
                EndHold(hasHit ? curRay : default);
            }

            pendingHoldDown = pendingHoldUp = false;
        }

        // ---------------- press / drag / release ----------------
        void BeginHold(RaycastResult hit)
        {
            // Prefer a down-handler; fallback to the hit object
            var target = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject) ?? hit.gameObject;

            var ped = NewPED(hit);
            ped.pointerEnter = target;
            ped.rawPointerPress = target;
            ped.pointerPress = target;
            ped.eligibleForClick = true;

            // pointerDown
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerDownHandler);

            // Let UI prep for drag (even if we don't end up dragging)
            var initTarget = ExecuteEvents.GetEventHandler<IInitializePotentialDragHandler>(target);
            if (initTarget)
                ExecuteEvents.ExecuteHierarchy(initTarget, ped, ExecuteEvents.initializePotentialDrag);

            isHolding = true;
            isDragging = false;
            pressedTarget = target;
            dragTarget = null;
            lastDropTarget = null;
            pressRaycast = hit;
            pressPos = hit.screenPosition;
            lastPos = pressPos;
            pressTime = Time.unscaledTime;
            overThreshFrames = 0;

#if UNITY_EDITOR
            Debug.Log($"[ClickInputFeeder] DOWN → {target.name}", target);
#endif
        }
        /*
        void UpdateDrag(bool allowDrag, RaycastResult cur)
        {
            // current pointer position
            Vector2 curPos = cur.isValid ? cur.screenPosition : lastPos;

            if (!isDragging && allowDrag)
            {
                int baseThresh = ComputeBaseDragThresholdPixels();
                float thresh = Mathf.Max(1f, baseThresh * Mathf.Max(0.01f, dragThresholdMultiplier));

                // click-first policy: require a tiny hold before we’re allowed to drag
                if (preferClickOverDrag && (Time.unscaledTime - pressTime) < minHoldBeforeDrag)
                {
                    lastPos = curPos;
                    return;
                }

                // accumulate “over threshold” frames to avoid spurious drags
                if (Vector2.Distance(curPos, pressPos) >= thresh)
                    overThreshFrames++;
                else
                    overThreshFrames = 0;

                if (overThreshFrames >= Mathf.Max(1, overThresholdFramesRequired))
                {
                    // Find a proper drag target.
                    // Try from the object under the press raycast first (works well for Slider),
                    // then from pressedTarget as a fallback.
                    var candidate =
                        ExecuteEvents.GetEventHandler<IBeginDragHandler>(pressRaycast.gameObject) ??
                        ExecuteEvents.GetEventHandler<IBeginDragHandler>(pressedTarget);

                    if (candidate != null)
                    {
                        dragTarget = candidate;

                        var ped = NewPED(pressRaycast);
                        ped.pointerPress = pressedTarget;
                        ped.rawPointerPress = pressedTarget;
                        ped.pointerDrag = dragTarget;
                        ped.dragging = true;
                        ped.pressPosition = pressPos;
                        ped.position = curPos;
                        ped.delta = curPos - lastPos;

                        ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.beginDragHandler);
                        isDragging = true;
#if UNITY_EDITOR
                        Debug.Log($"[ClickInputFeeder] BEGIN DRAG → {dragTarget.name}", dragTarget);
#endif
                    }
                    // else: no draggable in hierarchy → keep click pathway
                }
            }
            else if (isDragging)
            {
                var ped = NewPED(cur.isValid ? cur : pressRaycast);
                ped.pointerPress = pressedTarget;
                ped.rawPointerPress = pressedTarget;
                ped.pointerDrag = dragTarget;
                ped.dragging = true;
                ped.pressPosition = pressPos;
                ped.position = curPos;
                ped.delta = curPos - lastPos;

                ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.dragHandler);

                if (cur.isValid)
                    lastDropTarget = ExecuteEvents.GetEventHandler<IDropHandler>(cur.gameObject);
            }

            lastPos = curPos;
        }*/

        void UpdateDrag(bool allowDrag, RaycastResult cur)
        {
            // current pointer position
            Vector2 curPos = cur.isValid ? cur.screenPosition : lastPos;

            if (!isDragging && allowDrag)
            {
                int baseThresh = ComputeBaseDragThresholdPixels();
                float thresh = Mathf.Max(1f, baseThresh * Mathf.Max(0.01f, dragThresholdMultiplier));

                // click-first policy: require a tiny hold before we’re allowed to drag
                if (preferClickOverDrag && (Time.unscaledTime - pressTime) < minHoldBeforeDrag)
                {
                    lastPos = curPos;
                    return;
                }

                // accumulate “over threshold” frames to avoid spurious drags
                if (Vector2.Distance(curPos, pressPos) >= thresh)
                    overThreshFrames++;
                else
                    overThreshFrames = 0;

                if (overThreshFrames >= Mathf.Max(1, overThresholdFramesRequired))
                {
                    // Find a proper drag target.
                    // Try from the object under the press raycast first (works well for ScrollRect/Scrollbar),
                    // then from pressedTarget as a fallback.
                    //
                    // CHANGED: Prefer IBeginDragHandler if present, otherwise fall back to IDragHandler (Slider case).
                    var candidate =
                        ExecuteEvents.GetEventHandler<IBeginDragHandler>(pressRaycast.gameObject) ??
                        ExecuteEvents.GetEventHandler<IBeginDragHandler>(pressedTarget) ??
                        ExecuteEvents.GetEventHandler<IDragHandler>(pressRaycast.gameObject) ??   // <-- added
                        ExecuteEvents.GetEventHandler<IDragHandler>(pressedTarget);              // <-- added

                    if (candidate != null)
                    {
                        dragTarget = candidate;

                        var ped = NewPED(pressRaycast);
                        ped.pointerPress = pressedTarget;
                        ped.rawPointerPress = pressedTarget;
                        ped.pointerDrag = dragTarget;
                        ped.dragging = true;
                        ped.pressPosition = pressPos;
                        ped.position = curPos;
                        ped.delta = curPos - lastPos;

                        // Safe: if target doesn’t implement IBeginDragHandler (e.g., Slider), this is a no-op.
                        ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.beginDragHandler);

                        isDragging = true;
#if UNITY_EDITOR
                Debug.Log($"[ClickInputFeeder] BEGIN DRAG → {dragTarget.name}", dragTarget);
#endif
                    }
                    // else: no draggable in hierarchy → keep click pathway
                }
            }
            else if (isDragging)
            {
                var ped = NewPED(cur.isValid ? cur : pressRaycast);
                ped.pointerPress = pressedTarget;
                ped.rawPointerPress = pressedTarget;
                ped.pointerDrag = dragTarget;
                ped.dragging = true;
                ped.pressPosition = pressPos;
                ped.position = curPos;
                ped.delta = curPos - lastPos;

                ExecuteEvents.Execute(dragTarget, ped, ExecuteEvents.dragHandler);

                if (cur.isValid)
                    lastDropTarget = ExecuteEvents.GetEventHandler<IDropHandler>(cur.gameObject);
            }

            lastPos = curPos;
        }

        void EndHold(RaycastResult cur)
        {
            var upPed = NewPED(cur.isValid ? cur : pressRaycast);
            upPed.pointerEnter = pressedTarget;
            upPed.rawPointerPress = pressedTarget;
            upPed.pointerPress = pressedTarget;

            if (isDragging)
            {
                if (lastDropTarget)
                {
                    var dropPed = NewPED(cur.isValid ? cur : pressRaycast);
                    dropPed.pointerPress = pressedTarget;
                    dropPed.rawPointerPress = pressedTarget;
                    dropPed.pointerDrag = dragTarget;
                    dropPed.dragging = true;
                    dropPed.pressPosition = pressPos;
                    dropPed.position = upPed.position;

                    ExecuteEvents.Execute(lastDropTarget, dropPed, ExecuteEvents.dropHandler);
                }

                var endPed = NewPED(cur.isValid ? cur : pressRaycast);
                endPed.pointerPress = pressedTarget;
                endPed.rawPointerPress = pressedTarget;
                endPed.pointerDrag = dragTarget;
                endPed.dragging = false;
                endPed.pressPosition = pressPos;
                endPed.position = upPed.position;

                ExecuteEvents.Execute(dragTarget, endPed, ExecuteEvents.endDragHandler);
#if UNITY_EDITOR
                Debug.Log($"[ClickInputFeeder] END DRAG → {dragTarget?.name ?? "null"}");
#endif
            }

            // Always send pointerUp
            ExecuteEvents.ExecuteHierarchy(pressedTarget, upPed, ExecuteEvents.pointerUpHandler);

            // Click only when we never entered drag
            if (!isDragging)
            {
                bool releasedOverSame = false;
                if (cur.isValid)
                {
                    var releaseHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(cur.gameObject);
                    releasedOverSame = (releaseHandler != null && releaseHandler == pressedTarget);
                }
                upPed.eligibleForClick = !requireReleaseOverSame || releasedOverSame;

                if (upPed.eligibleForClick)
                {
                    ExecuteEvents.ExecuteHierarchy(pressedTarget, upPed, ExecuteEvents.pointerClickHandler);
#if UNITY_EDITOR
                    Debug.Log($"[ClickInputFeeder] CLICK → {pressedTarget.name}", pressedTarget);
#endif
                }
            }

            isHolding = false;
            isDragging = false;
            pressedTarget = null;
            dragTarget = null;
            lastDropTarget = null;
            overThreshFrames = 0;

            PinchExternalBus.uiIsHolding[(int)channel] = false;
        }

        void ForceEndHold()
        {
            isHolding = false;
            isDragging = false;
            pressedTarget = null;
            dragTarget = null;
            lastDropTarget = null;
            overThreshFrames = 0;
            PinchExternalBus.uiIsHolding[(int)channel] = false;
        }

        static PointerEventData NewPED(RaycastResult hit)
        {
            var es = EventSystem.current;
            var ped = new PointerEventData(es)
            {
                pointerId = -1, // mouse-left semantics
                button = PointerEventData.InputButton.Left,
                useDragThreshold = true,
                clickCount = 1,
                position = hit.screenPosition,
                pressPosition = hit.screenPosition,
                pointerCurrentRaycast = hit,
                pointerPressRaycast = hit
            };
            return ped;
        }

        int ComputeBaseDragThresholdPixels()
        {
            // Prefer XRUIInputModule's tracked-device threshold when available
            int fallback = EventSystem.current ? EventSystem.current.pixelDragThreshold : 10;
            var mod = EventSystem.current ? EventSystem.current.currentInputModule : null;
            if (mod == null) return fallback;

            // Reflection keeps this compile-safe across XRI versions
            var t = mod.GetType();
            var prop = t.GetProperty("trackedDeviceDragThreshold", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(int))
            {
                try { return (int)prop.GetValue(mod); } catch { return fallback; }
            }
            var field = t.GetField("m_TrackedDeviceDragThreshold", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(int))
            {
                try { return (int)field.GetValue(mod); } catch { return fallback; }
            }
            return fallback;
        }
    }
}