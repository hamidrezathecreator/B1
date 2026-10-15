using UnityEngine;

namespace Animation.InteractionHelpers
{
    [RequireComponent(typeof(UIInputBehaviour))]
    public abstract class UIAssetDragHandlerBase : MonoBehaviour
    {
        protected virtual void Start()
        {
            UIInputBehaviour inputBehaviour = GetComponent<UIInputBehaviour>();

            inputBehaviour.OnStartDragging += StartDrag;
            inputBehaviour.OnDragging += UpdateDrag;
            inputBehaviour.OnFinishDragging += FinishDrag;
        }
        
        private void StartDrag(PointerInputBehaviour pointerInput)
        {
            StartDrag(pointerInput.CurrentPointerPosition);
        }

        private void UpdateDrag(PointerInputBehaviour pointerInput)
        {
            UpdateDrag(pointerInput.CurrentPointerPosition);
        }
        private void FinishDrag(PointerInputBehaviour pointerInput)
        {
            FinishDrag(pointerInput.CurrentPointerPosition);
        }
        protected abstract void StartDrag(Vector2 pointerPosition);
        protected abstract void UpdateDrag(Vector2 pointerPosition);
        protected abstract void FinishDrag(Vector2 pointerPosition);
    }
}