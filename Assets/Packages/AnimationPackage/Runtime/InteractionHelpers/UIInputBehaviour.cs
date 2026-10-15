using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Animation.InteractionHelpers
{
    [RequireComponent(typeof(Selectable))]
    public class UIInputBehaviour : PointerInputBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public override Vector2 CurrentPointerPosition => Input.mousePosition;


        public void OnPointerEnter(PointerEventData eventData)
        {
            EnterPointer();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ExitPointer();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            PushDownPointer();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            StartCoroutine(ReleaseUpRoutine());
        }

        private IEnumerator ReleaseUpRoutine()
        {
            while (Input.GetMouseButton(0))
            {
                yield return null;
            }
            ReleaseUpPointer();
        }

        public override bool IsPointerBlocked => false;
    }
}