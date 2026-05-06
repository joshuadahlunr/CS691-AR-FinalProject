// Assets/Scripts/HandSubsystemSpy.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    public class HandSubsystemSpy : MonoBehaviour
    {
        void Start()
        {
            var list = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(list);
            foreach (var ss in list)
                Debug.Log($"[Spy] XRHandSubsystem found: {ss.GetType().Name}, running={ss.running}");
        }
    }
}