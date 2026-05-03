using UnityEngine;

public class SettingsEvents : MonoBehaviour {
    public GameObject settingsPanel;
    
    private GestureManager gestureManager;
    
    public void OnEnable() {
        gestureManager = FindObjectsByType<GestureManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0];
        gestureManager.swipe.AddListener(OnSwipe);
        gestureManager.pinch.AddListener(OnPinch);
    }
    
    public void OnDisable() {
        gestureManager.swipe.RemoveListener(OnSwipe);
        gestureManager.pinch.RemoveListener(OnPinch);
    }
    
    public void OnSwipe(GestureManager.SwipeDirection swipeDirection) {
        if(settingsPanel.activeInHierarchy)
            settingsPanel.SetActive(false);
        else if(swipeDirection == GestureManager.SwipeDirection.Right) 
            FindObjectsByType<SceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0].reset();
    }

    public void OnPinch(float delta) {
        if (delta > 300) return;
        
        settingsPanel.SetActive(true);
    }
}
