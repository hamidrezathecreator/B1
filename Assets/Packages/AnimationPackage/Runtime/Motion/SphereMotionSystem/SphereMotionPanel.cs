using UnityEngine;
using UnityEngine.EventSystems;

namespace Animation.Motion.SphereMotionSystem
{
    [DisallowMultipleComponent]
    public class SphereMotionPanel : UIBehaviour, IDragHandler
    {
        [SerializeField]
        private SphereMotion cameraMotion;
        [SerializeField]
        [Range(1f, 20f)]
        private float dragVelocity = 4f;
        [SerializeField]
        [Range(1f, 20f)]
        private float maxDragSpeed = 10f;
        [SerializeField]
        [Range(1f, 20f)]
        private float dragFriction = 5f;
        private Vector2 currentSpeed;
        private const float dragVelocityMultiplier = 0.001f;


        private void Update()
        {
            cameraMotion.Move(currentSpeed);
            currentSpeed = Vector2.Lerp(currentSpeed, Vector2.zero, dragFriction * Time.deltaTime);
        }

        public void OnDrag(PointerEventData eventData)
        {
            float velocity = dragVelocityMultiplier * dragVelocity * Time.deltaTime;
            currentSpeed.x = Mathf.Min(currentSpeed.x + eventData.delta.x * velocity, maxDragSpeed);
            currentSpeed.y = Mathf.Min(currentSpeed.y + eventData.delta.y * velocity, maxDragSpeed);
        }
    }

}