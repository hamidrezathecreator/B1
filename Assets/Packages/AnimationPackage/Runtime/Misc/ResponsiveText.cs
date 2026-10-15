using System;
using UnityEngine;
using UnityEngine.UI;

namespace Animation.Misc
{
    [RequireComponent(typeof(Text))]
    public class ResponsiveText : ResponsiveTextBase
    {
        private Text text;

        protected override void SpecificInitialize()
        {
            text = GetComponent<Text>();
            text.resizeTextForBestFit = false;
        }

        protected override void ApplyFontSize(int calculatedFontSize)
        {
            text.fontSize = calculatedFontSize;
        }
    }
}