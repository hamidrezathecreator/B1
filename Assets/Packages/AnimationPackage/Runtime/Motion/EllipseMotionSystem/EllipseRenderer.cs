using UnityEngine;

namespace Animation.Motion.EllipseMotionSystem
{
    [RequireComponent(typeof(LineRenderer))]
    public class EllipseRenderer : MonoBehaviour
    {
        [Range(3, 36)]
        [SerializeField]
        private int segments;
        [SerializeField]
        private Ellipse ellipse;
        private LineRenderer lr;

        private void Awake()
        {
            lr = GetComponent<LineRenderer>();
            CalculateEllipse();
        }

        private void CalculateEllipse()
        {
            Vector3[] points = new Vector3[segments + 1];
            for (int i = 0; i < segments; i++)
            {
                var angle = ((float)i / (float)segments);
                points[i] = ellipse.Evaluate(angle);
            }
            points[segments] = points[0];
            lr.positionCount = segments + 1;
            lr.SetPositions(points);
        }

        private void OnValidate()
        {
            if (Application.isPlaying && lr != null)
                CalculateEllipse();
        }
    }
}
