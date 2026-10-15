using Animation.Base;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Animation.Animations
{
    [RequireComponent(typeof(TMP_Text))]
    public class TextMeshUIColorAnimation : BaseAnimation
    {
        public Color From;
        public Color To;
        private TMP_Text textUi;
        private Color currentColor;

        protected override void Initialize()
        {
            textUi = GetComponent<TMP_Text>();
        }

        protected override void Animate(float t)
        {
            currentColor = Color.Lerp(From, To, t);
            textUi.color = currentColor;
        }
    }
}
