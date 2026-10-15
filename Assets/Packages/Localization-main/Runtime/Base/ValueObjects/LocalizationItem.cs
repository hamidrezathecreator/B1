using System;
using System.Collections.Generic;
using System.Linq;
using Localization.Base.Enums;
using UnityEngine;

namespace Localization.Base.ValueObjects
{
    internal class LocalizationItem
    {
        public string Key;
        public string English;
        public string Spanish;
        public string Portuguese;
        public string French;
        public string Italian;
        public string German;
        public string Dutch;
        public string Turkish;
        public string Chinese;
        public string Arabic;
        public string Persian;
        public string Russian;
        public string Hindi;
        public string Japanese;
        public string Korean;
        public string Vietnamese;
        public string Thai;
        public string Malay;
        public string Romanian;

        public bool IsFormality
        {
            get
            {
                var allFields = typeof(LocalizationItem).GetFields();
                allFields = allFields.Where(f => f.Name != nameof(Key) &&
                                                 f.FieldType == typeof(string) &&
                                                 f.GetValue(this) != null &&
                                                 (string)f.GetValue(this) != "").ToArray();
                return !allFields.Any();
            }
        }

        public string GetTextByLanguage(Languages language)
        {
            var type = typeof(LocalizationItem);
            var field = type.GetField(language.ToString());
            var value = field.GetValue(this);
            if (value == null)
            {
                Debug.LogWarning($"Localization: Key {Key} is null");
                return "";
            }
            var valueString = value.ToString();
            if (valueString != "")
                return valueString;
            Debug.LogWarning($"Localization: Key {Key} is Empty");
            return "";
        }

        public IEnumerable<Languages> GetAvailableLanguages()
        {
            var allFields = typeof(LocalizationItem).GetFields();
            allFields = allFields.Where(f => f.Name != nameof(Key) &&
                                             f.FieldType == typeof(string) &&
                                             f.GetValue(this) != null &&
                                             (string)f.GetValue(this) != "").ToArray();
            var allLanguages = Enum.GetValues(typeof(Languages)) as Languages[];
            return allLanguages.Where(l => allFields.Any(f => l.ToString() == f.Name));
        }
    }
}
