using UnityEngine.EventSystems;
using UnityEngine;

namespace Animation.InteractionHelpers
{
    public class MouseInputBehaviour : PointerInputBehaviour
    {
        [SerializeField] private MouseButton mouseButton = MouseButton.LeftClick;
        [SerializeField] private bool ignoreUI;
        [SerializeField] private bool ignoreCollision;
        [SerializeField] private LayerMask ignoreCollisionLayers;
        private Camera _mainCamera;
        private Camera mainCamera => _mainCamera ??= Camera.main;

        public override bool IsPointerBlocked
        {
            get
            {
                if (SystemInfo.deviceType == DeviceType.Handheld)
                {
                    if (Input.touchCount > 0)
                    {
                        if (ignoreUI && EventSystem.current != null &&
                            EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                        {
                            return true;
                        }
                    }
                }

                if (ignoreUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                {
                    return true;
                }

                if (ignoreCollision)
                {
                    if (mainCamera == null) {return false;}
                    if (mainCamera.gameObject.activeSelf == false) { return false;}
                    var ray = mainCamera.ScreenPointToRay(CurrentPointerPosition);
                    if (Physics.Raycast(ray, out _, Mathf.Infinity, ignoreCollisionLayers))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public override Vector2 CurrentPointerPosition => Input.mousePosition;

        public enum MouseButton
        {
            LeftClick = 0,
            RightClick = 1
        }

        protected virtual void Update()
        {
            if (Input.GetMouseButtonDown((int)mouseButton))
            {
                PushDownPointer();
            }
            if (Input.GetMouseButtonUp((int)mouseButton))
            {
                ReleaseUpPointer();
            }
        }
    }
}