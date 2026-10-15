#if UNITY_EDITOR
using Localization.Base;
using UnityEditor;
using UnityEditor.UI;

namespace Localization.Tests.Editor.Base
{
    [CustomEditor(typeof(LocalizeLegacyText))]
    public class LocalizeTextEditor : TextEditor
    {
        public override void OnInspectorGUI()
        {
            LocalizeLegacyText component = (LocalizeLegacyText)target;
            var currLocalizationKey = component.LocalizationKey;
            var currDontChangeAlignment = component.DontChangeAlignment;
            var currDontChangeFont = component.DontChangeFont;
            var newLocalizationKey = (string)EditorGUILayout.TextField("Localization Key", component.LocalizationKey);
            var newDontChangeAlignment = (bool)EditorGUILayout.Toggle("Dont Change Alignment", component.DontChangeAlignment);
            var newDontChangeFont = (bool)EditorGUILayout.Toggle("Dont Change Font", component.DontChangeFont);
            var anyChange = false;
            if (newLocalizationKey != currLocalizationKey)
            {
                component.LocalizationKey = newLocalizationKey;
                anyChange = true;
            }
            if (newDontChangeAlignment != currDontChangeAlignment)
            {
                component.DontChangeAlignment = newDontChangeAlignment;
                anyChange = true;
            }

            if (newDontChangeFont != currDontChangeFont)
            {
                component.DontChangeFont = newDontChangeFont;
                anyChange = true;
            }
            if (anyChange)
            {
                EditorUtility.SetDirty(target);
            }
            base.OnInspectorGUI();
        }
    }
}
#endif