using UnityEngine;

namespace Animation.InteractionHelpers
{
    public class TouchInputBehaviour : PointerInputBehaviour
    {
        public override bool IsPointerBlocked => false;
        private bool justClicked;
        private bool isHandheldDevice;

        public override Vector2 CurrentPointerPosition =>
            Input.touches.Length > 0 ? Input.touches[Input.touches.Length - 1].position : Vector2.zero;

        public float PinchDelta
        {
            get
            {
                if (isHandheldDevice == false) { return 0;}
                if (Input.touchCount < 2) { return 0f;}
                
                var touch1 = Input.GetTouch(0);
                var touch2 = Input.GetTouch(1);
                var touchPrevPos1 = touch1.position - touch1.deltaPosition;
                var touchPrevPos2 = touch2.position - touch2.deltaPosition;
                var prevMagnitude = (touchPrevPos1 - touchPrevPos2).magnitude;
                var currentMagnitude = (touch1.position - touch2.position).magnitude;
                return currentMagnitude - prevMagnitude;
            }   
        }
        private void Awake()
        {
           isHandheldDevice = SystemInfo.deviceType == DeviceType.Handheld;
        }
        protected void Update()
        {
            if (isHandheldDevice == false) { return;}
            if (Input.touchCount == 1 && justClicked == false)
            {
                PushDownPointer();
                justClicked = true;
            }

            if ((Input.touchCount == 0 || Input.touchCount > 1) && justClicked)
            {
                ReleaseUpPointer();
                justClicked = false;
            }
        }
    }
}
