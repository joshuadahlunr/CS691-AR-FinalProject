using System;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils.Bindings.Variables;
using UnityEngine;

public class PoseDebugPrint : MonoBehaviour
{
    public PoseLandmarkerManager poseLandmarkerManager;
    public Transform parentTransform;
    
    private static readonly Dictionary<int, string> LandmarkNames = new() {
        {0,  "Nose"},
        {11, "L Shoulder"}, {12, "R Shoulder"},
        {13, "L Elbow"},    {14, "R Elbow"},
        {15, "L Wrist"},    {16, "R Wrist"},
        {23, "L Hip"},      {24, "R Hip"},
        {25, "L Knee"},     {26, "R Knee"},
        {27, "L Ankle"},    {28, "R Ankle"},
    };

    private Transform[] transforms = {};
    
    public void Update() {
        if (poseLandmarkerManager.latestResult.poseWorldLandmarks is null || poseLandmarkerManager.latestResult.poseWorldLandmarks.Count < 1) return;
        var points = new Dictionary<string, Vector3>(); 
        //foreach(var landmarks in poseLandmarkerManager.latestResult.poseWorldLandmarks)
        var landmarks = poseLandmarkerManager.latestResult.poseLandmarks[0]; // Assume there is only one landmark
        //landmarks.landmarks[0].
        foreach (var (idx, name) in LandmarkNames)
            points[name] = new Vector3(landmarks.landmarks[idx].x, landmarks.landmarks[idx].y, landmarks.landmarks[idx].z);

        if (transforms.Length != points.Count) {
            transforms = new Transform[LandmarkNames.Count];
            for(var i = 0; i < LandmarkNames.Count; i++) {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.transform.SetParent(parentTransform);
                ball.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
                transforms[i] = ball.transform;
            }
        }

        for (var i = 0; i < points.Count; i++)
            if (points.ContainsKey(LandmarkNames.Values.ElementAt(i))) {
                var rawPoint =  points[LandmarkNames.Values.ElementAt(i)];
                var point = Camera.main.ScreenToWorldPoint(rawPoint);
                Debug.Log($"{rawPoint} -> {point}");
                transforms[i].transform.position = point;
            }
    }
}
