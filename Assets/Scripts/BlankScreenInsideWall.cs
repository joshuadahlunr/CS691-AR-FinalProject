using System;
using UnityEngine;

public class BlankScreenInsideWall : MonoBehaviour
{
    public GameObject blankerObject;
    public float detectionRadius = .2f;

    private void Update() {
        Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, ~0, QueryTriggerInteraction.Collide);
        
        bool blank = false;
        foreach (var hit in hits) {
            if (hit.gameObject.CompareTag("Blanker"))
                blank = true;
        }
        
        blankerObject.SetActive(blank);
    }
}
