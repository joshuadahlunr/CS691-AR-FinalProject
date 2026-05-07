using System;
using ManoMotion;
using UnityEngine;

public class HandGestureTracker : MonoBehaviour {
    public ManoGestureContinuous lastValidGesture;

    public Action<int, int, int> gestureChanceEvent;
    public Ray indexRay;
    public LineRenderer handLine;
    int[] SettingToSkeletonJoint = { 0, 2, 5, 9, 13, 17 };

    public void Update() {
        bool foundHand = ManoMotionManager.Instance.TryGetHandInfo(LeftOrRightHand.LEFT_HAND, out HandInfo handInfo, out int handIndex);
        if (!foundHand) foundHand = ManoMotionManager.Instance.TryGetHandInfo(LeftOrRightHand.RIGHT_HAND, out handInfo, out handIndex);
        if (!foundHand) return;
        var gestureInfo = handInfo.gestureInfo;
        var trackingInfo = handInfo.trackingInfo;
        var fingerInfo = trackingInfo.fingerInfo;
        
        var fingerJoint = SettingToSkeletonJoint[/*ManoMotionManager.Instance.ManomotionSession.enabledFeatures.fingerInfo*/2];
        var joints = SkeletonManager.Instance.GetJoints(handIndex);
        Vector3 direction = (joints[fingerJoint + 2].transform.position - joints[fingerJoint].transform.position).normalized;
        Vector3 origin = joints[fingerJoint].transform.position;
        indexRay = new Ray(origin, direction);
        handLine.SetPosition(0, indexRay.origin);
        handLine.SetPosition(1, indexRay.origin + indexRay.direction * 100);

        if (gestureInfo.manoGestureContinuous == ManoGestureContinuous.NO_GESTURE ||
            gestureInfo.manoGestureContinuous == lastValidGesture) return;
        
        gestureChanceEvent.Invoke((int)lastValidGesture, (int)gestureInfo.manoGestureContinuous, (int)gestureInfo.handSide);
        lastValidGesture = gestureInfo.manoGestureContinuous;
    }
}
