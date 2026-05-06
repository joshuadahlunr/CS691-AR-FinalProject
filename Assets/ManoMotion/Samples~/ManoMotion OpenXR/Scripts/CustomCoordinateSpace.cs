using UnityEngine;
/// <summary>
/// Part of the Open XR Unity XR Interaction Toolkit Integration developed for ManoMotion by Joshua Holzfurtner.
/// </summary>
/// <summary>
/// Scene-level custom coordinate system with static helpers.
/// - Define a stable frame by Origin, BackRef (behind origin), and optional UpRef.
/// - Provides PositionCustomCoordinateSystem(...) and RotationCustomCoordinateSystem(...)
///   that are currently pass-through (world -> stable -> world).
/// - Later you can add clamps/offsets INSIDE these helpers to affect all callers.
/// </summary>
[DefaultExecutionOrder(-500)]
public class CustomCoordinateSpace : MonoBehaviour
{
    [Header("Stable Space References")]
    [Tooltip("Space origin (0,0,0).")]
    public Transform origin;

    [Tooltip("Reference point BEHIND origin. Forward = -(BackRef - Origin).")]
    public Transform backRef;

    [Tooltip("Optional up reference; if null, Vector3.up is used.")]
    public Transform upRef;

    public static CustomCoordinateSpace Instance { get; private set; }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    // ---------------- PUBLIC STATIC API ----------------

    /// <summary>
    /// Returns the same world position today (pass-through).
    /// Internally: world -> stable -> world. Later you can inject stable-space offsets here.
    /// </summary>
    public static Vector3 PositionCustomCoordinateSystem(Vector3 worldPos)
    {
        if (!Ready) return worldPos;
        // world -> stable -> world (identity for now, but centralizes the mapping)
        var pStable = WorldToStablePos(worldPos);
        return StableToWorldPos(pStable);
    }

    /// <summary>
    /// Map a world direction via the custom space and back (pass-through for now).
    /// </summary>
    public static Vector3 RotationCustomCoordinateSystem(Vector3 worldDir)
    {
        if (!Ready) return worldDir;
        var dStable = WorldToStableDir(worldDir);
        return StableToWorldDir(dStable);
    }

    /// <summary>
    /// Convenience for applying offsets IN stable space to a pose.
    /// (Not used yet, but ready for next step.)
    /// </summary>
    public static void ApplyStableOffsets(ref Vector3 worldPos, ref Quaternion worldRot,
                                          Vector3 posOffsetStable, Vector3 eulerOffsetStable)
    {
        if (!Ready) return;
        var pS = WorldToStablePos(worldPos) + posOffsetStable;
        var rS = WorldToStableRot(worldRot) * Quaternion.Euler(eulerOffsetStable);
        worldPos = StableToWorldPos(pS);
        worldRot = StableToWorldRot(rS);
    }

    /// <summary>True when references are valid.</summary>
    public static bool Ready =>
        Instance && Instance.origin && Instance.backRef;

    // ---------------- INTERNAL BASIS MATH ----------------

    public static void BuildBasis(out Vector3 o, out Vector3 R, out Vector3 U, out Vector3 F)
    {
        var inst = Instance;
        o = inst.origin.position;

        Vector3 dirBack = inst.backRef.position - o;
        if (dirBack.sqrMagnitude < 1e-10f) dirBack = -Vector3.forward;

        Vector3 fwdRaw = -dirBack.normalized;               // bow
        U = inst.upRef ? inst.upRef.up : Vector3.up;
        if (U.sqrMagnitude < 1e-10f) U = Vector3.up; U.Normalize();

        R = Vector3.Cross(U, fwdRaw);
        if (R.sqrMagnitude < 1e-10f) R = Vector3.right;
        R.Normalize();

        F = Vector3.Cross(R, U);
        if (F.sqrMagnitude < 1e-10f) F = fwdRaw;
        F.Normalize();
    }

    public static Quaternion BasisRotation()
    {
        BuildBasis(out _, out _, out var U, out var F);
        return Quaternion.LookRotation(F, U);
    }

    // ---- conversions: world <-> stable ----
    public static Vector3 WorldToStablePos(Vector3 worldPos)
    {
        BuildBasis(out var o, out var R, out var U, out var F);
        var rel = worldPos - o;
        return new Vector3(Vector3.Dot(rel, R), Vector3.Dot(rel, U), Vector3.Dot(rel, F));
    }

    public static Vector3 StableToWorldPos(Vector3 stablePos)
    {
        BuildBasis(out var o, out var R, out var U, out var F);
        return o + R * stablePos.x + U * stablePos.y + F * stablePos.z;
    }

    public static Vector3 WorldToStableDir(Vector3 worldDir)
    {
        BuildBasis(out _, out var R, out var U, out var F);
        return new Vector3(Vector3.Dot(worldDir, R), Vector3.Dot(worldDir, U), Vector3.Dot(worldDir, F));
    }

    public static Vector3 StableToWorldDir(Vector3 stableDir)
    {
        BuildBasis(out _, out var R, out var U, out var F);
        return R * stableDir.x + U * stableDir.y + F * stableDir.z;
    }

    public static Quaternion WorldToStableRot(Quaternion worldRot)
    {
        var basis = BasisRotation();
        return Quaternion.Inverse(basis) * worldRot;
    }

    public static Quaternion StableToWorldRot(Quaternion stableRot)
    {
        var basis = BasisRotation();
        return basis * stableRot;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!origin) return;
        BuildBasis(out var o, out var R, out var U, out var F);
        float L = 0.25f;
        Gizmos.color = Color.red;   Gizmos.DrawLine(o, o + R * L);
        Gizmos.color = Color.green; Gizmos.DrawLine(o, o + U * L);
        Gizmos.color = Color.blue;  Gizmos.DrawLine(o, o + F * L);
    }
#endif
}