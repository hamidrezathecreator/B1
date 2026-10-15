using RTLTMPro.Scripts.Runtime;
using TMPro.EditorUtilities;
using UnityEditor;
using UnityEngine;
using TMP_UiEditorPanel = TMPro.EditorUtilities.TMP_EditorPanelUI;

namespace RTLTMPro.Editor
{
    [CustomEditor(typeof(LocalizeTmpText)), CanEditMultipleObjects]
    public class RTLTextMeshProEditor : TMP_UiEditorPanel
    {
        private SerializedProperty originalTextProp;
        private SerializedProperty preserveNumbersProp;
        private SerializedProperty farsiProp;
        private SerializedProperty fixTagsProp;
        private SerializedProperty forceFixProp;

        private SerializedProperty localizationKeyProp;
        private SerializedProperty dontChangeAlignmentProp;
        private SerializedProperty dontChangeFontProp;
        
        private bool foldout;
        private LocalizeTmpText tmpro;

        protected override void OnEnable()
        {
            base.OnEnable();
            foldout = true;
            preserveNumbersProp = serializedObject.FindProperty("preserveNumbers");
            farsiProp = serializedObject.FindProperty("farsi");
            fixTagsProp = serializedObject.FindProperty("fixTags");
            forceFixProp = serializedObject.FindProperty("forceFix");
            localizationKeyProp = serializedObject.FindProperty("localizationKey");
            dontChangeAlignmentProp = serializedObject.FindProperty("dontChangeAlignment");
            dontChangeFontProp = serializedObject.FindProperty("dontChangeFont");
            originalTextProp = serializedObject.FindProperty("originalText");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            tmpro = (LocalizeTmpText)target;

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(originalTextProp, new GUIContent("RTL Text Input Box"));

            ListenForZeroWidthNoJoiner();

            if (EditorGUI.EndChangeCheck())
                OnChanged();

            serializedObject.ApplyModifiedProperties();

            base.OnInspectorGUI();

            foldout = EditorGUILayout.Foldout(foldout, "RTL Settings", TMP_UIStyleManager.boldFoldout);
            if (foldout)
            {
                DrawOptions();

                if (GUILayout.Button("Re-Fix"))
                    m_HavePropertiesChanged = true;

                if (EditorGUI.EndChangeCheck())
                    m_HavePropertiesChanged = true;
            }

            if (m_HavePropertiesChanged)
                OnChanged();

            serializedObject.ApplyModifiedProperties();
        }

        protected void OnChanged()
        {
            tmpro.UpdateText();
            m_HavePropertiesChanged = false;
            m_TextComponent.havePropertiesChanged = true;
            m_TextComponent.ComputeMarginSize();
            EditorUtility.SetDirty(target);
        }

        protected virtual void DrawOptions()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            farsiProp.boolValue = GUILayout.Toggle(farsiProp.boolValue, new GUIContent("Farsi"));
            forceFixProp.boolValue = GUILayout.Toggle(forceFixProp.boolValue, new GUIContent("Force Fix"));
            preserveNumbersProp.boolValue = GUILayout.Toggle(preserveNumbersProp.boolValue, new GUIContent("Preserve Numbers"));

            if (tmpro.richText)
                fixTagsProp.boolValue = GUILayout.Toggle(fixTagsProp.boolValue, new GUIContent("FixTags"));
            dontChangeAlignmentProp.boolValue = GUILayout.Toggle(dontChangeAlignmentProp.boolValue, new GUIContent("Don't Change Alignment"));
            dontChangeFontProp.boolValue = GUILayout.Toggle(dontChangeFontProp.boolValue, new GUIContent("Don't Change Font"));

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Localization Key");
            localizationKeyProp.stringValue =
                GUILayout.TextField(localizationKeyProp.stringValue); 
        }

        protected virtual void ListenForZeroWidthNoJoiner()
        {
            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);

            bool shortcutPressed = (Event.current.modifiers & EventModifiers.Control) != 0 &&
                                   (Event.current.modifiers & EventModifiers.Shift) != 0 &&
                                   Event.current.type == EventType.KeyUp &&
                                   Event.current.keyCode == KeyCode.Alpha2;

            if (!shortcutPressed) return;

            originalTextProp.stringValue = originalTextProp.stringValue.Insert(editor.cursorIndex, ((char)SpecialCharacters.ZeroWidthNoJoiner).ToString());
            editor.selectIndex++;
            editor.cursorIndex++;
            Event.current.Use();
            Repaint();
        }
    }
}
