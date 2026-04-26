#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneManager : MonoBehaviour {
#if UNITY_EDITOR
    public SceneAsset scene1Asset, scene2Asset, mainMenuAsset; // Drag scene here in Inspector
#endif

    [SerializeField] private string scene1, scene2, mainMenu; // Used in builds
    
    //public AudioSource backgroundMusic;

#if UNITY_EDITOR
    private void OnValidate()
    {
        //if (scene1Asset != null)
        //    scene1 = scene1Asset.name; // Store name for runtime
        //if (scene2Asset != null)
        //    scene2 = scene2Asset.name; // Store name for runtime
        if (mainMenuAsset != null)
            mainMenu = mainMenuAsset.name; // Store name for runtime
    }
#endif

    IEnumerator LoadScene(string sceneName)
    {
        var unloaders = FindObjectsByType<SceneUnloader>(FindObjectsSortMode.None);
        foreach(SceneUnloader unloader in unloaders)
            unloader.Unload();
        
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive); 
        // UnityEngine.SceneManagement.SceneManager.SetActiveScene(UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName));
    } 

    //public void loadScene1() {
    //    StartCoroutine(LoadScene(scene1));
    //}
    //public void loadScene2() {
    //    StartCoroutine(LoadScene(scene2));
    //}
    public void loadMainMenu() {
        StartCoroutine(LoadScene(mainMenu));
    }

    public void Awake() {
        loadMainMenu();
    }
}
