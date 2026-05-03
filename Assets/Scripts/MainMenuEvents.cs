using System;
using UnityEngine;
using UnityEngine.Serialization;

public class MainMenuEvents : MonoBehaviour {
    public void OnCoversClicked() {
        Debug.Log("Covers");
        FindObjectsByType<SceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0].loadCoversScene();
    }
    
    public void OnPokeballClicked() {
        Debug.Log("Pokeball");
        FindObjectsByType<SceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0].loadPokeballScene();
    }
    
    public void OnZenClicked() {
        Debug.Log("Zen");
        FindObjectsByType<SceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0].loadZenScene();
    }
    
    public void OnMikuClicked() {
        Debug.Log("Miku");
        FindObjectsByType<SceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0].loadMikuScene();
    }
    
    public void OnRezeClicked() {
        Debug.Log("Reze");
        FindObjectsByType<SceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0].loadRezeScene();
    }
    
    public void OnCreditsClicked() {
        Debug.Log("Credits");
        FindObjectsByType<SceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0].loadCreditsScene();
    }
    
    public void OnWizardClicked() {
        Debug.Log("Wizard");
    }
}
