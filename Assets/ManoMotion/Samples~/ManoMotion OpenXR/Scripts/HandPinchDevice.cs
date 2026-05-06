// Assets/Input/HandPinchDevice.cs
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using ManoMotion.OpenXR;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [InputControlLayout(displayName = "Hand Pinch Device")]
    public class HandPinchDevice : InputDevice
    {
        [InputControl(layout = "Button")] public ButtonControl press { get; private set; }
        [InputControl(layout = "Axis")] public AxisControl pinch { get; private set; }

        protected override void FinishSetup()
        {
            base.FinishSetup();
            press = GetChildControl<ButtonControl>("press");
            pinch = GetChildControl<AxisControl>("pinch");
        }
    }
}