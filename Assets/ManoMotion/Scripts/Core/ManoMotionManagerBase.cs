using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using ManoMotion.CameraSystem;

namespace ManoMotion
{
    [Serializable]
    public enum ProcessingType
    {
        Sync, // Run image processing on Unitys main thread
        Async // Run image processing asynchronously on other threads
    }

    /// <summary>
    /// Base class for ManoMotionManager. Handles library imports and settings.
    /// </summary>
    public abstract partial class ManoMotionManagerBase : MonoBehaviour
    {
        [Tooltip("The FPS to target. 0 for default Unity settings.")]
        [SerializeField] int targetFps = 0;
        [SerializeField] protected ProcessingType processingType = ProcessingType.Async;
        [Tooltip("The information about the Session values, the SDK settings")]
        [SerializeField] protected Session manomotionSession;

        /// <summary>
        /// Index 0 for left hand, 1 for right hand.
        /// </summary>
        [Tooltip("Hand information processed by the SDK.")]
        [SerializeField] protected HandInfo[] handInfos;

        [Space]
        [SerializeField] protected bool overrideLicense;
        [SerializeField] protected string customLicense;

        protected AssetStatus status;
        protected ManoSettings settings;
        protected float version;
        protected InputManagerBase inputManager;

        // Information about camera image
        protected ManoMotionFrame currentManomotionFrame;
        protected VisualizationInfo visualizationInfo;
        protected Color32[] leftMaskBuffer, rightMaskBuffer; // Hand occlusion buffers

        protected int width, height;
        protected int fps, processingTime;

        protected float fpsUpdateTimer = 0;
        protected int frameCount = 0;
        protected List<int> processingTimeList = new List<int>();

        #region Events

        ///Sends information after each frame is processed by the SDK.
        public static Action OnManoMotionFrameProcessed;

        ///Sends information after the license is checked by the SDK.
        public static Action OnManoMotionLicenseInitialized;

        ///Sends information when changing between 2D and 3D joints
        public static Action<SkeletonModel> OnSkeletonActivated;

        ///Sends information when changing the smoothing value
        public static Action<float> OnSmoothingValueChanged;

        public static Action<bool> OnContourToggle;

        #endregion

        #region Properties
        internal int ProcessingTime => processingTime;

        internal ProcessingType ProcessingType => processingType;

        internal int Fps => fps;

        internal int Width => width;

        internal int Height => height;

        public float Version => version;

        internal ref VisualizationInfo VisualizationInfo => ref visualizationInfo;

        public HandInfo[] HandInfos => handInfos;

        public AssetStatus LicenseStatus => status;

        public Session ManomotionSession => manomotionSession;

        public InputManagerBase InputManager => inputManager;

        #endregion

        #region Library imports

#if UNITY_IOS
        protected const string library = "__Internal";
#else
        protected const string library = "manomotion";
#endif

        [DllImport(library)]
        protected static extern void initSetup(int imageFormat, ref AssetStatus status);

        [DllImport(library)]
        protected static extern void initSetupWithKey(ManoSettings settings, ref AssetStatus status);

        [DllImport(library)]
        protected static extern void stop();

        [DllImport(library)]
        protected static extern void SetLeftRightFrameArrayPointers(IntPtr left, IntPtr right, int width, int height, bool isAsync);

        /// <summary>
        /// Tell the SDK to process the current frame
        /// </summary>
        [DllImport(library)]
        protected static extern IntPtr GetRenderEventFunc();

        [DllImport(library)]
        protected static extern int copyHandInfo(ref HandInfo firstHandInfo, ref HandInfo secondHandInfo, ref Session manomotion_session);

        [DllImport(library)]
        protected static extern void processFrameTwoHands(ref HandInfo firstHandInfo, ref HandInfo secondHandInfo, ref Session manomotion_session, int new_width = 0, int new_height = 0);

        [DllImport(library)]
        protected static extern void setResolution(int width, int height);

        [DllImport(library)]
        protected static extern void setMRFrameArrays(Color32[] frame, Color32[] frameSecond);

        [DllImport(library)]
        protected static extern void getPerformanceInfo(ref int processingTime, ref int getImageTime);

        #endregion

