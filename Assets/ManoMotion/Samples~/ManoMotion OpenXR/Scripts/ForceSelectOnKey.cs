// Assets/Debug/ForceSelectOnKey.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using ManoMotion.OpenXR;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DisallowMultipleComponent]
    public class ForceSelectOnKey : MonoBehaviour
    {
        public NearFarInteractor interactor; // assign or we'll TryGetComponent

        [Header("Hold-to-select")]
        public bool enableHold = true;
        public Key holdKey = Key.Space;

        [Header("Toggle-to-select")]
        public bool enableToggle = true;
        public Key toggleKey = Key.Y;

        IXRSelectInteractable current;
        readonly List<IXRInteractable> cache = new();
        bool latched;

        void Awake()
        {
            if (!interactor) TryGetComponent(out interactor);
            if (!interactor || interactor.interactionManager == null)
                Debug.LogWarning("ForceSelectOnKey: Missing interactor or interactionManager.", this);
        }

        public HandChannel channel = HandChannel.Left; // set Right on right-hand

        void Update()
        {
            var kb = Keyboard.current;
            int ch = (int)channel;

            // If this hand's UI is holding, pause 3D for this hand
            bool uiBusy = PinchExternalBus.uiIsHolding[ch];

            // External triggers from pinch (per hand)
            bool extToggle = !uiBusy && PinchExternalBus.Consume(ref PinchExternalBus.selectToggle[ch]);
            bool extDown = !uiBusy && PinchExternalBus.Consume(ref PinchExternalBus.selectHoldDown[ch]);
            bool extUp = PinchExternalBus.Consume(ref PinchExternalBus.selectHoldUp[ch]); // allow release

            // Keyboard (editor)
            bool keyToggle = !uiBusy && enableToggle && kb != null && kb[toggleKey] != null && kb[toggleKey].wasPressedThisFrame;
            bool keyHeld = !uiBusy && enableHold && kb != null && kb[holdKey] != null && kb[holdKey].isPressed;
            bool keyDown = !uiBusy && enableHold && kb != null && kb[holdKey] != null && kb[holdKey].wasPressedThisFrame;
            bool keyUp = enableHold && kb != null && kb[holdKey] != null && kb[holdKey].wasReleasedThisFrame;

            // Toggle
            if (extToggle || keyToggle)
            {
                latched = !latched;
                if (latched) StartSelect();
                else EndSelect();
            }

            // Hold edges
            if ((extDown || keyDown) && !interactor.hasSelection && !latched) StartSelect();
            if ((extUp || keyUp) && interactor.hasSelection && !latched) EndSelect();

            // Continuous keyboard hold (unchanged)
            if (enableHold && !latched && !uiBusy && kb != null && kb[holdKey] != null)
            {
                bool held = keyHeld;
                if (held && !interactor.hasSelection) StartSelect();
                else if (!held && interactor.hasSelection) EndSelect();
            }
        }

        void StartSelect()
        {
            cache.Clear();
            interactor.GetValidTargets(cache);
            if (cache.Count > 0 && cache[0] is IXRSelectInteractable sel)
            {
                interactor.StartManualInteraction(sel);
                current = sel;
                Debug.Log($"[ForceSelectOnKey] StartManualInteraction → {sel.transform.name}");
            }
            else
            {
                Debug.Log("[ForceSelectOnKey] No valid 3D target under ray.");
            }
        }

        void EndSelect()
        {
            if (interactor.hasSelection)
            {
                interactor.EndManualInteraction();
                Debug.Log($"[ForceSelectOnKey] EndManualInteraction → {(current != null ? current.transform.name : "null")}");
                current = null;
            }
        }
    }
}