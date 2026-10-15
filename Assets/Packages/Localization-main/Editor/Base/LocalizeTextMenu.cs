#if UNITY_EDITOR
using Localization.Base;
using UnityEditor;
using UnityEngine;

namespace Localization.Tests.Editor.Base
{
    public static class LocalizeTextMenu
    {
        [MenuItem("GameObject/UI/LocalizeNormalText")]
        public static void AddLocalizeText()
        {
            var go = new GameObject("LocalizeNormalText");
            var trans = Selection.activeTransform;
            if (trans != null)
            {
                go.transform.SetParent(trans, false);
            }
            go.AddComponent<LocalizeLegacyText>();
        }
    }
}
#endif