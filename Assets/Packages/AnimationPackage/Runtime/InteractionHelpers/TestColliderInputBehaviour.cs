using UnityEngine;

namespace Animation.InteractionHelpers
{
    public class TestColliderInputBehaviour : ColliderInputBehaviour
    {
        protected override void OnHoverAction()
        {
            Debug.LogWarning("Hover");
        }

        protected override void OnBlurAction()
        {
            Debug.LogWarning("Blur");
        }

        protected override void OnClickAction()
        {
            Debug.LogWarning("Click");
        }

        protected override void OnDraggingAction()
        {
            Debug.LogWarning("Dragging " + Time.time);
        }

        protected override void OnPushAction()
        {
            Debug.LogWarning("Push");
        }

        protected override void OnReleaseAction()
        {
            Debug.LogWarning("Release");
        }

        protected override void OnStartDraggingAction()
        {
            Debug.LogWarning("Dragging Started");
        }

        protected override void OnFinishDraggingAction()
        {
            Debug.LogWarning("Dragging Finished");
        }
    }
}