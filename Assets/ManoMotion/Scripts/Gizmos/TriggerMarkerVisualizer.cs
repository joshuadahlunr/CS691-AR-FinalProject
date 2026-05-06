using System.Collections.Generic;
using UnityEngine;

namespace ManoMotion.Gizmos
{
    /// <summary>
    /// Activates a marker when a specified gesture is triggered.
    /// </summary>
    public class TriggerMarkerVisualizer : MonoBehaviour
    {
        [SerializeField] LeftOrRightHand handLeftRight;
        [SerializeField] ManoGestureTrigger gesture;
        [SerializeField] TriggerMarker marker;
        [Tooltip("Will place the marker at the center of the joints.")]
        [SerializeField] int[] joints;

        private void Update()
        {
            if (ManoMotionManager.Instance.TryGetHandInfo(handLeftRight, out HandInfo handInfo, out int handIndex))
            {
                GestureInfo gestureInfo = handInfo.gestureInfo;

                if (gestureInfo.manoGestureTrigger.Equals(gesture))
                {
                    Vector3 position = SkeletonManager.Instance.GetCenterPosition(handIndex, joints);
                    marker.Activate(ManoUtils.GetCenter(position));
                }
            }
        }
    }
}