// Assets/Input/HandPinchRegistrar.cs
using UnityEngine.InputSystem;
using ManoMotion.OpenXR;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    public static class HandPinchRegistrar
    {
        static bool s_Done;
        const string LayoutName = "HandPinchDevice";

        public static void EnsureRegistered()
        {
            if (s_Done) return;

            bool exists = false;
            foreach (var name in InputSystem.ListLayouts()) // IEnumerable<string>
            {
                if (name == LayoutName) { exists = true; break; }
            }

            if (!exists)
            {
                // No matcher needed—we instantiate the device ourselves in the feeder.
                InputSystem.RegisterLayout<HandPinchDevice>(name: LayoutName);
            }

            s_Done = true;
        }
    }
}