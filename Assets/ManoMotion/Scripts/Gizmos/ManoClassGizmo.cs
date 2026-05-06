using TMPro;
using UnityEngine;

namespace ManoMotion.Gizmos
{
    /// <summary>
    /// Displays which continuous gesture is being performed.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class ManoClassGizmo : MonoBehaviour
    {
        [SerializeField] LeftOrRightHand handLeftRight;
        [SerializeField] Color grabColor, pinchColor, pointColor;
        TextMeshProUGUI text;

        private void Awake()
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        void LateUpdate()
        {
            if (ManoMotionManager.Instance.TryGetHandInfo(handLeftRight, out HandInfo handInfo))
            {
                UpdateText(handInfo.gestureInfo.manoClass);
            }
        }

        private void UpdateText(ManoClass gesture)
        {
            switch (gesture)
            {
                case ManoClass.NO_HAND:
                    text.text = "";
                    break;
                case ManoClass.GRAB_GESTURE:
                    text.text = "Grab";
                    text.color = grabColor;
                    break;
                case ManoClass.PINCH_GESTURE:
                    text.text = "Pinch";
                    text.color = pinchColor;
                    break;
                case ManoClass.POINTER_GESTURE:
                    text.text = "Point";
                    text.color = pointColor;
                    break;
                default:
                    break;
            }
        }
    }
}