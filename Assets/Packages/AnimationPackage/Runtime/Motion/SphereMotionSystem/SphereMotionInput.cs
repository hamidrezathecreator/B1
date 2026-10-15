using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Linq;
using Animation.InteractionHelpers;
using UnityEngine;

namespace Animation.Motion.SphereMotionSystem
{
    public class SphereMotionInput : MonoBehaviour
    {
        private const float DragVelocityMultiplier = 0.001f;
        
        [SerializeField] private SphereMotion cameraMotion;
        [SerializeField] private TouchInputBehaviour touchInputBehaviour;
        [SerializeField] private MouseInputBehaviour mouseInputBehaviour;
        [SerializeField] [Range(1f, 20f)] private float dragVelocity = 4f;
        [SerializeField] [Range(1f, 20f)] private float maxDragSpeed = 10f;
        [SerializeField] [Range(1f, 20f)] private float dragFriction = 5f;
        [SerializeField] private List<GameObject> inputUIs;
        [SerializeField] private float zoomSpeed;
        
        public SphereMotion CameraMotion => cameraMotion;
        
        private bool outOfPosition;
        private float zoomDelta;
        private bool isActive = true;
        private Vector2 currentSpeed;
        private float targetCameraDistance;
        private Vector2 lastTouchPosition;
        private Vector2 lastMousePosition;
        private Vector2 currentDragDirection;
        
        private EventSystem currentEventSystem;

        public bool IsActive
        {
            get => isActive;
            set
            {
                isActive = value;
                if (value == false)
                {
                    targetCameraDistance = cameraMotion.Distance;
                }
            }
        }

        private void Awake()
        {
            currentEventSystem = EventSystem.current;
        }

        private void Start()
        {
            targetCameraDistance = cameraMotion.Distance;
            touchInputBehaviour.OnDragging += TouchInputBehaviourOnDragging;
            touchInputBehaviour.OnPush += TouchInputBehaviourOnPush;
            touchInputBehaviour.OnFinishDragging += TouchInputBehaviourOnFinishDragging;
        }
        
        public void StopMovement()
        {
            currentSpeed = Vector2.zero;
        }

        private void Update()
        {
            if (IsActive == false)
            {
                return;
            }
            UpdateInput();
        }

        public void UpdateInput()
        {
            MoveCamera();
            
            outOfPosition = IsOutOfPosition();
            zoomDelta = 0f;
            
            ReadTouchInput();
            ReadMouseInput();
            SetCurrentSpeedDirection();
            SetTargetCameraDistance();
        }

        private void MoveCamera()
        {
            cameraMotion.Distance = Mathf.Lerp(cameraMotion.Distance, targetCameraDistance, Time.deltaTime * dragFriction);
            cameraMotion.Move(currentSpeed);
            cameraMotion.ForceUpdate();
            currentSpeed = Vector2.Lerp(currentSpeed, Vector2.zero, dragFriction * Time.deltaTime);
        }

        private void ReadMouseInput()
        {
            if(SystemInfo.deviceType == DeviceType.Handheld){return;}
            
            currentDragDirection = Vector2.zero;
            zoomDelta = Input.mouseScrollDelta.y;
            if (Input.GetMouseButton(0) && outOfPosition == false)
            {
                currentDragDirection = mouseInputBehaviour.CurrentPointerPosition - lastMousePosition;
            }
            lastMousePosition = Input.mousePosition;
        }
        private void ReadTouchInput()
        {
            if (Input.touchCount > 0)
            {
                if (Input.touchCount > 1)
                {
                    zoomDelta = touchInputBehaviour.PinchDelta / 8f;
                    currentDragDirection = Vector2.zero; 
                    lastTouchPosition = Vector2.zero;
                }
            }
        }
        private void TouchInputBehaviourOnDragging(PointerInputBehaviour input)
        {
            if (Input.touchCount == 1)
            {
                currentDragDirection = touchInputBehaviour.CurrentPointerPosition - lastTouchPosition;
            }
            lastTouchPosition = touchInputBehaviour.CurrentPointerPosition;
        }
        private void TouchInputBehaviourOnFinishDragging(PointerInputBehaviour input)
        {
            currentDragDirection = Vector2.zero;
        }
        private void TouchInputBehaviourOnPush(PointerInputBehaviour asset)
        {
            lastTouchPosition = touchInputBehaviour.CurrentPointerPosition;
        }
        private bool IsOutOfPosition()
        {
            if (inputUIs.Any())
            {
                if (SystemInfo.deviceType == DeviceType.Handheld)
                {
                    if (Input.touchCount > 0)
                    {
                        outOfPosition = currentEventSystem.IsPointerOverGameObject(Input.GetTouch(0).fingerId) == false;
                    }
                }
                else
                {
                    outOfPosition = currentEventSystem.IsPointerOverGameObject() == false;
                }
            }
            else
            {
                outOfPosition = currentEventSystem.IsPointerOverGameObject();
            }
            return outOfPosition;
        }
        private void SetTargetCameraDistance()
        {
            targetCameraDistance -= zoomDelta * zoomSpeed * (Input.touchCount == 2 && outOfPosition == false ? 0.01f : 1f);
            targetCameraDistance =
                Mathf.Clamp(targetCameraDistance, cameraMotion.MinDistance, cameraMotion.MaxDistance);
        }

        private void SetCurrentSpeedDirection()
        {
            if (currentDragDirection == Vector2.zero) return;
            
            var velocity = DragVelocityMultiplier * dragVelocity * Time.deltaTime;
            currentSpeed.x = Mathf.Min(currentSpeed.x + currentDragDirection.x * velocity, maxDragSpeed);
            currentSpeed.y = Mathf.Min(currentSpeed.y + currentDragDirection.y * velocity, maxDragSpeed);
        }
        
    }
}