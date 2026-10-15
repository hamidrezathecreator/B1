using Animation.Base;
using UnityEngine;

namespace Animation.Animations
{
    public class MeshColorAnimation : BaseAnimation
    {
        public Color From;
        public Color To;
        private Material material;
        private Color currentColor;
        public bool CreateTempMaterial = true;
        public string shaderColorName;

        protected override void Initialize()
        {
            var meshRenderer = GetComponentInChildren<MeshRenderer>();
            if (CreateTempMaterial)
            {
                material = Instantiate(meshRenderer.material);
                meshRenderer.material = material;
            }
            else
            {
                material = meshRenderer.material;
            }
        }

        protected override void Animate(float t)
        {
            currentColor = Color.Lerp(From, To, t);
            if (!string.IsNullOrEmpty(shaderColorName))
                material.SetColor(shaderColorName, currentColor);
            else
                material.color = currentColor;
        }
    }
}
