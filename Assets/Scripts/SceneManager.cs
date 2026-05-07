#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneManager : MonoBehaviour {
#if UNITY_EDITOR
    public SceneAsset CoversSceneAsset, PokeballSceneAsset, ZenSceneAsset, RezeSceneAsset, CreditsSceneAsset, MikuSceneAsset, HandMagicSceneAsset, mainMenuAsset, arBaseAsset; // Drag scene here in Inspector
#endif

    [SerializeField] private string CoversScene, PokeballScene, ZenScene, RezeScene, CreditsScene, MikuScene, HandMagicScene, mainMenu, arBase; // Used in builds
    
    public bool loadMainMenuOnStart = true;
    public AudioManager audio;
    public string currentlyLoadedScene = "";

#if UNITY_EDITOR
    private void OnValidate() {
        if (CoversSceneAsset != null)
            CoversScene = CoversSceneAsset.name; // Store name for runtime
        if (PokeballSceneAsset != null)
            PokeballScene = PokeballSceneAsset.name; // Store name for runtime
        if (ZenSceneAsset != null)
            ZenScene = ZenSceneAsset.name; // Store name for runtime
        if (RezeSceneAsset != null)
            RezeScene = RezeSceneAsset.name; // Store name for runtime
        if (CreditsSceneAsset != null)
            CreditsScene = CreditsSceneAsset.name; // Store name for runtime
        if (MikuSceneAsset != null)
            MikuScene = MikuSceneAsset.name; // Store name for runtime
        if (HandMagicSceneAsset != null)
            HandMagicScene = HandMagicSceneAsset.name; // Store name for runtime
        if (mainMenuAsset != null)
            mainMenu = mainMenuAsset.name; // Store name for runtime
        if (arBaseAsset != null)
            arBase = arBaseAsset.name; // Store name for runtime
    }
#endif

    private IEnumerator LoadSceneAdditive(string sceneName) {
        if (currentlyLoadedScene.Length > 0) {
            var unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(currentlyLoadedScene);
            yield return unload;
        }

        // var unloaders = FindObjectsByType<SceneUnloader>(FindObjectsSortMode.None);
        // foreach(SceneUnloader unloader in unloaders)
        //     unloader.Unload();

        currentlyLoadedScene = sceneName;
        var load = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        yield return load; 
        // UnityEngine.SceneManagement.SceneManager.SetActiveScene(UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName));
    } 

    public void loadCoversScene() {
        StartCoroutine(LoadSceneAdditive(CoversScene));
    }
    public void loadPokeballScene() {
        StartCoroutine(LoadSceneAdditive(PokeballScene));
    }
    public void loadZenScene() {
        StartCoroutine(LoadSceneAdditive(ZenScene));
    }
    public void loadRezeScene() {
        StartCoroutine(LoadSceneAdditive(RezeScene));
    }
    public void loadCreditsScene() {
        StartCoroutine(LoadSceneAdditive(CreditsScene));
    }
    public void loadMainMenu() {
        StartCoroutine(LoadSceneAdditive(mainMenu));
    }
    
    private IEnumerator LoadScene(string sceneName, bool updateCurrentlyLoadedScene = false) {
        currentlyLoadedScene = updateCurrentlyLoadedScene ? sceneName : "";
        var load = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        yield return load; 
    }

    public IEnumerator ResetCoro() {
        yield return LoadScene(arBase);
        //yield return LoadSceneAdditive(mainMenu); // Called automatically in Awake from AR Base
    }

    public void reset() {
        StartCoroutine(ResetCoro());
    }

    public void loadMikuScene() {   
        StartCoroutine(LoadScene(MikuScene));
    }

    public void loadHandMagicScene() {
        StartCoroutine(LoadScene(HandMagicScene));
    }

    public void Awake() {
        if(loadMainMenuOnStart) loadMainMenu();
    }
}
