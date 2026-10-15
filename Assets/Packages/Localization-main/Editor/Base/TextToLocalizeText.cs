#if UNITY_EDITOR
using Localization.Base;
using Localization.Helpers;
using RTLTMPro.Scripts.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Localization.Tests.Editor.Base
{
    public static class TextToLocalizeText
    {
        [MenuItem("CONTEXT/Text/ConvertToLocalizeText")]
        public static void Localize(MenuCommand command)
        {
            var text = (Text)command.context;
            var go = text.gameObject;
            var tempGo = new GameObject();
            tempGo.transform.SetParent(tempGo.transform);
            var tempLocalizeText = tempGo.AddComponent<LocalizeLegacyText>();
            PropertyCopier<Text, LocalizeLegacyText>.Copy(text, tempLocalizeText);
            Object.DestroyImmediate(text);
            var localizeText = go.AddComponent<LocalizeLegacyText>();
            PropertyCopier<LocalizeLegacyText, LocalizeLegacyText>.Copy(tempLocalizeText, localizeText);
            Object.DestroyImmediate(tempGo);
        }
        [MenuItem("CONTEXT/TextMeshProUGUI/ConvertToLocalizeTextMeshPro")]
        public static void LocalizeTMP(MenuCommand command)
        {
            var text = (TextMeshProUGUI)command.context;
            var go = text.gameObject;
            var tempGo = new GameObject();
            tempGo.transform.SetParent(tempGo.transform);
            var tempLocalizeText = tempGo.AddComponent<LocalizeTmpText>();
            PropertyCopier<TextMeshProUGUI, LocalizeTmpText>.Copy(text, tempLocalizeText);
            Object.DestroyImmediate(text);
            var localizeText = go.AddComponent<LocalizeTmpText>();
            PropertyCopier<LocalizeTmpText, LocalizeTmpText>.Copy(tempLocalizeText, localizeText);
            Object.DestroyImmediate(tempGo);
        }
    }
}
#endif