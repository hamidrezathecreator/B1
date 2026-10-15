using System.Collections.Generic;
using UnityEngine;

namespace Animation.ScreenShot
{
    [DefaultExecutionOrder(-5000)]
    public class ScreenShotGetter : MonoBehaviour
    {
        [SerializeField] private Camera snapCam;
        [SerializeField] private float distanceThreshold = 2;
        [SerializeField] private Transform assetPlace;
        [SerializeField] private float assetYPos = 0;
        [SerializeField] private GameObject holder;
        [SerializeField] private MultipleCameraFollower multipleCameraFollower;

        private int resWidth = 512;
        private int resHeight = 512;
        private Vector3 initialPos;
        private Quaternion initialRot;
        private float initialFov = 0;

        private void Awake()
        {
            initialPos = snapCam.transform.position;
            initialRot = snapCam.transform.rotation;
            initialFov = snapCam.fieldOfView;
            
            if (snapCam.targetTexture == null)
            {
                snapCam.targetTexture = new RenderTexture(resWidth,resHeight,24);
            }
            else
            {
                resWidth = snapCam.targetTexture.width;
                resHeight = snapCam.targetTexture.height;
            }
        }

        public Texture2D GetScreenShot(Vector3 position,Vector3 rotation)
        {
            return TakeSnapshot( position, rotation);
        }
        
        public Texture2D GetScreenShot(GameObject asset,Vector3 position,Vector3 rotation,bool autoLookAt)
        {
            return TakeSnapshot(asset,rotation, position,autoLookAt);
        }
        public Texture2D GetScreenShot(List<GameObject> assets,Vector3 rotation)
        {
            return TakeSnapshot(assets,rotation);
        }
        private Texture2D TakeSnapshot(List<GameObject> assets,Vector3 rotation)
        {
            snapCam.gameObject.SetActive(true);
            snapCam.transform.eulerAngles = rotation;

            multipleCameraFollower.FitObjectsToCameraView(assets);
            var snapShot = new Texture2D(resWidth,resHeight,TextureFormat.RGBA32,false);
            snapCam.Render();
            RenderTexture.active = snapCam.targetTexture;
            
            snapShot.ReadPixels(new Rect(0, 0,resWidth,resHeight),0,0);
            snapShot.Apply();
            
            snapCam.gameObject.SetActive(false);
            snapCam.transform.position = initialPos;
            snapCam.transform.rotation = initialRot;
            snapCam.fieldOfView = initialFov;

            return snapShot;
        }
        private Texture2D TakeSnapshot(GameObject asset, Vector3 rotation = default, Vector3 position = default,bool autoLookAt = false)
        {
            snapCam.gameObject.SetActive(true);
            var assetReplica = Instantiate(asset,assetPlace);
            assetReplica.transform.localPosition = Vector3.zero;
            assetPlace.parent = holder.transform;
            assetPlace.localPosition = position != default ? position : Vector3.zero;
            
            if (rotation != default)
            {
                assetPlace.transform.eulerAngles = rotation;
            }
            else
            {
                assetPlace.transform.eulerAngles = Vector3.zero;
            }
            PositionAsset(assetReplica);
            if (autoLookAt)
            {
                snapCam.transform.LookAt(assetReplica.transform);
                assetReplica.transform.LookAt(snapCam.transform);
            }
            
            Texture2D snapShot = new Texture2D(resWidth,resHeight,TextureFormat.RGBA32,false);
            snapCam.Render();
            RenderTexture.active = snapCam.targetTexture;
            snapShot.ReadPixels(new Rect(0, 0,resWidth,resHeight),0,0);
            snapShot.Apply();
            DestroyImmediate(assetReplica,true);
            
            holder.transform.localPosition = Vector3.zero;
            snapCam.transform.position = initialPos;
            snapCam.transform.rotation = initialRot;
            snapCam.gameObject.SetActive(false);
            snapCam.fieldOfView = initialFov;
            
            return snapShot;
        }
        private Texture2D TakeSnapshot(Vector3 position,Vector3 rotation)
        {
            snapCam.gameObject.SetActive(true);
            snapCam.transform.position = position;
            snapCam.transform.eulerAngles = rotation;
            Texture2D snapShot = new Texture2D(resWidth,resHeight,TextureFormat.RGBA32,false);
            snapCam.Render();
            RenderTexture.active = snapCam.targetTexture;
            
            snapShot.ReadPixels(new Rect(0, 0,resWidth,resHeight),0,0);
            snapShot.Apply();
            
            snapCam.gameObject.SetActive(false);
            snapCam.transform.position = initialPos;
            snapCam.transform.rotation = initialRot;
            snapCam.fieldOfView = initialFov;

            return snapShot;
        }

        private void PositionAsset(GameObject go)
        {
            Bounds b = GetBound(go);
            Vector3 max = b.size;
            float distance = Mathf.Max(max.x, max.y, max.z);
            distance /= (distanceThreshold *  Mathf.Tan( 0.5f * snapCam.fieldOfView * Mathf.Deg2Rad));
            holder.transform.localPosition = new Vector3(0,-b.extents.y + assetYPos,
                distance * 2f);  
        }
        private Bounds GetBound(GameObject go)
        {
            Bounds b = new Bounds(go.transform.position, Vector3.zero);        
            var rList = go.GetComponentsInChildren(typeof(Renderer));
            foreach (Renderer r in rList)
            {
                b.Encapsulate(r.bounds);
            }
            return b;
        }
        private Bounds CalculateSize(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>();

            Bounds size = new Bounds(colliders[0].bounds.center, Vector3.zero);
            
            foreach (var collider in colliders)
            {
                size.Encapsulate(collider.bounds);
            }
            return size;
        }

    }
}