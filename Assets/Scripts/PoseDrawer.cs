using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.PoseLandmarker;

/// <summary>
/// Draws pose skeleton, joint dots, landmark labels and FPS onto a Unity UI Canvas.
/// Equivalent to draw_pose(), draw_labels(), and draw_fps() in the Python script.
/// 
/// Requires a Canvas in Screen Space – Overlay (or Camera) mode.
/// The poseOverlay RectTransform should cover the camera RawImage exactly.
/// </summary>
public class PoseDrawer
{
    // ── Skeleton (MediaPipe 33-landmark connections) ──────────────────────────
    private static readonly (int, int)[] PoseConnections =
    {
        (0,1),(1,2),(2,3),(3,7),          // left eye / ear
        (0,4),(4,5),(5,6),(6,8),          // right eye / ear
        (9,10),                            // mouth
        (11,12),                           // shoulders
        (11,13),(13,15),                   // left arm
        (12,14),(14,16),                   // right arm
        (15,17),(15,19),(15,21),           // left hand
        (16,18),(16,20),(16,22),           // right hand
        (17,19),(18,20),
        (11,23),(12,24),(23,24),           // torso
        (23,25),(25,27),(27,29),(27,31),   // left leg
        (24,26),(26,28),(28,30),(28,32),   // right leg
        (29,31),(30,32),
    };

    private static readonly Dictionary<int, string> LandmarkNames = new()
    {
        {0,  "Nose"},
        {11, "L Shoulder"}, {12, "R Shoulder"},
        {13, "L Elbow"},    {14, "R Elbow"},
        {15, "L Wrist"},    {16, "R Wrist"},
        {23, "L Hip"},      {24, "R Hip"},
        {25, "L Knee"},     {26, "R Knee"},
        {27, "L Ankle"},    {28, "R Ankle"},
    };

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color JointColor   = new Color(0f,    1f,    0.5f);   // #00FF80
    private static readonly Color BoneColor    = new Color(1f,    0.78f, 0f);     // #FFC800
    private static readonly Color LowVisColor  = new Color(0.31f, 0.31f, 0.31f); // #505050
    private static readonly Color LabelColor   = Color.yellow;
    private static readonly Color FpsColor     = Color.green;

    private const float VisibilityThreshold = 0.5f;

    // ── Pooled UI objects ─────────────────────────────────────────────────────
    private readonly RectTransform _overlay;
    private readonly List<RectTransform> _boneLines  = new();
    private readonly List<RectTransform> _jointDots  = new();
    private readonly List<TextMeshProUGUI> _labels   = new();
    private TextMeshProUGUI _fpsText;

    private bool _showLabels;

