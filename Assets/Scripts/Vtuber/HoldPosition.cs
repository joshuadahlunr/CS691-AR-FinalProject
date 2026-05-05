using System;
using UnityEngine;

public class HoldPosition : MonoBehaviour {
    public Vector3 startPosition;

    // Update is called once per frame
    void Update() {
        transform.localPosition = startPosition;
    }
}
