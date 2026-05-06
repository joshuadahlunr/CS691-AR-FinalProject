// Assets/Scripts/XRHandsJointCloud.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
public class XRHandsJointCloud : MonoBehaviour
{
    [Header("Debug spheres")]
    [SerializeField] float sphereRadius = 0.01f;
    [SerializeField] bool logSelection = true;

    XRHandSubsystem _subsystem;

    readonly Dictionary<XRHandJointID, Transform> _left = new();
    readonly Dictionary<XRHandJointID, Transform> _right = new();

    static readonly List<XRHandSubsystem> s_Subsystems = new();

    void Awake()
    {
        CreateCloud(_left, Color.green, "LeftCloud");
        CreateCloud(_right, Color.magenta, "RightCloud");
    }

    void OnEnable()
    {
        EnsureSubsystem();
        if (_subsystem != null)
            _subsystem.updatedHands += OnUpdatedHands;
    }

    void OnDisable()
    {
        if (_subsystem != null)
            _subsystem.updatedHands -= OnUpdatedHands;
    }

    void EnsureSubsystem()
    {
        if (_subsystem != null && _subsystem.running)
            return;

        s_Subsystems.Clear();
        SubsystemManager.GetSubsystems(s_Subsystems);

        _subsystem = null;
        for (int i = 0; i < s_Subsystems.Count; ++i)
        {
            if (s_Subsystems[i].running)
            {
                _subsystem = s_Subsystems[i];
                break;
            }
        }

        if (logSelection)
        {
            var name = (_subsystem != null) ? _subsystem.GetType().Name : "none";
            var running = (_subsystem != null) && _subsystem.running;
            Debug.Log($"[JointCloud] Using XRHandSubsystem: {name}, running={running}");
        }
    }

    void OnUpdatedHands(XRHandSubsystem ss, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType type)
    {
        if ((flags & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) != 0) UpdateCloud(ss.leftHand, _left);
        if ((flags & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) != 0) UpdateCloud(ss.rightHand, _right);
    }

    void LateUpdate()
    {
        // Also poll every frame so you see spheres even if no event fired yet.
        EnsureSubsystem();
        if (_subsystem == null) return;

        UpdateCloud(_subsystem.leftHand, _left);
        UpdateCloud(_subsystem.rightHand, _right);
    }

    // ----- helpers -----
    void CreateCloud(Dictionary<XRHandJointID, Transform> map, Color color, string rootName)
    {
        var root = new GameObject(rootName).transform;
        root.SetParent(transform, false);

        for (int i = XRHandJointID.BeginMarker.ToIndex() + 1; i < XRHandJointID.EndMarker.ToIndex(); ++i)
        {
            var id = XRHandJointIDUtility.FromIndex(i);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = id.ToString();
            sphere.transform.SetParent(root, false);
            sphere.transform.localScale = Vector3.one * (sphereRadius * 2f);
            Destroy(sphere.GetComponent<Collider>());

            // Try built-in Standard first, then URP Lit
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { color = color };
            sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;

            map[id] = sphere.transform;
            sphere.SetActive(false); // will enable when we get a pose
        }
    }

    void UpdateCloud(XRHand hand, Dictionary<XRHandJointID, Transform> map)
    {
        foreach (var kvp in map)
        {
            var joint = hand.GetJoint(kvp.Key);
            if (joint.TryGetPose(out var pose))
            {
                kvp.Value.position = pose.position;
                kvp.Value.rotation = pose.rotation;
                if (!kvp.Value.gameObject.activeSelf)
                    kvp.Value.gameObject.SetActive(true);
            }
            else if (kvp.Value.gameObject.activeSelf)
            {
                kvp.Value.gameObject.SetActive(false);
            }
        }
    }
}