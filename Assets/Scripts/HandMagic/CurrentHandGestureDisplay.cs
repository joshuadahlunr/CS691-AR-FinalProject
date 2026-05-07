using System;
using ManoMotion;
using TMPro;
using UnityEngine;

public class CurrentHandGestureDisplay : MonoBehaviour {
    public int hand = 1;
    public TMPro.TextMeshProUGUI handText;

    public void Awake() {
        handText = GetComponent<TextMeshProUGUI>();
    }

    public void Update() {
        bool foundHand = ManoMotionManager.Instance.TryGetHandInfo(LeftOrRightHand.LEFT_HAND, out HandInfo handInfo, out int handIndex);
        if (!foundHand) foundHand = ManoMotionManager.Instance.TryGetHandInfo(LeftOrRightHand.RIGHT_HAND, out handInfo, out handIndex);
        if (!foundHand) return;
        GestureInfo gestureInfo = handInfo.gestureInfo;

        handText.text = gestureInfo.manoGestureContinuous.ToString().Replace("_GESTURE", "").Replace("_", " ");
        handText.color = gestureInfo.manoGestureContinuous == ManoGestureContinuous.NO_GESTURE ? new Color(1, 0, 0, 0) : Color.white;
    }
}
