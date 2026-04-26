using System;
using UnityEngine;
using UnityEngine.Serialization;

public class MainMenuEvents : MonoBehaviour
{

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

    public void OnCoversClicked() {
        Debug.Log("Covers");
    }
    
    public void OnPokeballClicked() {
        Debug.Log("Pokeball");
    }
    
    public void OnZenClicked() {
        Debug.Log("Zen");
    }
    
    public void OnMikuClicked() {
        Debug.Log("Miku");
    }
    
    public void OnRezeClicked() {
        Debug.Log("Reze");
    }
    
    public void OnCreditsClicked() {
        Debug.Log("Credits");
    }
    
    public void OnWizardClicked() {
        Debug.Log("Wizard");
    }

    public void OnSwipe(GestureManager.SwipeDirection swipeDirection) {
        settingsPanel.SetActive(false);
    }

    public void OnPinch(float delta) {
        if (delta > 300) return;
        
        settingsPanel.SetActive(true);
    }
}
