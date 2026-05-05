using UnityEngine;
using UnityEngine.Audio;

public class SettingsEvents : MonoBehaviour {
    public GameObject settingsPanel;
    public AudioMixer mixer;
    
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
        
        Debug.Log("Opened Settings");
        settingsPanel.SetActive(true);
    }

    public void SetFPS(float fps) {
        QualitySettings.vSyncCount = 0; // Disable VSync to allow custom FPS
        Application.targetFrameRate = (int)fps;
    }

    public void SetVolume(float volume) {
        mixer.SetFloat("Volume", volume);
    }

    public void SetQuality(int quality) {
        QualitySettings.SetQualityLevel(quality == 0 ? 1 : 0, true);
    }
    

    // Call this method to set the flashlight state
    public void SetFlashlight(bool enable) {
#if UNITY_ANDROID && !UNITY_EDITOR
        SetAndroid(enable);
#elif UNITY_IOS && !UNITY_EDITOR
        SetIOS(enable);
#else
        Debug.Log($"Flashlight set to: {enable} (Editor - no real flashlight)");
#endif
    }

    // ── Android ──────────────────────────────────────────────────────────────
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject cameraManager;
    private string cameraId;

    private void InitAndroid()
    {
        using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity    = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        using var context     = activity.Call<AndroidJavaObject>("getApplicationContext");

        cameraManager = context.Call<AndroidJavaObject>("getSystemService", "camera");

        string[] ids = cameraManager.Call<string[]>("getCameraIdList");
        cameraId = ids.Length > 0 ? ids[0] : null;
    }

    private void SetAndroid(bool enable)
    {
        if (cameraManager == null) InitAndroid();
        if (cameraId == null)
        {
            Debug.LogError("No camera found on this device.");
            return;
        }

        cameraManager.Call("setTorchMode", cameraId, enable);
        Debug.Log($"Android flashlight: {(enable ? "ON" : "OFF")}");
    }
#endif

    // ── iOS ───────────────────────────────────────────────────────────────────
#if UNITY_IOS && !UNITY_EDITOR
    private void SetIOS(bool enable)
    {
        iPhoneTorch(enable);
        Debug.Log($"iOS flashlight: {(enable ? "ON" : "OFF")}");
    }

    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void iPhoneTorch(bool enable);
#endif
    
}
