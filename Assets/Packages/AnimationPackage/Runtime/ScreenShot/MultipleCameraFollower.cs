using System.Collections.Generic;
using UnityEngine;

namespace Animation.ScreenShot
{
    public class MultipleCameraFollower : MonoBehaviour
    {
        [SerializeField] private List<Transform> targets = new List<Transform>();
        [SerializeField] private Vector3 offset;
        [SerializeField] private float minZoom = 40f;
        [SerializeField] private float maxZoom = 10f;
        [SerializeField] private float zoomLimiter = 50f;
        [SerializeField] private Camera cam;
        private Vector3 velocity;

        public void FitObjectsToCameraView(List<GameObject> objects)
        {
            targets.Clear();
            foreach (var obj in objects)
            {
                targets.Add(obj.transform);
            }
            if(targets.Count == 0){return;}
            Move();
            Zoom();
        }
        private void Zoom()
        {
            var newZoom = Mathf.Lerp(maxZoom, minZoom, GetGreatestDistance() / zoomLimiter);
            cam.fieldOfView = newZoom; 
            //Mathf.Lerp(cam.fieldOfView,newZoom,Time.deltaTime);
        }

        private void Move()
        {
            var centerPoint = GetCenterPoint();
            var newPos = centerPoint + offset;
            transform.position = newPos; //Vector3.SmoothDamp(transform.position, newPos,ref velocity,smoothTime);
        }

        private Vector3 GetCenterPoint()
        {
            if (targets.Count == 1)
            {
                return targets[0].position;
            }

            var bounds = new Bounds(targets[0].position, Vector3.zero);
            foreach (var t in targets)
            {
                bounds.Encapsulate(t.position);
            }

            return bounds.center;
        }

        private float GetGreatestDistance()
        {
            var bounds = new Bounds(targets[0].position, Vector3.zero);
            foreach (var t in targets)
            {
                bounds.Encapsulate(t.position);
            }

            return bounds.size.x;
        }

    }
}