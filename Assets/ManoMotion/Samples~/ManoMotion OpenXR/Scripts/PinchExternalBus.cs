// Assets/Input/PinchExternalBus.cs
using System.Threading;

public enum HandChannel { Left = 0, Right = 1 }
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    public static class PinchExternalBus
    {
        // One slot per hand (0 = Left, 1 = Right)
        public static bool[] uiClickOnce = new bool[2];
        public static bool[] uiToggle = new bool[2];
        public static bool[] uiHoldDown = new bool[2];
        public static bool[] uiHoldUp = new bool[2];

        public static bool[] selectToggle = new bool[2];
        public static bool[] selectHoldDown = new bool[2];
        public static bool[] selectHoldUp = new bool[2];

        // UI holding state (so 3D side can yield) — per hand
        public static bool[] uiIsHolding = new bool[2];


        // PinchExternalBus.cs
        public static bool[] uiPressed = new bool[2];
        public static bool[] selectPressed = new bool[2];
        public static float[] pinchValue = new float[2]; // 0..1 (or distance-mapped)

        // Atomically consume a one-shot flag (works with array elements)
        public static bool Consume(ref bool flag)
        {
            if (!flag) return false;
            flag = false;
            Thread.MemoryBarrier();
            return true;
        }
    }
}