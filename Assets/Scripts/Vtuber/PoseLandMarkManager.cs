using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Rendering;

/// <summary>
/// Real-time Pose Prediction using MediaPipe Tasks API in Unity.
/// Equivalent to the Python script using mediapipe >= 0.10.
///
/// Setup:
///   1. Import MediaPipe Unity Plugin (github.com/homuler/MediaPipeUnityPlugin)
///   2. Place pose_landmarker_full.task in StreamingAssets/
///   3. Attach this script to a GameObject in your scene
///   4. Assign the RawImage and PoseOverlay references in the Inspector
/// </summary>
public class PoseLandmarkerManager : MonoBehaviour {
    [Header("Model Settings")]
    [Tooltip("Filename inside StreamingAssets/")]
    public string modelFileName = "pose_landmarker_full.task";

    [Range(1, 4)]
    public int numPoses = 1;

    [Range(0f, 1f)] public float minPoseDetectionConfidence = 0.5f;
    [Range(0f, 1f)] public float minPosePresenceConfidence  = 0.5f;
    [Range(0f, 1f)] public float minTrackingConfidence      = 0.5f;

    [Header("UI References")]
    [Tooltip("CameraFeed providing the webcam texture")]
    public CameraFeed cameraFeed;
    [Tooltip("Webcam Texture to pull the video frames from... overrided by the cameraFeed's texture if set")]
    public WebCamTexture webcamTexture;

    [Tooltip("Overlay Canvas/RectTransform for drawing landmarks")]
    public RectTransform poseOverlay;
    public PoseWorldMapper poseWorldMapper;
    
    [Header("Inference Resolution")]
    [Tooltip("Width fed to MediaPipe. Webcam display is unaffected.")]
    public int inferenceWidth  = 640;
    [Tooltip("Height fed to MediaPipe. Keep the same aspect ratio as the webcam.")]
    public int inferenceHeight = 480;
    
    // NativeArrays live outside managed heap -- Burst jobs can touch them safely
    private NativeArray<Color32> _srcNative; // full-res readback destination
    private NativeArray<Color32> _dstNative; // downsampled + flipped output
 
    // Async readback
    private AsyncGPUReadbackRequest _readbackRequest;
    private bool                    _readbackPending;
 
    // Latest completed inference result (written from readback callback)
    public PoseLandmarkerResult latestResult;
    private bool                 _resultReady;
    

    // ── Runtime ───────────────────────────────────────────────────────────────
    private PoseLandmarker     _landmarker;
    private Texture2D          _inputTexture;
    private PoseDrawer         _poseDrawer;
    
