using System.Collections.Generic;
using Localization.Base.Enums;

namespace Localization.Base.Constraints
{
    internal static class LanguageConstraints
    {
        public static readonly Dictionary<Languages, LanguageDirection> LanguageDirectionDict = new()
        {
            { Languages.English, LanguageDirection.Ltr },
            { Languages.Persian, LanguageDirection.Rtl },
            /*{ Languages.Portuguese, LanguageDirection.Ltr },
            { Languages.French, LanguageDirection.Ltr },
            { Languages.Italian, LanguageDirection.Ltr },
            { Languages.German, LanguageDirection.Ltr },
            { Languages.Dutch, LanguageDirection.Ltr },
            { Languages.Turkish, LanguageDirection.Ltr },
            { Languages.Chinese, LanguageDirection.Ltr },
            { Languages.Arabic, LanguageDirection.Rtl },
            
            { Languages.Russian, LanguageDirection.Ltr },
            { Languages.Hindi, LanguageDirection.Ltr },
            { Languages.Japanese, LanguageDirection.Ltr },
            { Languages.Spanish, LanguageDirection.Ltr },
            { Languages.Vietnamese, LanguageDirection.Ltr },
            { Languages.Thai, LanguageDirection.Ltr },
            { Languages.Malay, LanguageDirection.Ltr },
            { Languages.Romanian, LanguageDirection.Ltr }*/
        };
        
        public static Dictionary<Languages, LanguageAlphabetic> LanguageAlphabeticDict = new ()
        {
            { Languages.English, LanguageAlphabetic.Latin },
            { Languages.Persian, LanguageAlphabetic.ArabicPersian }
            /*{ Languages.Portuguese, LanguageAlphabetic.Latin },
            { Languages.French, LanguageAlphabetic.Latin },
            { Languages.Italian, LanguageAlphabetic.Latin },
            { Languages.German, LanguageAlphabetic.Latin },
            { Languages.Dutch, LanguageAlphabetic.Latin },
            { Languages.Turkish, LanguageAlphabetic.Latin },
            { Languages.Chinese, LanguageAlphabetic.Chinese },
            { Languages.Arabic, LanguageAlphabetic.Arabic },
            { Languages.Persian, LanguageAlphabetic.ArabicPersian },
            { Languages.Russian, LanguageAlphabetic.Cyrillic },
            { Languages.Hindi, LanguageAlphabetic.Devanagari },
            { Languages.Japanese, LanguageAlphabetic.Hiragana },
            { Languages.Spanish, LanguageAlphabetic.Hangul },
            { Languages.Vietnamese, LanguageAlphabetic.Vietnamese },
            { Languages.Thai, LanguageAlphabetic.Thai },
            { Languages.Malay, LanguageAlphabetic.Malay },
            { Languages.Romanian, LanguageAlphabetic.LatinRomanian },*/
        };
    }
}