using System;
using System.Collections;
using System.Collections.Generic;
using ManoMotion;
using UnityEngine;

public class HandMagic : MonoBehaviour {
    public HandGestureTracker tracker;
    public HandSide currentSide;

    public GameObject dustLoop;
    public GameObject sparksPrefab;

    private static IEnumerator RemoveSparks(GameObject spark) {
        yield return new WaitForSeconds(1);
        Destroy(spark);
        yield return null;
    }

    public void OnEnable() {
        tracker.gestureChanceEvent += (last_, current_, side_) => {
            var last = (ManoGestureContinuous)last_;
            var current = (ManoGestureContinuous)current_;
            var side = (HandSide)side_;
            currentSide = side;
            
            //Debug.Log($"{last} -> {current}:  {side}");
            if (last != ManoGestureContinuous.OPEN_PINCH_GESTURE
                || (current is not (ManoGestureContinuous.POINTER_GESTURE or ManoGestureContinuous.CLOSED_HAND_GESTURE))
                || side != HandSide.Backside) return;
            Debug.Log("Bang!");
            
            if (!Physics.Raycast(tracker.indexRay, out RaycastHit hitInfo)) return;
            var sparks = Instantiate(sparksPrefab);
            sparks.transform.position = hitInfo.point;
            StartCoroutine(RemoveSparks(sparks));
        };
    }

    public void Update() {
        dustLoop.transform.position = new Vector3(0, -100, 0);
        
        if (tracker.lastValidGesture != ManoGestureContinuous.OPEN_HAND_GESTURE) return;
        if (currentSide != HandSide.Palmside) return;

        dustLoop.transform.position = tracker.indexRay.origin;
    }
}
