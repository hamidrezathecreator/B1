using System;
using Localization.Base.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Localization.Base.ValueObjects
{
    [Serializable]
    public class AlphabeticFontItem
    {
        public LanguageAlphabetic Alphabetic;
        public Font Font;
        public TMP_FontAsset FontAsset;

        public void SetFont(TextMeshProUGUI tmpText)
        {
            tmpText.font = FontAsset;
        }

        public void SetFont(Text text)
        {
            text.font = Font;
        }
    }
}