        protected virtual void Awake()
        {
            Application.targetFrameRate = targetFps;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            handInfos = new HandInfo[2];
            for (int i = 0; i < handInfos.Length; i++)
            {
                handInfos[i] = new HandInfo();
                handInfos[i].gestureInfo = new GestureInfo();
                handInfos[i].gestureInfo.manoClass = ManoClass.NO_HAND;
                handInfos[i].gestureInfo.handSide = HandSide.None;
                handInfos[i].gestureInfo.leftRightHand = LeftOrRightHand.NO_HAND;
                handInfos[i].trackingInfo = new TrackingInfo();
                handInfos[i].trackingInfo.boundingBox = new BoundingBox();
                handInfos[i].trackingInfo.boundingBox.topLeft = new Vector3();

            }
            visualizationInfo.rgbImage = new Texture2D(0, 0);
            visualizationInfo.leftHandMask = new Texture2D(0, 0);
            visualizationInfo.rightHandMask = new Texture2D(0, 0);
            visualizationInfo.leftHandMask.filterMode = FilterMode.Point;
            visualizationInfo.rightHandMask.filterMode = FilterMode.Point;
        }

        protected void Init()
        {
            settings.imageFormat = ImageFormat.BGRA_FORMAT;
            settings.serialKey = customLicense;

            if (!overrideLicense)
                initSetup((int)ImageFormat.BGRA_FORMAT, ref status);
            else
                initSetupWithKey(settings, ref status);

            version = status.version;
            OnManoMotionLicenseInitialized?.Invoke();
        }

        /// <summary>
        /// Calculates the Frames Per Second in the application and retrieves the estimated Processing time.
        /// </summary>
        protected void CalculateFPSAndProcessingTime()
        {
            fpsUpdateTimer += Time.deltaTime;
            frameCount++;
            if (fpsUpdateTimer >= 1)
            {
                fps = frameCount;
                frameCount = 0;
                fpsUpdateTimer -= 1;
                CalculateProcessingTime();
            }
        }

        /// <summary>
        /// Calculates the elapses time needed for processing the frame.
        /// </summary>
        protected void CalculateProcessingTime()
        {
            if (processingType.Equals(ProcessingType.Async))
            {
                int processing = 0;
                int image = 0;
                getPerformanceInfo(ref processing, ref image);
                processingTime = processing;
            }
            else if (processingTimeList.Count > 0)
            {
                int sum = 0;
                for (int i = 0; i < processingTimeList.Count; i++)
                {
                    sum += processingTimeList[i];
                }
                sum /= processingTimeList.Count;
                processingTime = sum;
                processingTimeList.Clear();
            }
        }

        /// <summary>
        /// Sets the resolution values used throughout the initialization methods of the arrays and textures.
        /// </summary>
        /// <param name="width">Requires a width value.</param>
        /// <param name="height">Requires a height value.</param>
        protected virtual void SetResolutionValues(int width, int height)
        {
            Debug.Log($"Setting resolution values: {width}x{height}");
            this.width = width;
            this.height = height;
            setResolution(width, height);
            visualizationInfo.rgbImage.Reinitialize(width, height);

            leftMaskBuffer = new Color32[width * height];
            rightMaskBuffer = new Color32[width * height];
            setMRFrameArrays(leftMaskBuffer, rightMaskBuffer);

            visualizationInfo.leftHandMask.Reinitialize(width, height);
            visualizationInfo.leftHandMask.SetPixels32(leftMaskBuffer);
            visualizationInfo.leftHandMask.Apply();
            visualizationInfo.rightHandMask.Reinitialize(width, height);
            visualizationInfo.rightHandMask.SetPixels32(rightMaskBuffer);
            visualizationInfo.rightHandMask.Apply();
        }

        #region Feature Settings

        /// <summary>
        /// Lets the SDK know if it should calculate the Skeleton in 3D or 2D.
        /// And gives information to SkeletonManager which skeleton model to display.
        /// </summary>
        /// <param name="condition">run or not</param>
        public void ShouldCalculateSkeleton3D(bool condition)
        {
            int boolValue = condition ? 1 : 0;
            manomotionSession.enabledFeatures.skeleton3D = boolValue;
            OnSkeletonActivated?.Invoke((SkeletonModel)boolValue);
        }

        /// <summary>
        /// Lets the SDK know that it needs to calculate the Gestures.
        /// </summary>
        /// <param name="condition">run or not</param>
        public void ShouldCalculateGestures(bool condition)
        {
            manomotionSession.enabledFeatures.gestures = condition ? 1 : 0;
        }

