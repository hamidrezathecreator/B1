using System;
using TMPro;
using UnityEngine;

namespace Localization.Base
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class TmProLocalizer : MonoBehaviour, ILocalizeText
    {
        [field: SerializeField] public string LocalizationKey { get; set; }
        [field: SerializeField] public bool DontChangeAlignment { get; set; }
        [field: SerializeField] public bool DontChangeFont { get; set; } = true;
        private TextMeshProUGUI _tmPro;
        private TextMeshProUGUI tmPro => _tmPro ??= GetComponent<TextMeshProUGUI>();
        private bool isInitialized;
        private void Awake()
        {
            if (isInitialized == false)
            {
                UpdateTextLocalization();
            }
        }

        private void OnEnable()
        {
            LocalizationAccessor.Localization.RegisterLabel(this);
        }

        private void OnDisable()
        {
            if (Application.isPlaying == false)
                return;
            if (LocalizationAccessor.Localization == null)
                return;
            LocalizationAccessor.Localization.UnregisterLabel(this);
        }

        public void UpdateTextLocalization()
        {
            isInitialized = true;
            if (LocalizationAccessor.Localization == null)
                return;
            if (!DontChangeFont && !LocalizationAccessor.Localization.StaticFont)
                LocalizationAccessor.Localization.CurrentFont.SetFont(tmPro);
            if (string.IsNullOrEmpty(LocalizationKey) != false)
                return;
            var text = LocalizationAccessor.Localization.GetText(LocalizationKey);
            if(!string.IsNullOrEmpty(text))
                tmPro.SetText(text);
        }
    }
}