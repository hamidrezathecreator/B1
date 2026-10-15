#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Animation.Inventory.Editor
{
    [CustomEditor(typeof(InventoryManager))]
    public class InventoryManagerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var inventoryManager = target as InventoryManager;
            if (inventoryManager.InitializeOnAwake)
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("initialPrefabs"));
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
#endif