        /// <summary>
        /// Lets the SDK know if it should run fast mode or not.
        /// </summary>
        /// <param name="condition">run or not</param>
        public void ShouldRunFastMode(bool condition)
        {
            manomotionSession.enabledFeatures.fastMode = condition ? 1 : 0;
        }

        /// <summary>
        /// Lets the SDK know if it should calculate wrist information.
        /// </summary>
        /// <param name="condition">run or not</param>
        public void ShouldRunWristInfo(bool condition)
        {
            manomotionSession.enabledFeatures.wristInfo = condition ? 1 : 0;
        }

        /// <summary>
        /// Lets the SDK know if it should calculate finger information.
        /// 4 will run the finger_info for the ring finger, 0 is off.
        /// </summary>
        /// <param name="condition">run or not</param>
        public void ShouldRunFingerInfo(bool condition)
        {
            manomotionSession.enabledFeatures.fingerInfo = condition ? 4 : 0;
        }

        /// <summary>
        /// Toggle wich finger to use for the finger information.
        /// 0 = off
        /// 1 = Thumb
        /// 2 = Index
        /// 3 = Middle
        /// 4 = Ring
        /// 5 = Pinky
        /// </summary>
        /// <param name="index">int between 0 and 5, 0 is off and 1-5 is the different fingers</param>
        public void ToggleFingerInfoFinger(int index)
        {
            int minIndex = 0;
            int maxIndex = 5;

            if (index >= minIndex && index <= maxIndex)
            {
                manomotionSession.enabledFeatures.fingerInfo = index;
            }
            else
            {
                Debug.Log("index needs to between 0 and 5, current index = " + index);
            }
        }

        /// <summary>
        /// Lets the SDK know if it should calculate hand contour.
        /// </summary>
        /// <param name="condition">run or not</param>
        public void ShouldRunContour(bool condition)
        {
            manomotionSession.enabledFeatures.contour = condition ? 1 : 0;
            OnContourToggle?.Invoke(condition);
        }

        #endregion

        /// <summary>
        /// Returns true and gives back the hand info of the left/right hand specified.
        /// </summary>
        public bool TryGetHandInfo(LeftOrRightHand leftRight, out HandInfo handInfo)
        {
            handInfo = default;

            if (leftRight < LeftOrRightHand.LEFT_HAND || leftRight > LeftOrRightHand.RIGHT_HAND)
                return false;

            HandInfo hand = handInfos[(int)leftRight];
            SkeletonInfo skeletonInfo = hand.trackingInfo.skeleton;

            if (skeletonInfo.confidence == 1 && HasValidPositions(skeletonInfo.jointPositions))
            {
                handInfo = hand;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true and gives back the hand info of the left/right hand specified.
        /// </summary>
        public bool TryGetHandInfo(LeftOrRightHand leftRight, out HandInfo handInfo, out int handIndex)
        {
            handInfo = default;
            handIndex = 0;

            if (leftRight < LeftOrRightHand.LEFT_HAND || leftRight > LeftOrRightHand.RIGHT_HAND)
                return false;

            HandInfo hand = handInfos[(int)leftRight];
            SkeletonInfo skeletonInfo = hand.trackingInfo.skeleton;

            if (skeletonInfo.confidence == 1 && HasValidPositions(skeletonInfo.jointPositions))
            {
                handInfo = hand;
                handIndex = (int)leftRight;
                return true;
            }
            return false;
        }

        public bool AnyHandVisible()
        {
            for (LeftOrRightHand hand = LeftOrRightHand.LEFT_HAND; hand <= LeftOrRightHand.RIGHT_HAND; hand++)
            {
                SkeletonInfo skeletonInfo = handInfos[(int)hand].trackingInfo.skeleton;
                if (skeletonInfo.confidence == 1 && HasValidPositions(skeletonInfo.jointPositions))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns false if all positions are zero, positive infinity or negative infinity.
        /// </summary>
        private bool HasValidPositions(Vector3[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 position = positions[i];
                if (position != Vector3.zero && position != Vector3.positiveInfinity && position != Vector3.negativeInfinity)
                    return true;
            }
            return false;
        }

        public virtual void SetProcessingType(ProcessingType processingType)
        {
            this.processingType = processingType;
        }
    }
}