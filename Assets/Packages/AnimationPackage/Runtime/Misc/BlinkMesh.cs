using Animation.Animations;
using Animation.Base;
using UnityEngine;

namespace Animation.Misc
{
    public class BlinkMesh : MonoBehaviour
    {

        private MeshColorAnimation _meshAnimation;
        private MeshColorAnimation meshAnimation
        {
            get
            {
                if (_meshAnimation != null)
                {
                    return _meshAnimation;
                }
                _meshAnimation = GetComponent<MeshColorAnimation>();
                if (_meshAnimation != null)
                {
                    return _meshAnimation;
                }
                _meshAnimation = gameObject.AddComponent<MeshColorAnimation>();
                _meshAnimation.From = Color.white;
                _meshAnimation.To = new Color(1f, 0.223f, 0.356f);
                _meshAnimation.Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                _meshAnimation.Duration = 0.6f;
                _meshAnimation.SetPlayOrder(PlayOrder.PingPong);
                return _meshAnimation;
            }
        }
        public void StartBlinking()
        {
            meshAnimation.Execute();
        }
        public void StopBlinking()
        {
            meshAnimation.Stop();
            meshAnimation.Play(AnimationDirection.Backward, true);
        }
    }
}