    // ── Construction ──────────────────────────────────────────────────────────
    public PoseDrawer(RectTransform overlay, bool showLabels)
    {
        _overlay     = overlay;
        _showLabels  = showLabels;
        CreateFpsLabel();
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public void Draw(PoseLandmarkerResult result, int srcW, int srcH, float fps, bool showLabels)
    {
        _showLabels = showLabels;

        // Hide everything first; we'll re-activate what we need
        SetPoolActive(_boneLines, false);
        SetPoolActive(_jointDots, false);
        SetPoolActive(_labels,    false);

        if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
        {
            UpdateFps(fps);
            return;
        }

        // Use the overlay rect as the drawing canvas
        float canvasW = _overlay.rect.width;
        float canvasH = _overlay.rect.height;

        int boneIdx  = 0;
        int dotIdx   = 0;
        int labelIdx = 0;

        foreach (var landmarks in result.poseLandmarks)
        {
            // ── Bones ─────────────────────────────────────────────────────────
            foreach (var (a, b) in PoseConnections)
            {
                if (a >= landmarks.landmarks.Count || b >= landmarks.landmarks.Count)
                    continue;

                var lmA = landmarks.landmarks[a];
                var lmB = landmarks.landmarks[b];
                float vis = Mathf.Min(lmA.visibility ?? 1f, lmB.visibility ?? 1f);
                Color color = vis >= VisibilityThreshold ? BoneColor : LowVisColor;

                Vector2 ptA = NormToCanvas(lmA.x, lmA.y, canvasW, canvasH);
                Vector2 ptB = NormToCanvas(lmB.x, lmB.y, canvasW, canvasH);

                var line = GetOrCreate(_boneLines, boneIdx, CreateBoneLine);
                SetLine(line, ptA, ptB, 2f, color);
                line.gameObject.SetActive(true);
                boneIdx++;
            }

            // ── Joints ────────────────────────────────────────────────────────
            foreach (var lm in landmarks.landmarks)
            {
                float vis   = lm.visibility ?? 1f;
                Color color = vis >= VisibilityThreshold ? JointColor : LowVisColor;
                Vector2 pt  = NormToCanvas(lm.x, lm.y, canvasW, canvasH);

                var dot = GetOrCreate(_jointDots, dotIdx, CreateJointDot);
                dot.anchoredPosition = pt;
                dot.GetComponent<Image>().color = color;
                dot.gameObject.SetActive(true);
                dotIdx++;
            }

            // ── Labels ────────────────────────────────────────────────────────
            if (_showLabels)
            {
                foreach (var (idx, name) in LandmarkNames)
                {
                    if (idx >= landmarks.landmarks.Count) continue;
                    var lm = landmarks.landmarks[idx];
                    if ((lm.visibility ?? 1f) < VisibilityThreshold) continue;

                    Vector2 pt = NormToCanvas(lm.x, lm.y, canvasW, canvasH);
                    var lbl = GetOrCreate(_labels, labelIdx, CreateLabel);
                    lbl.text = name;
                    lbl.rectTransform.anchoredPosition = pt + new Vector2(8f, 4f);
                    lbl.gameObject.SetActive(true);
                    labelIdx++;
                }
            }
        }

        UpdateFps(fps);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Converts normalised [0,1] coords (origin top-left) → anchored UI position
    private static Vector2 NormToCanvas(float nx, float ny, float w, float h)
        => new(nx * w, (1f - ny) * h); // flip Y because UI origin is bottom-left

    private static T GetOrCreate<T>(List<T> pool, int idx, System.Func<T> factory)
        where T : Component
    {
        while (pool.Count <= idx)
            pool.Add(factory());
        return pool[idx];
    }

    private static void SetPoolActive<T>(List<T> pool, bool active) where T : Component
    {
        foreach (var item in pool)
            item.gameObject.SetActive(active);
    }

    // ── Factory methods ───────────────────────────────────────────────────────
    private RectTransform CreateBoneLine()
    {
        var go  = new GameObject("Bone", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_overlay, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.sizeDelta = new Vector2(2f, 2f);
        go.SetActive(false);
        return rt;
    }

    private RectTransform CreateJointDot()
    {
        var go  = new GameObject("Joint", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_overlay, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.sizeDelta = new Vector2(8f, 8f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        go.SetActive(false);
        return rt;
    }

    private TextMeshProUGUI CreateLabel()
    {
        var go  = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(_overlay, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize  = 12f;
        tmp.color     = LabelColor;
        var rt = tmp.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(120f, 20f);
        go.SetActive(false);
        return tmp;
    }

    private void CreateFpsLabel()
    {
        var go  = new GameObject("FPS", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(_overlay, false);
        _fpsText = go.GetComponent<TextMeshProUGUI>();
        _fpsText.fontSize  = 22f;
        _fpsText.color     = FpsColor;
        var rt = _fpsText.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = Vector2.zero;
        rt.anchoredPosition = new Vector2(10f, _overlay.rect.height - 30f);
        rt.sizeDelta = new Vector2(160f, 30f);
    }

    private void UpdateFps(float fps)
    {
        if (_fpsText != null)
            _fpsText.text = $"FPS: {fps:F1}";
    }

    // ── Line rendering via RectTransform rotation ─────────────────────────────
    private static void SetLine(RectTransform rt, Vector2 from, Vector2 to, float thickness, Color color)
    {
        Vector2 dir    = to - from;
        float   length = dir.magnitude;
        float   angle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        rt.anchoredPosition = from + dir * 0.5f;
        rt.sizeDelta        = new Vector2(length, thickness);
        rt.localRotation    = Quaternion.Euler(0f, 0f, angle);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.GetComponent<Image>().color = color;
    }
}