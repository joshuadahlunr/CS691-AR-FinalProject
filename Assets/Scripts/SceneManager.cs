#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneManager : MonoBehaviour {
#if UNITY_EDITOR
    public SceneAsset CoversSceneAsset, PokeballSceneAsset, ZenSceneAsset, RezeSceneAsset, CreditsSceneAsset, mainMenuAsset; // Drag scene here in Inspector
#endif

    [SerializeField] private string CoversScene, PokeballScene, ZenScene, RezeScene, CreditsScene, mainMenu; // Used in builds
    
    //public AudioSource backgroundMusic;

#if UNITY_EDITOR
    private void OnValidate()
    {
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
        if (mainMenuAsset != null)
            mainMenu = mainMenuAsset.name; // Store name for runtime
    }
#endif

    IEnumerator LoadScene(string sceneName) {
        var unloaders = FindObjectsByType<SceneUnloader>(FindObjectsSortMode.None);
        foreach(SceneUnloader unloader in unloaders)
            unloader.Unload();
        
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive); 
        // UnityEngine.SceneManagement.SceneManager.SetActiveScene(UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName));
    } 

    public void loadCoversScene() {
        StartCoroutine(LoadScene(CoversScene));
    }
    public void loadPokeballScene() {
        StartCoroutine(LoadScene(PokeballScene));
    }
    public void loadZenScene() {
        StartCoroutine(LoadScene(ZenScene));
    }
    public void loadRezeScene() {
        StartCoroutine(LoadScene(RezeScene));
    }
    public void loadCreditsScene() {
        StartCoroutine(LoadScene(CreditsScene));
    }
    public void loadMainMenu() {
        StartCoroutine(LoadScene(mainMenu));
    }

    public void Awake() {
        loadMainMenu();
    }
}
