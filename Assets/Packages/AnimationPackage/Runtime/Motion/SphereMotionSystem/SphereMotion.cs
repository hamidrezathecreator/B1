using UnityEngine;
using UnityEngine.Serialization;

namespace Animation.Motion.SphereMotionSystem
{
    public class SphereMotion : MonoBehaviour
    {
        [SerializeField][FormerlySerializedAs("Progress")] private Vector2 progress = Vector2.one * 0.5f;
        [SerializeField] [FormerlySerializedAs("ProgressLimit")]private Vector4 progressLimit = new Vector4(0f, 1f, 0f, 1f);
        protected Transform thisTransform { get; private set; }
        [SerializeField] private Vector3 size = Vector3.one;
        [SerializeField] private Transform target;
        [SerializeField] private float distance = 0f;
        [SerializeField] private float minDistance = 0f;
        [SerializeField] private float maxDistance = 1f;
        [SerializeField] private float defaultDistance = 0.5f;
        public Vector3 Size { get => size; set => size = value; }
        public Transform Target
        {
            get => target;
            set
            {
                target = value;
                UpdateMotion(progress);
            }
        }
        public float Distance
        {
            get => distance;
            set
            {
                distance = Mathf.Clamp(value, minDistance, maxDistance);
                UpdateMotion(progress);
                OnDistanceChange?.Invoke(distance);
            }
        }
        public float MinDistance { get => minDistance; set => minDistance = value; }
        public float MaxDistance { get => maxDistance; set => maxDistance = value; }
        public float DefaultDistance { get => defaultDistance; set => defaultDistance = value; }

        public DistanceChangeDelegate OnDistanceChange { get; set; }
        public delegate void DistanceChangeDelegate(float distance);
        public Vector4 ProgressLimit
        {
            get => progressLimit;
            set
            {
                if (value == progressLimit)
                {
                    return;
                }
                progressLimit = value;
                ClampProgressLimit();
                ClampProgress();
                UpdateMotion(progress);
            }
        }
        
        public Vector2 Progress
        {
            get => progress;
            set
            {
                if (progress == value)
                {
                    return;
                }
                progress = value;
                ClampProgress();
                UpdateMotion(progress);
            }
        }

        private void Awake()
        {
            thisTransform = transform;
            distance = defaultDistance;
        }
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorApplication.isPlaying)
            {
                return;
            }
            minDistance = Mathf.Max(0f, minDistance);
            maxDistance = Mathf.Max(0f, maxDistance);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            defaultDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
            ClampProgressLimit();
            ClampProgress();
            UpdateMotion(Progress);
        }
#endif

        private void ClampProgressLimit()
        {
            progressLimit.x = Mathf.Clamp01(progressLimit.x);
            progressLimit.y = Mathf.Clamp01(progressLimit.y);
            progressLimit.z = Mathf.Clamp01(progressLimit.z);
            progressLimit.w = Mathf.Clamp01(progressLimit.w);
        }
        
        private void ClampProgress()
        {
            progress = ClampProgress(progress);
        }

        private Vector2 ClampProgress(Vector2 p)
        {
            p.x = Mathf.Clamp(p.x, ProgressLimit.x, ProgressLimit.y);
            p.y = Mathf.Clamp(p.y, ProgressLimit.z, ProgressLimit.w);
            return p;
        }

        public void Move(Vector2 direction)
        {
            var targetProgress = Progress;
            targetProgress.x = (float)(Progress.x + direction.x) % 1f;
            targetProgress.y = (float)(Progress.y + direction.y) % 1f;
            if (targetProgress.x < 0f)
            {
                targetProgress.x = Mathf.Abs(1f - Progress.x);
            }
            if (targetProgress.y < 0f)
            {
                targetProgress.y = Mathf.Abs(1f - Progress.y);
            }
            Progress = targetProgress;
        }

        public void UpdateMotion()
        {
            ClampProgressLimit();
            ClampProgress();
            UpdateMotion(Progress);
        }

        public void ForceUpdate()
        {
            UpdateMotion(Progress);
        }

        private void UpdateMotion(Vector2 progress)
        {
            Transform targetTransform = thisTransform != null ? thisTransform : transform;
            Vector3 targetPosition = ProgressToPosition(progress, distance);
            if (target != null)
            {
                targetTransform.position = target.position + targetPosition;
            }
            else
            {
                targetTransform.localPosition = targetPosition;
            }
        }

        public Vector3 ProgressToPosition(Vector2 progressToConvert, float distance)
        {
            Vector2 angleInDeg = ProgressToRotation(progressToConvert);
            Vector2 angle = Mathf.Deg2Rad * angleInDeg;
            Vector3 pos = new Vector3(
                Mathf.Sin(angle.x) * Mathf.Cos(angle.y),
                Mathf.Sin(angle.y),
                Mathf.Cos(angle.y) * Mathf.Cos(angle.x));
            var distanceMultiplier = Mathf.Clamp(distance, minDistance, maxDistance)  + 1;
            Vector3 finalPos = new Vector3(pos.x * size.x * distanceMultiplier, pos.y * size.y * distanceMultiplier, pos.z * size.z * distanceMultiplier);
            return finalPos;
        }

        public Vector2 ProgressToRotation(Vector2 progressToConvert)
        {
            return progressToConvert * 360f;
        }

        public Vector2 RotationToProgress(Vector2 rotationToConvert)
        {
            return new Vector2(rotationToConvert.x % 360f, rotationToConvert.y % 360f) / 360f;
        }

        //private void OnDrawGizmos()
        //{
        //    //Transform targetTransform = thisTransform != null ? thisTransform : transform;
        //    Vector2 angleInDeg = 360f * Progress;
        //    Vector2 angle = Mathf.Deg2Rad * angleInDeg;
        //    Vector3 hPos = new Vector3(
        //        Mathf.Sin(angle.x),
        //        0f,
        //        Mathf.Cos(angle.x));
        //    Vector3 pos = new Vector3(
        //        Mathf.Sin(angle.x) * Mathf.Cos(angle.y),
        //        Mathf.Sin(angle.y),
        //        Mathf.Cos(angle.y) * Mathf.Cos(angle.x));
        //    Gizmos.color = Color.yellow;
        //    Gizmos.DrawSphere(hPos, 0.1f);
        //    Gizmos.color = Color.blue;
        //    Gizmos.DrawSphere(pos, 0.1f);
        //}
    }
}
