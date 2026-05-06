using UnityEngine;

namespace ManoMotion
{
    public class BoundingBoxTrigger : MonoBehaviour
    {
        [SerializeField] RectTransform rect;
        [SerializeField] BoxCollider2D boxCollider2D;

        private void Awake()
        {
            if (boxCollider2D == null)
                boxCollider2D = GetComponent<BoxCollider2D>();
            boxCollider2D.size = rect.sizeDelta;

            // Change pivot without moving
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            Vector2 size = rect.rect.size;
            Vector2 deltaPivot = rect.pivot - pivot;
            Vector3 deltaPosition = new Vector3(deltaPivot.x * size.x, deltaPivot.y * size.y);
            rect.pivot = pivot;
            rect.localPosition -= deltaPosition;
        }
    }
}