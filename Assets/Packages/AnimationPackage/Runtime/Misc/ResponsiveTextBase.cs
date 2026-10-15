using System;
using UnityEngine;
using UnityEngine.UI;

namespace Animation.Misc
{
#if UNITY_EDITOR
    [ExecuteInEditMode]
#endif
    public abstract class ResponsiveTextBase : MonoBehaviour
    {
        [SerializeField] private Direction baseDirection = Direction.Horizontal;
        [SerializeField][Range(0f, 1f)] private float fontSizeScale = 0.07f;
        [SerializeField] private int minFontSize = 2;
        [SerializeField] private int maxFontSize = 200;
        private RectTransform rect;
        private bool isInitialized = false;

        private void Start()
        {
            Initialize();
            UpdateFontSize();
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (UnityEditor.EditorApplication.isPlaying)
            {
                return;
            }
            Initialize();
            UpdateFontSize();
        }
#endif

        private void OnRectTransformDimensionsChange()
        {
            if (isInitialized == false)
            {
                return;
            }
            UpdateFontSize();
        }

        private void Initialize()
        {
            if (isInitialized)
            {
#if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPlaying == false)
                {
                    return;
                }
#endif
                Debug.LogError("You cannot initialize responsive text twice.");
                return;
            }
            rect = GetComponent<RectTransform>();
            SpecificInitialize();
            isInitialized = true;
        }

        public void UpdateFontSize()
        {
            float textBoxLength = 0f;
            switch (baseDirection)
            {
                case Direction.Horizontal:
                    textBoxLength = rect.rect.width;
                    break;;
                case Direction.Vertical:
                    textBoxLength = rect.rect.height;
                    break;
            }
            int calculatedFontSize = Mathf.RoundToInt(fontSizeScale * textBoxLength);
            calculatedFontSize = Mathf.Max(Mathf.Min(calculatedFontSize, maxFontSize), minFontSize);
            ApplyFontSize(calculatedFontSize);
        }
        
        protected abstract void SpecificInitialize();

        protected abstract void ApplyFontSize(int calculatedFontSize);

        private enum Direction
        {
            Horizontal = 0,
            Vertical = 1
        }
    }
}