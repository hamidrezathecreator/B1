using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MissileLauncherSystem))]
public sealed class MissileLauncherSystemEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var launcher = (MissileLauncherSystem)target;

        DrawTestButtons(launcher);
        EditorGUILayout.Space();
        DrawDefaultInspector();
        EditorGUILayout.Space();
        DrawTargetingHelp(launcher);
    }

    static void DrawTestButtons(MissileLauncherSystem launcher)
    {
        EditorGUILayout.LabelField("Play Mode test", EditorStyles.boldLabel);

        var big = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 34 };
        var old = GUI.backgroundColor;

        using (new EditorGUI.DisabledScope(!Application.isPlaying || !launcher.isActiveAndEnabled))
        {
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to fly a test missile.", MessageType.None);

            GUI.backgroundColor = new Color(1f, 0.55f, 0.25f);
            if (GUILayout.Button("▶  Test missile at Point B", big))
                launcher.TestFireAtPointB();
            GUI.backgroundColor = old;

            if (GUILayout.Button("Fire full salvo (Fire)"))
                launcher.Fire();
            if (GUILayout.Button("Reset missiles and effects"))
                launcher.ResetSystem();
        }

        if (GUILayout.Button("Repaint Scene (refresh impact gizmos)"))
            SceneView.RepaintAll();
    }

    static void DrawTargetingHelp(MissileLauncherSystem launcher)
    {
        var settings = new SerializedObject(launcher);
        var mask = settings.FindProperty("impactLayers").intValue;
        if (mask == 0)
        {
            EditorGUILayout.HelpBox(
                "Impact Layers is empty: strikes use Point B height only. Assign your bridge (or other) layer so missiles snap to that surface.",
                MessageType.Info);
        }

        if (CondensationCloudSystem.Instance == null && !Application.isPlaying)
            EditorGUILayout.HelpBox(
                "Add a CondensationCloudSystem to the scene (once) for Wilson clouds on each impact.",
                MessageType.None);
    }
}