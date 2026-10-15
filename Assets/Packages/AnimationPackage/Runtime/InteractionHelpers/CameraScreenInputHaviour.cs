using UnityEngine;

namespace Animation.InteractionHelpers
{
    [RequireComponent(typeof(RectTransform))]
    public class CameraScreenInputHaviour : UIInputBehaviour
    {
        [SerializeField] private LayerMask layerMask;
        [SerializeField] private Camera camera3d;
        [SerializeField] private float maxDistance = 100f;
        
        private RectTransform thisRect;
        private Collider currentHoveredCollider;
        private void OnEnable()
        {
            thisRect = transform as RectTransform;
            if (camera3d == null)
            {
                Debug.LogError("Please assign the camera");
            }
        }

        private void Update()
        {
            var collider = Raycast();
            if (collider != null)
            {
                if (currentHoveredCollider != null)
                {
                    if (currentHoveredCollider == collider) return;
                    
                    currentHoveredCollider.SendMessage("OnMouseExit", SendMessageOptions.DontRequireReceiver);
                    currentHoveredCollider = null;
                }
                else
                {
                    currentHoveredCollider = collider;
                    currentHoveredCollider.SendMessage("OnMouseEnter", SendMessageOptions.DontRequireReceiver);   
                }
            }
            else
            {
                if (currentHoveredCollider == null) return;
                
                currentHoveredCollider.SendMessage("OnMouseExit", SendMessageOptions.DontRequireReceiver);
                currentHoveredCollider = null;
            }
        }

        protected override void OnPushAction()
        {
            var collider = Raycast();
            if (collider != null)
            {
                collider.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);
            }
        }

        protected override void OnReleaseAction()
        {
            var collider = Raycast();
            if (collider != null)
            {
                collider.SendMessage("OnMouseUp", SendMessageOptions.DontRequireReceiver);
            }
        }

        public Collider Raycast()
        {
            Vector3 pointerPosition = CurrentPointerPosition;
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying == false)
            {
                return null;
            }
#endif
            var localPoint = new Vector2();
            var rectScreenBounds =
                RectTransformUtility.WorldToScreenPoint(null,
                    thisRect.position) -
                new Vector2(thisRect.rect.width / 2f,
                    thisRect.rect.height / 2f);

            localPoint.x = Mathf.InverseLerp(rectScreenBounds.x, rectScreenBounds.x + thisRect.rect.width, pointerPosition.x);
            localPoint.y = Mathf.InverseLerp(rectScreenBounds.y, rectScreenBounds.y + thisRect.rect.height, pointerPosition.y);

            var rayX = (float)(localPoint.x * (float)camera3d.pixelWidth);
            var rayY = (float)(localPoint.y * (float)camera3d.pixelHeight);
            var mapRay = camera3d.ScreenPointToRay(new Vector2(rayX, rayY));
            
            if (Physics.Raycast(mapRay, out var mapHit, maxDistance, layerMask, QueryTriggerInteraction.Collide))
            {
                return mapHit.collider;
            }
            return null;
        }
    }
}