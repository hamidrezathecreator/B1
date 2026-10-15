using System.Collections;
using UnityEngine;

namespace Animation.InteractionHelpers
{
    public abstract class PointerInputBehaviour : MonoBehaviour
    {
        private const float DragThresholdPercentage = 0.7f; 
        private Vector2 startDragPosition;
        private bool isPointerDown = false;
        private bool isHover = false;
        private bool interactable = true;
        private Coroutine pointerEnterRoutine;
        private Coroutine dragCoroutine;
        public bool IsDragging { get; private set; }
        public event PointerInputDelegate OnHover;
        public event PointerInputDelegate OnBlur;
        public event PointerInputDelegate OnClick;
        public event PointerInputDelegate OnStartDragging;
        public event PointerInputDelegate OnDragging;
        public event PointerInputDelegate OnFinishDragging;
        public event PointerInputDelegate OnPush;
        public event PointerInputDelegate OnRelease;
        public delegate void PointerInputDelegate(PointerInputBehaviour asset);
        public abstract bool IsPointerBlocked { get; }

        public bool Interactable
        {
            get => interactable;
            set
            {
                if (interactable == value)
                {
                    return;
                }
                interactable = value;
                if (interactable == false)
                {
                    if (dragCoroutine != null)
                    {
                        StopCoroutine(dragCoroutine);
                    }
                    isPointerDown = false;
                    isHover = false;
                    IsDragging = false;
                }
            }
        }

        public static float DragThreshold
        {
            get
            {
                var drag = new Vector2(Screen.width / DragThresholdPercentage * .01f, Screen.height / DragThresholdPercentage * .01f);
                return drag.magnitude;
            }
        }

        public abstract Vector2 CurrentPointerPosition { get; }

        public float CurrentDragDelta =>  CurrentPointerPosition.magnitude - startDragPosition.magnitude;
        public Vector2 CurrentDragDirection => CurrentPointerPosition - startDragPosition;

        protected void EnterPointer()
        {
            if (pointerEnterRoutine != null)
            {
                StopCoroutine(pointerEnterRoutine);
            }
            if (Interactable == false)
            {
                return;
            }
            pointerEnterRoutine = StartCoroutine(PointerEnterRoutine());
            
        }

        private IEnumerator PointerEnterRoutine()
        {
            while (IsPointerBlocked)
            {
                yield return null;
            }

            isHover = true;
            OnHover?.Invoke(this);
            OnHoverAction();

        }

        protected void ExitPointer()
        {
            if (pointerEnterRoutine != null)
            {
                StopCoroutine(pointerEnterRoutine);
            }
            if (Interactable == false)
            {
                return;
            }

            if (isHover == false)
            {
                return;
            }

            isHover = true;
            OnBlur?.Invoke(this);
            OnBlurAction();
        }

        protected void PushDownPointer()
        {
            if (Interactable == false)
            {
                return;
            }
            if (IsPointerBlocked)
            {
                return;
            }
            startDragPosition = CurrentPointerPosition;
            isPointerDown = true;
            OnPush?.Invoke(this);
            OnPushAction();
            dragCoroutine = StartCoroutine(DraggingRoutine());
        }

        protected void ReleaseUpPointer()
        {
            if (Interactable == false)
            {
                return;
            }

            if (isPointerDown == false)
            {
                return;
            }
            isPointerDown = false;
            
           // if (IsPointerBlocked)
           // {
           //     return;
           // }

            float dragDelta = Mathf.Abs(CurrentDragDelta);
            if (dragDelta <= DragThreshold)
            {
                OnClick?.Invoke(this);
                OnClickAction();
            }
            OnRelease?.Invoke(this);
            OnReleaseAction();
        }

        private IEnumerator DraggingRoutine()
        {
            bool isDraggingStarted = false;
            while (isPointerDown)
            {
                if (isDraggingStarted)
                {
                    OnDragging?.Invoke(this);
                    OnDraggingAction();   
                }
                else
                {
                    float dragDelta = Mathf.Abs(CurrentDragDelta);

                    isDraggingStarted = dragDelta > DragThreshold;
                    if (isDraggingStarted)
                    {
                        IsDragging = true;
                        OnStartDragging?.Invoke(this);
                        OnStartDraggingAction();
                    }
                }
                yield return null;
            }

            if (isDraggingStarted)
            {
                IsDragging = false;
                OnFinishDragging?.Invoke(this);
                OnFinishDraggingAction();
            }
        }

        protected virtual void OnHoverAction(){}
        protected virtual void OnBlurAction(){}
        protected virtual void OnClickAction(){}
        protected virtual void OnStartDraggingAction(){}
        protected virtual void OnDraggingAction(){}
        protected virtual void OnFinishDraggingAction(){}
        protected virtual void OnPushAction(){}
        protected virtual void OnReleaseAction(){}
    }
}
