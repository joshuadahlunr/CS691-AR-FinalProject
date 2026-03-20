using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Extends RawImage to display a live camera feed.
/// Attach this component to a UI GameObject in place of a standard RawImage.
///
/// Setup:
///   1. Add this component to a UI RawImage GameObject.
///   2. Optionally assign a specific device name in the Inspector,
///      or leave blank to use the default (first available) camera.
///   3. Set your desired capture resolution and frame rate.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class CameraFeed : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Device Selection")]
    [Tooltip("Leave blank to use the first available camera.")]
    [SerializeField] private string preferredDeviceName = "";

    [Header("Capture Settings")]
    [SerializeField] private int defaultWidth  = 1280;
    [SerializeField] private int defaultHeight = 720;
    [SerializeField] private int defaultFPS     = 30;

    [Header("Display")]
    [Tooltip("Flip the image horizontally (useful for front-facing / selfie cameras).")]
    [SerializeField] private bool flipHorizontal = false;

    [Tooltip("Flip the image vertically (corrects upside-down feeds on some platforms).")]
    [SerializeField] private bool flipVertical = false;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private RawImage   _rawImage;
    public WebCamTexture webcamTexture;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake() {
        _rawImage = GetComponent<RawImage>();
    }
    private IEnumerator Start() {
    #if UNITY_EDITOR || UNITY_STANDALONE
        // No permission API needed on Editor / Standalone — go straight to init.
        InitialiseCamera();

    #elif UNITY_IOS
        yield return RequestCameraPermission_iOS();

    #elif UNITY_ANDROID
        yield return RequestCameraPermission_Android();

    #endif
        yield return null;
    }

    private void Update() {
        // Keep the RectTransform rotation in sync once the device reports
        // its actual orientation / flip state (Android / iOS may differ).
        if (webcamTexture is not null && webcamTexture.didUpdateThisFrame)
            ApplyVideoRotationAndFlip();
    }

    private void OnDisable() {
        StopCamera();
    }

    private void OnDestroy() {
        if (webcamTexture == null) return;
        webcamTexture.Stop();
        Destroy(webcamTexture);
        webcamTexture = null;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Pause the camera feed without destroying the texture.</summary>
    public void PauseCamera() {
        if (webcamTexture != null && webcamTexture.isPlaying)
            webcamTexture.Pause();
    }

    /// <summary>Resume a paused camera feed.</summary>
    public void ResumeCamera() {
        if (webcamTexture != null && !webcamTexture.isPlaying)
            webcamTexture.Play();
    }

    /// <summary>Stop the camera feed and clear the display.</summary>
    public void StopCamera() {
        if (webcamTexture != null && webcamTexture.isPlaying)
            webcamTexture.Stop();
    }

    /// <summary>
    /// Switch to a different camera device by name at runtime.
    /// Pass an empty string to fall back to the first available device.
    /// </summary>
    public void SwitchCamera(string deviceName) {
        preferredDeviceName = deviceName;
        StopCamera();

        if (webcamTexture != null) {
            Destroy(webcamTexture);
            webcamTexture = null;
        }

        InitialiseCamera();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------
    
    // ---------------------------------------------------------
    // iOS — Application.RequestUserAuthorization
    // Returns after the system dialog is dismissed.
    // ---------------------------------------------------------
    private IEnumerator RequestCameraPermission_iOS() {
        if (Application.HasUserAuthorization(UserAuthorization.WebCam)) {
            InitialiseCamera();
            yield break;
        }

        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
            InitialiseCamera();
        else {
            Debug.LogError("[CameraFeedRawImage] Camera permission denied (iOS).");
            OnPermissionDenied();
        }
    }

    // ---------------------------------------------------------
    // Android — Permission.RequestUserPermissions with callbacks
    // The callbacks fire on the main thread; a coroutine is used
    // to suspend until one of them completes.
    // ---------------------------------------------------------
    #if UNITY_ANDROID
    private bool _permissionResolved = false;
    private bool _permissionGranted  = false;

    private IEnumerator RequestCameraPermission_Android() {
        if (Permission.HasUserAuthorizedPermission(Permission.Camera)) {
            InitialiseCamera();
            yield break;
        }

        _permissionResolved = false;
        _permissionGranted  = false;

        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted         += OnAndroidPermissionGranted;
        callbacks.PermissionDenied          += OnAndroidPermissionDenied;
        callbacks.PermissionDeniedAndDontAskAgain += OnAndroidPermissionDeniedPermanently;

        Permission.RequestUserPermission(Permission.Camera, callbacks);

        // Suspend until a callback fires.
        yield return new WaitUntil(() => _permissionResolved);

        if (_permissionGranted)
            InitialiseCamera();
        else
            OnPermissionDenied();
    }

    private void OnAndroidPermissionGranted(string permissionName) {
        Debug.Log($"[CameraFeedRawImage] Permission granted: {permissionName}");
        _permissionGranted  = true;
        _permissionResolved = true;
    }

    private void OnAndroidPermissionDenied(string permissionName) {
        Debug.LogWarning($"[CameraFeedRawImage] Permission denied: {permissionName}");
        _permissionGranted  = false;
        _permissionResolved = true;
    }

    private void OnAndroidPermissionDeniedPermanently(string permissionName) {
        Debug.LogError($"[CameraFeedRawImage] Permission permanently denied: {permissionName}. Direct the user to App Settings to re-enable it.");
        _permissionGranted  = false;
        _permissionResolved = true;
    }
    #endif

    // ---------------------------------------------------------
    // Shared denial handler — override or extend as needed.
    // ---------------------------------------------------------
    private void OnPermissionDenied() {
        // Show UI feedback, disable the component, open app settings, etc.
        enabled = false;
    }

    private void InitialiseCamera() {
        WebCamDevice[] devices = WebCamTexture.devices;
    
        if (devices.Length == 0) {
            Debug.LogError("[CameraFeedRawImage] No camera devices found.");
            return;
        }
    
        WebCamDevice selectedDevice = ResolveDevice(devices);
        var (resolution, refreshRate) = GetPreferredResolution(selectedDevice);
        var fps = (int)(1.0 / refreshRate.value);
    
        webcamTexture = new WebCamTexture(selectedDevice.name, resolution.x, resolution.y, fps);
        _rawImage.texture = webcamTexture;
        webcamTexture.Play();
    
        Debug.Log($"[CameraFeedRawImage] Started '{selectedDevice.name}' @ {resolution.x}x{resolution.y}, {fps} fps.");
    }
    
    private WebCamDevice ResolveDevice(WebCamDevice[] devices) {
        if (!string.IsNullOrEmpty(preferredDeviceName)) {
            foreach (var device in devices)
                if (device.name == preferredDeviceName)
                    return device;
            Debug.LogWarning($"[CameraFeedRawImage] Device '{preferredDeviceName}' not found. Using default.");
        }
        return devices[0];
    }
    
    private (Vector2Int, RefreshRate) GetPreferredResolution(WebCamDevice device) {
        Resolution[] resolutions = device.availableResolutions;
    
        // availableResolutions can be null on some platforms (e.g. older Android, WebGL)
        if (resolutions == null || resolutions.Length == 0) {
            Debug.LogWarning("[CameraFeedRawImage] No resolution list available, falling back to inspector values.");
            var rate = new RefreshRate();
            rate.numerator = 1;
            rate.denominator = (uint)defaultFPS;
            return (new Vector2Int(defaultWidth, defaultHeight), rate);
        }
    
        // Pick the resolution with the highest pixel count as the "preferred" one.
        // You could also sort by refreshRate, or find the closest to a target size.
        Resolution best = resolutions[0];
        foreach (var r in resolutions)
            if (r.width * r.height > best.width * best.height)
                best = r;
    
        Debug.Log($"[CameraFeedRawImage] Chose resolution {best.width}x{best.height} @ {best.refreshRateRatio}hz from {resolutions.Length} available.");
    
        return (new Vector2Int(best.width, best.height), best.refreshRateRatio);
    }

    /// <summary>
    /// Correct rotation/flip differences reported by the WebCamTexture
    /// and the optional inspector overrides.
    /// </summary>
    private void ApplyVideoRotationAndFlip() {
        // videoRotationAngle accounts for device orientation on mobile.
        float angle = -webcamTexture.videoRotationAngle;

        // videoVerticallyMirrored is set by Unity based on the driver.
        bool mirroredByDriver = webcamTexture.videoVerticallyMirrored;

        // Combine driver mirroring with inspector overrides.
        bool finalFlipX = flipHorizontal;
        bool finalFlipY = flipVertical ^ mirroredByDriver;

        _rawImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
        _rawImage.uvRect = BuildUVRect(finalFlipX, finalFlipY);
    }

    private static Rect BuildUVRect(bool flipX, bool flipY) {
        float x      = flipX ? 1f : 0f;
        float y      = flipY ? 1f : 0f;
        float width  = flipX ? -1f : 1f;
        float height = flipY ? -1f : 1f;
        return new Rect(x, y, width, height);
    }
}