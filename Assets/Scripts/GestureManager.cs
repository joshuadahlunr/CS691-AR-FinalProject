using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class GestureManager : MonoBehaviour {
    private Vector3 fp;   //First touch position
    private Vector3 lp;   //Last touch position
    private float dragDistance;  //minimum distance for a swipe to be registered
    
    public enum SwipeDirection {
        Left,
        Right,
        Up,
        Down
    }

    public UnityEvent<float> pinch;
    public UnityEvent<SwipeDirection> swipe;
    
    public void OnEnable() {
        EnhancedTouchSupport.Enable();
        dragDistance = Screen.height * 15 / 100; //dragDistance is 15% height of the screen
    }

    public void OnDisable() {
        EnhancedTouchSupport.Disable();
    }
    
    public void Update() {
        //Debug.Log($"Fingers {Touch.activeFingers.Count} Touches {Touch.activeTouches.Count}");
        if (Touch.activeFingers.Count < 1) return;
        
        if (Touch.activeTouches.Count == 1) {
            //Debug.Log("Single Fingy");
            // user is touching the screen with a single touch
            var touch = Touch.activeTouches[0];
            Debug.Log(touch.phase);

            switch (touch.phase) {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    // Debug.Log("Begin");
                    fp = touch.screenPosition;
                    lp = touch.screenPosition;
                    break;
                case UnityEngine.InputSystem.TouchPhase.Moved:
                    // Debug.Log("Move");
                    lp = touch.screenPosition;
                    break;
                case UnityEngine.InputSystem.TouchPhase.Ended: {
                    // Debug.Log("End");
                    lp = touch.screenPosition;

                    if (Mathf.Abs(lp.x - fp.x) > dragDistance || Mathf.Abs(lp.y - fp.y) > dragDistance) {
                        if (Mathf.Abs(lp.x - fp.x) > Mathf.Abs(lp.y - fp.y))
                        {
                            Debug.Log(lp.x > fp.x ? "Right Swipe" : "Left Swipe");
                            swipe.Invoke(lp.x > fp.x ? SwipeDirection.Right : SwipeDirection.Left);
                        } else {
                            Debug.Log(lp.y > fp.y ? "Up Swipe" : "Down Swipe");
                            swipe.Invoke(lp.y > fp.y ? SwipeDirection.Up : SwipeDirection.Down);
                        }
                    }
                    //else
                    //{
                    //    Debug.Log("Tap");
                    //}

                    break;
                }
            }

            return;
        }
        
        var touch1 = Touch.activeFingers[0];
        var touch2 = Touch.activeFingers[1];
            
        var currentDistance = Vector2.Distance(touch1.screenPosition, touch2.screenPosition);
        var previousDistance = Vector2.Distance(
            touch1.screenPosition - touch1.lastTouch.screenPosition, 
            touch2.screenPosition - touch2.lastTouch.screenPosition
        );
            
        var delta = currentDistance - previousDistance;
        Debug.Log($"Pinch Delta {delta}");
        // Use delta for zooming or scaling logic
        pinch.Invoke(delta);
    }
}