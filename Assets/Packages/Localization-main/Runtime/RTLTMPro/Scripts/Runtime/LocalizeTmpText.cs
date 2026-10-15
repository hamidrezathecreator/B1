using Localization;
using Localization.Base;
using Localization.Base.Enums;
using TMPro;
using UnityEngine;

namespace RTLTMPro.Scripts.Runtime
{
    [ExecuteInEditMode]
    public class LocalizeTmpText : TextMeshProUGUI,ILocalizeText
    {
        public override string text
        {
            get { return base.text; }
            set
            {
                if (originalText == value)
                    return;

                originalText = value;

                UpdateText();
            }
        }

        public string OriginalText
        {
            get { return originalText; }
        }

        public bool PreserveNumbers
        {
            get { return preserveNumbers; }
            set
            {
                if (preserveNumbers == value)
                    return;

                preserveNumbers = value;
                havePropertiesChanged = true;
            }
        }

        public bool Farsi
        {
            get { return farsi; }
            set
            {
                if (farsi == value)
                    return;

                farsi = value;
                havePropertiesChanged = true;
            }
        }

        public bool FixTags
        {
            get { return fixTags; }
            set
            {
                if (fixTags == value)
                    return;

                fixTags = value;
                havePropertiesChanged = true;
            }
        }

        public bool ForceFix
        {
            get { return forceFix; }
            set
            {
                if (forceFix == value)
                    return;

                forceFix = value;
                havePropertiesChanged = true;
            }
        }
        [SerializeField] private  string localizationKey;
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

        [SerializeField] protected bool preserveNumbers;

        [SerializeField] protected bool farsi = true;

        [SerializeField] [TextArea(3, 10)] protected string originalText;

        [SerializeField] protected bool fixTags = true;

        [SerializeField] protected bool forceFix;

        protected readonly FastStringBuilder finalText = new FastStringBuilder(RTLSupport.DefaultBufferSize);

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
        
        protected void Update()
        {
            if (havePropertiesChanged)
            {
                UpdateText();
            }
        }

        public void UpdateText()
        {
            if (originalText == null)
                originalText = "";

            if (ForceFix == false && TextUtils.IsRTLInput(originalText) == false)
            {
                isRightToLeftText = false;
                base.text = originalText;
            } else
            {
                if(LocalizationAccessor.Localization == null){return;}
                var currentAlphabetics = LocalizationAccessor.Localization.CurrentAlphabetic;
                isRightToLeftText = true;//currentAlphabetics == LanguageAlphabetic.Arabic ||
                                    //currentAlphabetics == LanguageAlphabetic.ArabicPersian;
                if (isRightToLeftText)
                {
                    base.text = GetFixedText(originalText);   
                }
                else
                {
                    isRightToLeftText = false;
                    base.text = originalText;
                }
            }

            havePropertiesChanged = true;
        }

        private string GetFixedText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            finalText.Clear();
            RTLSupport.FixRTL(input, finalText, farsi, fixTags, preserveNumbers);
            return finalText.ToString();
        }
#if UNITY_EDITOR
        protected override void OnValidate()
        {
            UpdateTextLocalization();
            base.OnValidate();
        }
#endif
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
                alignment != TextAlignmentOptions.Bottom &&
                alignment != TextAlignmentOptions.Center &&
                alignment != TextAlignmentOptions.Top)
            {
                if (LocalizationAccessor.Localization.CurrentDirection == LanguageDirection.Ltr)
                {
                    if (alignment == TextAlignmentOptions.BottomRight)
                    {
                        alignment = TextAlignmentOptions.BottomLeft;
                    }
                    else if (alignment == TextAlignmentOptions.MidlineRight)
                    {
                        alignment = TextAlignmentOptions.MidlineLeft;
                    }
                    else if (alignment == TextAlignmentOptions.TopRight)
                    {
                        alignment = TextAlignmentOptions.TopLeft;
                    }
                }
                else
                {
                    if (alignment == TextAlignmentOptions.BottomLeft)
                    {
                        alignment = TextAlignmentOptions.BottomRight;
                    }
                    else if (alignment == TextAlignmentOptions.Left)
                    {
                        alignment = TextAlignmentOptions.Right;
                    }
                    else if (alignment == TextAlignmentOptions.TopLeft)
                    {
                        alignment = TextAlignmentOptions.TopRight;
                    }
                }
            }
            SetAllDirty();
        }
    }
}