    private int   _screenshotIndex;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────
    private IEnumerator Start() {
        yield return InitLandmarker();

        if(poseOverlay)
            _poseDrawer = new PoseDrawer(poseOverlay, true);
        _srcNative = new NativeArray<Color32>(8, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _dstNative = new NativeArray<Color32>(inferenceWidth * inferenceHeight, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
    }

    private void Update() {
        if(_landmarker == null) return;
        if (cameraFeed) {
            if (!cameraFeed.enabled) return;
            webcamTexture = cameraFeed.webcamTexture;
        } 
        if (!webcamTexture) return;
        if (!webcamTexture.didUpdateThisFrame) return;

        if (webcamTexture.width * webcamTexture.height != _srcNative.Length) {
            // _srcNative.Dispose();
            _srcNative = new NativeArray<Color32>(webcamTexture.width * webcamTexture.height, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        if (!_inputTexture || inferenceWidth * inferenceHeight != _dstNative.Length) {
            _inputTexture = new Texture2D(inferenceWidth, inferenceHeight, TextureFormat.RGBA32, false);
            // _dstNative.Dispose();
            _dstNative = new NativeArray<Color32>(inferenceWidth * inferenceHeight, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        // --- Kick off async GPU readback once per new webcam frame ----------
        // AsyncGPUReadback.Request returns immediately; the callback fires
        // 1-2 frames later when the GPU has finished writing the pixels.
        // This means zero CPU stall -- the main thread is never blocked.
        if (webcamTexture.didUpdateThisFrame && !_readbackPending) {
            _readbackRequest = AsyncGPUReadback.Request(
                webcamTexture,
                0, // mip level
                // TextureFormat.RGBA32,
                OnReadbackComplete);        // called from main thread when ready
            _readbackPending = true;
        }
 
        // --- Draw the latest result (decoupled from readback cadence) -------
        if (!_resultReady) return;
        if (!(poseOverlay || poseWorldMapper)) return;
        
        if(poseOverlay)
            _poseDrawer.Draw(latestResult,
                webcamTexture.width, webcamTexture.height,
                1f/Time.deltaTime, true);
        if (poseWorldMapper) poseWorldMapper.UpdateLandmarks(latestResult);
        _resultReady = false;
    }
    
    // -----------------------------------------------------------------------
    // Async readback callback
    // Called on the main thread ~1-2 frames after Request(), with no GPU stall.
    // -----------------------------------------------------------------------
 
    private void OnReadbackComplete(AsyncGPUReadbackRequest req) {
        _readbackPending = false;
 
        if (req.hasError)
            return;
 
        // GetData<T> returns a NativeArray view into the readback buffer.
        // We copy into _srcNative so the buffer is ours to keep across frames.
        var readbackData = req.GetData<Color32>();
        readbackData.CopyTo(_srcNative);
 
        // --- Schedule Burst job: downsample + Y-flip in parallel ------------
        // var job = new DownsampleFlipJob {
        //     Src    = _srcNative,
        //     Dst    = _dstNative,
        //     SrcW   = webcamTexture.width,
        //     SrcH   = webcamTexture.height,
        //     DstW   = inferenceWidth,
        //     DstH   = inferenceHeight,
        // };
 
        int srcW = webcamTexture.width;
        int srcH = webcamTexture.height;
        int dstW = inferenceWidth;
        int dstH = inferenceHeight;
 
        for (int dstY = 0; dstY < dstH; dstY++) {
            // Flip Y: dstY=0 should map to the TOP of the source image.
            // Source rows are bottom-up, so top of image = last row in buffer.
            int srcY = (srcH - 1) - Mathf.RoundToInt((float)dstY / (dstH - 1) * (srcH - 1));
 
            for (int dstX = 0; dstX < dstW; dstX++) {
                int srcX = Mathf.RoundToInt((float)dstX / (dstW - 1) * (srcW - 1));
                _dstNative[dstY * dstW + dstX] = _srcNative[srcY * srcW + srcX];
            }
        }
 
        // --- Zero-copy GPU upload -------------------------------------------
        // LoadRawTextureData writes the NativeArray directly into the texture's
        // GPU buffer without marshalling through a managed Color32[].
        _inputTexture.LoadRawTextureData(_dstNative);
        _inputTexture.Apply(false, false);
 
        // --- Inference ------------------------------------------------------
        var mpImage = new Mediapipe.Image(
            ImageFormat.Types.Format.Srgba,
            inferenceWidth, inferenceHeight,
            inferenceWidth * 4,
            _inputTexture.GetRawTextureData<byte>());
            // _dstNative.Reinterpret<byte>());
 
        long tsMs    = (long)(Time.realtimeSinceStartup * 1000);
        latestResult = _landmarker.DetectForVideo(mpImage, tsMs);
        _resultReady  = true;
    }

    private void OnDestroy() {
        _landmarker?.Close();
        if (webcamTexture != null && webcamTexture.isPlaying)
            webcamTexture.Stop();
    }

    private IEnumerator InitLandmarker() {
        string modelPath = System.IO.Path.Combine(Application.streamingAssetsPath, modelFileName);

#if UNITY_ANDROID && !UNITY_EDITOR
        // On Android, StreamingAssets are inside a .apk — copy to persistentDataPath first
        modelPath = System.IO.Path.Combine(Application.persistentDataPath, modelFileName);
        if (!System.IO.File.Exists(modelPath)) {
            var loader = UnityEngine.Networking.UnityWebRequest.Get(
                System.IO.Path.Combine(Application.streamingAssetsPath, modelFileName));
            yield return loader.SendWebRequest();
            System.IO.File.WriteAllBytes(modelPath, loader.downloadHandler.data);
        }
#else
        yield return null; // keep coroutine signature consistent
#endif

        var baseOptions = new BaseOptions(modelAssetPath: modelPath);
        var options = new PoseLandmarkerOptions(
            baseOptions,
            runningMode:                  RunningMode.VIDEO,
            numPoses:                     numPoses,
            minPoseDetectionConfidence:   minPoseDetectionConfidence,
            minPosePresenceConfidence:    minPosePresenceConfidence,
            minTrackingConfidence:        minTrackingConfidence);

        _landmarker = PoseLandmarker.CreateFromOptions(options);
        Debug.Log("PoseLandmarker initialised.");
        Debug.Assert(_landmarker != null);
    }
}