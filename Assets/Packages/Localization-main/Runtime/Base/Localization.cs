using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Localization.Base.Constraints;
using Localization.Base.Enums;
using Localization.Base.ValueObjects;
using Localization.LINQtoCSV;
using UnityEngine;
using UnityEngine.Events;

namespace Localization.Base
{
    [CreateAssetMenu(menuName = nameof(Localization),fileName = nameof(Localization), order = 0)]
    public class Localization : ScriptableObject
    {
        [field: SerializeField] public Languages CurrentLanguage { get; set; } = Languages.English;
        [SerializeField] private FontMode fontMode;
        [SerializeField] private AlphabeticFontItem defaultFont;
        [SerializeField] private AlphabeticFontItem[] fonts;
        [SerializeField] private TextAsset localizationFile;
        public LanguageAlphabetic CurrentAlphabetic => LanguageConstraints.LanguageAlphabeticDict[CurrentLanguage];
        public LanguageDirection CurrentDirection => LanguageConstraints.LanguageDirectionDict[CurrentLanguage];

        public AlphabeticFontItem CurrentFont => fontMode != FontMode.Dynamic
            ? defaultFont : fonts.FirstOrDefault(f => f.Alphabetic == CurrentAlphabetic) ?? defaultFont;
        //[SerializeField]
        //private Languages[] languageSortInFile = new Languages[] { Languages.English };
        //private Dictionary<Languages, int> languageIndexInFile;
        //private int languageInt = 0;
        [NonSerialized] private static Dictionary<string, Dictionary<Languages,string>> textDict;
        [NonSerialized] private List<ILocalizeText> localizeTexts;
        public bool StaticFont => fontMode == FontMode.Static;
        public void Initialize()
        {
            localizeTexts = new List<ILocalizeText>();
            UpdateTexts();
        }

        public void UpdateFile(TextAsset newLocalizationFile)
        {
            if (newLocalizationFile == null)
            {
                Debug.LogError("The localization file is null");
                return;
            }
            localizationFile = newLocalizationFile;
            UpdateTexts();
        }

        private void UpdateTexts()
        {
            if (localizationFile == null)
            {
                Debug.LogError($"Localization file is null");
                return;
            }
            CsvFileDescription inputFileDescription = new CsvFileDescription
            {
                SeparatorChar = ',',
                FirstLineHasColumnNames = true,
                
            };
            textDict = new Dictionary<string, Dictionary<Languages, string>>();
            using var ms = new MemoryStream();
            using var txtWriter = new StreamWriter(ms);
            using var txtReader = new StreamReader(ms);
            txtWriter.Write(localizationFile.text);
            txtWriter.Flush();
            ms.Seek(0, SeekOrigin.Begin);

            CsvContext cc = new CsvContext();
            var localizationItems = cc.Read<LocalizationItem>(txtReader, inputFileDescription).ToList();
            // var allLanguages = Enum.GetValues(typeof(Languages)) as Languages[];
            var firstItem = localizationItems.FirstOrDefault(li => !li.IsFormality);
            if (firstItem == null)
                return;
            var allLanguages = firstItem.GetAvailableLanguages();
            foreach (var localizationItem in localizationItems)
            {
                if (string.IsNullOrEmpty(localizationItem.Key) ||
                    localizationItem.IsFormality ||
                    Localization.textDict.ContainsKey(localizationItem.Key))
                {
                    continue;
                }
                var textDict = new Dictionary<Languages,string>();
                foreach (var language in allLanguages)
                {
                    var text = localizationItem.GetTextByLanguage(language);
                    if (string.IsNullOrEmpty(text) == false)
                    {
                        textDict.Add(language, text);
                    }
                }
                Localization.textDict.Add(localizationItem.Key, textDict);
            }
        }

        private LanguageChangeEvent onLanguageChange = new LanguageChangeEvent();
        public void RegisterLabel(ILocalizeText localizeText)
        {
            localizeTexts.Add(localizeText);
        }
        public void UnregisterLabel(ILocalizeText localizeText)
        {
            localizeTexts.Remove(localizeText);
        }
        public void AddChangeListener(UnityAction<UnityEngine.Object> listener)
        {
            onLanguageChange.AddListener(listener);
        }
        public void RemoveChangeListener(UnityAction<UnityEngine.Object> listener)
        {
            onLanguageChange.RemoveListener(listener);
        }
        public void ChangeLanguage(Languages lang, UnityEngine.Object sender)
        {
            if (CurrentLanguage == lang)
                return;
            CurrentLanguage = lang;
            //languageInt = languageIndexInFile[lang];

            foreach (var localizeText in localizeTexts)
            {
                localizeText.UpdateTextLocalization();
            }

            if (sender)
            {
                onLanguageChange.Invoke(sender);
            }
        }

        public string GetText(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "";
            //if (!textDict.ContainsKey(key))
            //    return "Localization key doesn't exist in the localization file";
            if (!textDict.ContainsKey(key))
                return key;
            var dictItem = textDict[key];
            string text;
            if (dictItem.TryGetValue(CurrentLanguage, out text) == false)
            {
                return dictItem.FirstOrDefault().Value;
            }
            return text;
        }
        public class LanguageChangeEvent : UnityEvent<UnityEngine.Object> { }
        public enum FontMode { Static = 0, Dynamic = 1 }
    }
}
