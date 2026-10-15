using System.Collections.Generic;
using System.Text.RegularExpressions;
using Localization.Base.Enums;
using Localization.EGGA.UPersian.Scripts.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Localization.Base
{
    public class LocalizeLegacyText : Text,ILocalizeText
    {
        [SerializeField] private string localizationKey;
        protected static char LineEnding = '\n';
        [SerializeField] private bool dontChangeAlignment = true;
        [SerializeField] private bool dontChangeFont = false;
        
        public string LocalizationKey
        {
            get => localizationKey;
            set => localizationKey = value;
        }
        public bool DontChangeAlignment
        {
            get => dontChangeAlignment;
            set => dontChangeAlignment = value;
        }

        public bool DontChangeFont
        {
            get => dontChangeFont;
            set => dontChangeFont = value;
        }
        
        /// <summary>
        /// Original text which user sets via editor.
        /// You sould use this value if you need the original string. (to use in a third-party)
        /// </summary>
        public string BaseText
        {
            get { return base.m_Text; }
        }

        /// <summary>
        /// get: Return RTL fixed string
        /// set: Sets base.text
        /// </summary>
        public override string text
        {
            get
            {
                Regex regex = new Regex("[\u0600-\u06ff]|[\u0750-\u077f]|[\ufb50-\ufc3f]|[\ufe70-\ufefc]");
                if (regex.IsMatch(m_Text) == false)
                {
                    return m_Text;
                }
                var rtlFixedText = RtlFixString(m_Text,
                    cachedTextGenerator, rectTransform,
                    GetGenerationSettings(rectTransform.rect.size));
                return rtlFixedText;
            }
            set
            {
                if (m_Text != value)
                {
                    base.text = value;
                }
            }
        }

       

        public void UpdateTextLocalization()
        {
            if (LocalizationAccessor.Localization == null)
            {
                return;
            }

            if (!dontChangeFont && !LocalizationAccessor.Localization.StaticFont)
            {
                LocalizationAccessor.Localization.CurrentFont.SetFont(this);
            }
            if (string.IsNullOrEmpty(localizationKey) == false)
            {
                text = LocalizationAccessor.Localization.GetText(localizationKey);
            }
            if (dontChangeAlignment == false &&
                alignment != TextAnchor.LowerCenter &&
                alignment != TextAnchor.MiddleCenter &&
                alignment != TextAnchor.UpperCenter)
            {
                if (LocalizationAccessor.Localization.CurrentDirection == LanguageDirection.Ltr)
                {
                    if (alignment == TextAnchor.LowerRight)
                    {
                        alignment = TextAnchor.LowerLeft;
                    }
                    else if (alignment == TextAnchor.MiddleRight)
                    {
                        alignment = TextAnchor.MiddleLeft;
                    }
                    else if (alignment == TextAnchor.UpperRight)
                    {
                        alignment = TextAnchor.UpperLeft;
                    }
                }
                else
                {
                    if (alignment == TextAnchor.LowerLeft)
                    {
                        alignment = TextAnchor.LowerRight;
                    }
                    else if (alignment == TextAnchor.MiddleLeft)
                    {
                        alignment = TextAnchor.MiddleRight;
                    }
                    else if (alignment == TextAnchor.UpperLeft)
                    {
                        alignment = TextAnchor.UpperRight;
                    }
                }
            }
            SetAllDirty();
        }
        protected override void Start()
        {
            base.Start();
            if (Application.isPlaying == false)
            {
                return;
            }
            if (LocalizationAccessor.Localization != null)
            {
                LocalizationAccessor.Localization.RegisterLabel(this);
                SetAllDirty();
                UpdateTextLocalization();
            }
        }
        protected override void OnEnable()
        {
            base.OnEnable();
            if (Application.isPlaying == false)
            {
                return;
            }
            if (LocalizationAccessor.Localization != null)
            {
                LocalizationAccessor.Localization.RegisterLabel(this);
                UpdateTextLocalization();
            }
        }
        protected override void OnDisable()
        {
            base.OnDisable();
            if (Application.isPlaying == false)
            {
                return;
            }
            if (LocalizationAccessor.Localization != null)
            {
                LocalizationAccessor.Localization.UnregisterLabel(this);
            }
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Application.isPlaying == false)
            {
                return;
            }
            if (LocalizationAccessor.Localization != null)
            {
                LocalizationAccessor.Localization.UnregisterLabel(this);
            }
        }
#if UNITY_EDITOR
        protected override void OnValidate()
        {
            UpdateTextLocalization();
            base.OnValidate();
        }
#endif

        private static string RtlFixString(string text, TextGenerator textGenerator, RectTransform rectTransform, TextGenerationSettings generationSettings)
        {
            // Populate base text in rect transform and calculate number of lines.
            string newText = text;
            newText = newText.Replace("ی ", "ى ").Replace("ی\n", "ى\n").Replace("ی.", "ى.").Replace("ی,", "ى,").Replace("ی;", "ى;").Replace("ی،", "ى،").Replace("ی؛", "ى؛").Replace("ی؟", "ى؟").Replace("ی?", "ى?");
            newText = newText.Replace("ي ", "ى ").Replace("ي\n", "ى\n").Replace("ي.", "ى.").Replace("ي,", "ى,").Replace("ي;", "ى;").Replace("ي،", "ى،").Replace("ي؛", "ى؛").Replace("ي؟", "ى؟").Replace("ي?", "ى?");
            newText = newText.Replace("ی ", "ى ").Replace("ی\n", "ى\n").Replace("ی.", "ى.").Replace("ی,", "ى,").Replace("ی;", "ى;").Replace("ی،", "ى،").Replace("ی؛", "ى؛").Replace("ي؟", "ى؟").Replace("ي?", "ى?");
            if (newText.Length > 0 && (newText[newText.Length - 1] == 'ي' ||
                                       newText[newText.Length - 1] == 'ی'))
            {
                newText = newText.Remove(newText.Length - 1);
                newText = newText.Insert(newText.Length, "ى");
            }
            textGenerator.Populate(newText, generationSettings);
            // Make list of lines
            List<UILineInfo> lines = textGenerator.lines as List<UILineInfo>;
            if (lines == null) return null;
            string linedText = "";
            for (int i = 0; i < lines.Count; i++)
            {
                // Find Start and Length of RTL line and append Line Ending character.
                if (i < lines.Count - 1)
                {
                    int startIndex = lines[i].startCharIdx;
                    int length = lines[i + 1].startCharIdx - lines[i].startCharIdx;
                    linedText += newText.Substring(startIndex, length);
                    if (linedText.Length > 0 &&
                        linedText[linedText.Length - 1] != '\n' &&
                        linedText[linedText.Length - 1] != '\r')
                    {
                        linedText += LineEnding;
                    }
                }
                else
                {
                    // For the Last line, we only need startIndex and line continues to the end.
                    linedText += newText.Substring(lines[i].startCharIdx);
                    //if (resizeTextForBestFit) linedText += '\n';
                }
            }
            if (lines.Count == 0)
            {
                linedText = newText;
            }
            return linedText.RtlFix();
        }
    }
}