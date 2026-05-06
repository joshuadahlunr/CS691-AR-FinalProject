using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using Custom.XR;   // for CustomHandSubsystem

/// <summary>
/// Ensures exactly **one** running CustomHandSubsystem exists,
/// (optionally) stops any other XR hand providers,
/// applies layout/root/world-conversion config,
/// and logs what it did.
/// </summary>
using ManoMotion.OpenXR;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
namespace ManoMotion.OpenXR
{
    [DefaultExecutionOrder(-300)]
    public class CustomHandSubsystemBootstrap : MonoBehaviour
    {
        const string k_Id = "custom-hand-subsystem";

        [Header("Control")]
        [Tooltip("If enabled, stops any other XRHandSubsystems (e.g., Device Simulator/OpenXR) so only this provider runs.")]
        [SerializeField] bool stopOtherXRHandProviders = true;

        [Header("Layout Profile")]
        [SerializeField]
        CustomHandSubsystem.HandLayoutProfile profile =
            CustomHandSubsystem.HandLayoutProfile.Mano21_NoMetas;

        [Header("Root Options")]
        [SerializeField] bool configureRoot = true;
        [SerializeField] bool usePalmAsRoot = true;
        [SerializeField] Vector3 rootOffsetPosition = Vector3.zero; // meters
        [SerializeField] Vector3 rootOffsetEuler = Vector3.zero; // degrees

        [Header("WORLD → device-origin conversion (optional)")]
        [SerializeField] Transform deviceOriginRef;   // your "real device origin" anchor
        [SerializeField] Transform deviceBackRef;     // a point behind origin to define forward (+Z forward)
        [SerializeField] bool keepWorldUp = true;

        void Awake()
        {
            // 0. Optionally stop any other XR hand providers first.
            if (stopOtherXRHandProviders)
            {
                var subsToStop = new List<XRHandSubsystem>();
                SubsystemManager.GetSubsystems(subsToStop);
                foreach (var s in subsToStop)
                    if (s.running) s.Stop();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Bootstrap] Stopped {subsToStop.Count} XRHandSubsystem(s).");
#endif
            }

            // 1. If an instance already exists, just start it if needed.
            if (CustomHandSubsystem.Instance != null)
            {
                if (!CustomHandSubsystem.Instance.running)
                    CustomHandSubsystem.Instance.Start();

                ApplyConfig(CustomHandSubsystem.Instance);
                return;
            }

            // 2. Find an un-started instance Unity may have created.
            var subs = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var ss in subs)
            {
                if (ss is CustomHandSubsystem custom)
                {
                    if (!custom.running)
                        custom.Start();

                    ApplyConfig(custom);
                    return;
                }
            }

            // 3. No instance at all → create one from **our** descriptor only.
            var descs = new List<XRHandSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(descs);

            foreach (var d in descs)
            {
                if (d.id != k_Id) continue;   // skip simulator & others

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Bootstrap] Creating {d.id}");
#endif
                var ss = d.Create() as CustomHandSubsystem;
                if (ss == null)
                {
                    Debug.LogError("[Bootstrap] Descriptor.Create() did not return CustomHandSubsystem.");
                    return;
                }

                ss.Start();
                ApplyConfig(ss);
                return;
            }

            // 4. Still nothing? Descriptor failed to register.
            Debug.LogError(
                "[Bootstrap] CustomHandSubsystem descriptor not found. " +
                "Check that CustomHandSubsystem.cs compiled without errors.");
        }

        void ApplyConfig(CustomHandSubsystem ss)
        {
            if (ss == null) return;

            // Layout profile (default already Mano21_NoMetas; set anyway for clarity)
            ss.SetHandLayoutProfile(profile, restartSubsystem: false);

            // Root options
            if (configureRoot)
            {
                var offset = new Pose(rootOffsetPosition, Quaternion.Euler(rootOffsetEuler));
                ss.ConfigureRoot(usePalmAsRoot, offset);
            }

            // World→device-origin basis (optional)
            if (deviceOriginRef != null && deviceBackRef != null)
                ss.ConfigureWorldConversion(deviceOriginRef, deviceBackRef, keepWorldUp);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Bootstrap] Running={ss.running}, Profile={profile}, " +
                  $"Root={{usePalm:{usePalmAsRoot}, offsetP:{rootOffsetPosition}, offsetR:{rootOffsetEuler}}}, " +
                  $"WorldConversion={(deviceOriginRef && deviceBackRef ? "ON" : "OFF")}");
#endif
        }
    }
}