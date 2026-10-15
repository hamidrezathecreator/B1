using UnityEngine;
using UnityEngine.EventSystems;

namespace Animation.InteractionHelpers
{
    public abstract class ColliderInputBehaviour : PointerInputBehaviour
    {
        public override Vector2 CurrentPointerPosition => Input.mousePosition;

        private void OnMouseEnter()
        {
            // Debug.Log("hover: " + name);
            EnterPointer();
        }
 
        private void OnMouseExit()
        {
            // Debug.Log("blur: " + name);
            ExitPointer();
        }
 
        private void OnMouseDown()
        {
            PushDownPointer();
        }
 
        private void OnMouseUp()
        {
            ReleaseUpPointer();
        }

        public override bool IsPointerBlocked
        {
            get
            {
                if (SystemInfo.deviceType == DeviceType.Handheld)
                {
                    if (Input.touchCount > 0)
                    {
                        return EventSystem.current != null &&
                               EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
                    }
                }
                return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            }
        } 
        
    }
}