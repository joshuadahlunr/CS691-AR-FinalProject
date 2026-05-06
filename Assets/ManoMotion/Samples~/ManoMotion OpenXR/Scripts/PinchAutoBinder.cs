// Assets/Input/PinchAutoBinder.cs
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DisallowMultipleComponent]
    public class PinchAutoBinder : MonoBehaviour
    {
        [Header("Interactor lookup")]
        public NearFarInteractor interactor;   // leave null: we find it
        public bool findInteractorByTag = true;
        public string interactorTag = "LeftNearFarInteractor"; // set per hand
        public float retryTimeoutSeconds = 5f;

        [Header("Hand")]
        public bool isRight = false; // set true on right hand

        InputAction selectBtn;
        InputAction selectVal;

        void OnEnable() => StartCoroutine(BindRoutine());

        IEnumerator BindRoutine()
        {
            // Wait until interactor exists (runtime spawn support)
            float t0 = Time.unscaledTime;
            while (!interactor)
            {
                if (findInteractorByTag && !string.IsNullOrEmpty(interactorTag))
                {
#if UNITY_2022_2_OR_NEWER
                    var all = Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                var all = Object.FindObjectsOfType<NearFarInteractor>(true);
#endif
                    foreach (var cand in all)
                        if (cand && cand.gameObject.CompareTag(interactorTag)) { interactor = cand; break; }
                }
                if (!interactor) TryGetComponent(out interactor);
                if (interactor || (Time.unscaledTime - t0) > retryTimeoutSeconds) break;
                yield return null;
            }
            if (!interactor) { Debug.LogError($"PinchAutoBinder: interactor with tag '{interactorTag}' not found.", this); yield break; }

            // Create actions and bind to our virtual device
            selectBtn = new InputAction($"PinchSelect_{(isRight ? "R" : "L")}", InputActionType.Button, "<HandPinchDevice>/press");
            selectVal = new InputAction($"PinchValue_{(isRight ? "R" : "L")}", InputActionType.Value, "<HandPinchDevice>/pinch", processors: "Clamp(min=0,max=1)");
            selectBtn.Enable(); selectVal.Enable();

            TryAssign(interactor, "selectInput", selectBtn);
            TryAssign(interactor, "selectValueInput", selectVal);

            Debug.Log($"PinchAutoBinder: bound Select/SelectValue to {interactor.name}", this);
        }

        void OnDisable()
        {
            selectBtn?.Disable();
            selectVal?.Disable();
        }

        static void TryAssign(NearFarInteractor it, string fieldName, InputAction action)
        {
            if (!it || action == null) return;

            var t = it.GetType();

            // Public property path (InputActionProperty)
            var prop = t.GetProperty(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (prop != null && prop.PropertyType == typeof(InputActionProperty))
            {
                prop.SetValue(it, new InputActionProperty(action));
                return;
            }

            // Private serialized field path (m_SelectInput / m_SelectValueInput)
            var fld = t.GetField($"m_{char.ToUpper(fieldName[0]) + fieldName.Substring(1)}",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fld != null && fld.FieldType == typeof(InputActionProperty))
            {
                fld.SetValue(it, new InputActionProperty(action));
                return;
            }

            Debug.LogWarning($"PinchAutoBinder: Could not assign {fieldName} on {it.name}. Bind manually if needed.");
        }
    }
}