// Assets/Debug/ForceUIClickOnKey.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using ManoMotion.OpenXR;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DisallowMultipleComponent]
    public class ForceUIClickOnKey : MonoBehaviour
    {
        public NearFarInteractor interactor;

        [Header("Click once")]
        public Key clickKey = Key.Space;

        [Header("Hold to press")]
        public bool enableHold = true;
        public Key holdKey = Key.LeftShift;

        [Header("Toggle press")]
        public bool enableToggle = true;
        public Key toggleKey = Key.Y;

        bool isHolding;
        GameObject pressedTarget;
        Vector2 pressedScreenPos;
        RaycastResult pressedRaycast;

        void Awake()
        {
            if (!interactor) TryGetComponent(out interactor);
            if (!EventSystem.current)
                Debug.LogWarning("ForceUIClickOnKey: No EventSystem present.", this);
        }

        public HandChannel channel = HandChannel.Left; // set Right on right-hand

        void Update()
        {
            var kb = Keyboard.current;
            int ch = (int)channel;

            // External one-frame triggers (from pinch)
            bool extClick = PinchExternalBus.Consume(ref PinchExternalBus.uiClickOnce[ch]);
            bool extToggle = PinchExternalBus.Consume(ref PinchExternalBus.uiToggle[ch]);
            bool extDown = PinchExternalBus.Consume(ref PinchExternalBus.uiHoldDown[ch]);
            bool extUp = PinchExternalBus.Consume(ref PinchExternalBus.uiHoldUp[ch]);

            // Keyboard path (kept for editor)
            bool keyClick = kb != null && kb[clickKey] != null && kb[clickKey].wasPressedThisFrame;
            bool keyToggle = kb != null && enableToggle && kb[toggleKey] != null && kb[toggleKey].wasPressedThisFrame;
            bool keyDown = kb != null && enableHold && kb[holdKey] != null && kb[holdKey].wasPressedThisFrame;
            bool keyUp = kb != null && enableHold && kb[holdKey] != null && kb[holdKey].wasReleasedThisFrame;

            // Click once
            if (extClick || keyClick)
                DoClickOnce();

            // Toggle
            if (extToggle || keyToggle)
            {
                if (!isHolding) BeginHold();
                else EndHold();
            }

            // Hold edges
            if ((extDown || keyDown) && !isHolding) BeginHold();
            if ((extUp || keyUp) && isHolding) EndHold();

            // Publish this hand's holding state
            PinchExternalBus.uiIsHolding[ch] = isHolding;
        }

        void DoClickOnce()
        {
            if (!interactor.TryGetCurrentUIRaycastResult(out var hit) || !hit.isValid || !hit.gameObject)
            {
                Debug.Log("[ForceUIClickOnKey] No valid UI under ray.");
                return;
            }
            SendClickGesture(hit);
        }

        void BeginHold()
        {
            if (!interactor.TryGetCurrentUIRaycastResult(out var hit) || !hit.isValid || !hit.gameObject)
            {
                Debug.Log("[ForceUIClickOnKey] No valid UI to hold.");
                return;
            }

            var target = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject) ?? hit.gameObject;

            var ped = NewPED(hit);
            ped.pointerEnter = target;
            ped.rawPointerPress = target;
            ped.pointerPress = target;

            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerDownHandler);

            isHolding = true;
            pressedTarget = target;
            pressedScreenPos = hit.screenPosition;
            pressedRaycast = hit;
            Debug.Log($"[ForceUIClickOnKey] HOLD DOWN → {target.name}", target);
        }

        void EndHold()
        {
            if (!isHolding || !pressedTarget) { isHolding = false; pressedTarget = null; return; }

            var ped = NewPED(pressedRaycast);
            ped.pointerEnter = pressedTarget;
            ped.rawPointerPress = pressedTarget;
            ped.pointerPress = pressedTarget;
            ped.pressPosition = pressedScreenPos;

            ExecuteEvents.ExecuteHierarchy(pressedTarget, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(pressedTarget, ped, ExecuteEvents.pointerClickHandler);

            Debug.Log($"[ForceUIClickOnKey] HOLD UP + CLICK → {pressedTarget.name}", pressedTarget);

            isHolding = false;
            pressedTarget = null;
        }

        void SendClickGesture(RaycastResult hit)
        {
            var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject) ?? hit.gameObject;

            var ped = NewPED(hit);
            ped.pointerEnter = target;
            ped.rawPointerPress = target;
            ped.pointerPress = target;

            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerClickHandler);

            Debug.Log($"[ForceUIClickOnKey] CLICK → {target.name}", target);
        }

        static PointerEventData NewPED(RaycastResult hit)
        {
            var ped = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                position = hit.screenPosition,
                pressPosition = hit.screenPosition,
                pointerCurrentRaycast = hit,
                pointerPressRaycast = hit
            };
            return ped;
        }
    }
}