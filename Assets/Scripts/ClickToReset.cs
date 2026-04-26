using UnityEngine;
using UnityEngine.InputSystem;

public class ClickToReset : MonoBehaviour {
    public InputActionReference clickAction;
    public Rigidbody rigidbody;
    public Vector3 reset_point; 
    
    private void OnEnable() {
        reset_point = transform.localPosition;
        clickAction.action.Enable();
    }

    private void OnDisable() {
        clickAction.action.Disable();
    }

    private void Update() {
        if (!clickAction.action.triggered) return;
        
        Debug.Log("Clicked!");
        transform.localPosition = reset_point;
        rigidbody.linearVelocity = Vector3.zero;
    }
}
