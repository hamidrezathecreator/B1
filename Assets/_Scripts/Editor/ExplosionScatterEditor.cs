using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ExplosionScatter))]
public sealed class ExplosionScatterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var scatter = (ExplosionScatter)target;
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        DrawEditorTestButtons(scatter);

        EditorGUILayout.Space();

        var mergeButtonsDrawn = false;
        var prop = serializedObject.GetIterator();
        var enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "m_Script") continue;

            EditorGUILayout.PropertyField(prop, true);

            if (!mergeButtonsDrawn && prop.name == "maxPiecesPerMergeGroup")
            {
                DrawMergeButtons(scatter, serializedObject.FindProperty("maxPiecesPerMergeGroup").intValue);
                mergeButtonsDrawn = true;
            }
        }

        if (!mergeButtonsDrawn)
            DrawMergeButtons(scatter, serializedObject.FindProperty("maxPiecesPerMergeGroup")?.intValue ?? 3);

        serializedObject.ApplyModifiedProperties();

        DrawRefsHelp(scatter);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scatter preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Trajectories are drawn from cached rest poses (green / orange / red) and stay put while pieces move. Reset or re-enter Play to refresh rest cache after you move pieces.",
            MessageType.None);

        if (GUILayout.Button("Refresh gizmo sample (repaint Scene view)"))
            SceneView.RepaintAll();
    }

    static void DrawRefsHelp(ExplosionScatter scatter)
    {
        var launch = scatter.transform.Find("LaunchPoint") ?? scatter.transform.Find("Launch Point");
        var ground = scatter.transform.Find("GroundPoint") ?? scatter.transform.Find("Ground Point");
        if (launch == null || ground == null)
        {
            EditorGUILayout.HelpBox(
                "This scatter needs LaunchPoint and GroundPoint as direct children of '" + scatter.name + "'.",
                MessageType.Warning);
        }
    }

    void DrawEditorTestButtons(ExplosionScatter scatter)
    {
        EditorGUILayout.LabelField("Editor test", EditorStyles.boldLabel);

        var big = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 34 };
        var old = GUI.backgroundColor;

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("In Play Mode these run the real PrimeTween explosion.", MessageType.None);
            GUI.backgroundColor = new Color(1f, 0.55f, 0.25f);
            if (GUILayout.Button("▶  Explode all pieces", big))
            {
                serializedObject.ApplyModifiedProperties();
                scatter.Explode();
            }
            GUI.backgroundColor = old;
            if (GUILayout.Button("Reset to rest positions"))
                scatter.ResetExplosion();
            return;
        }

        EditorGUILayout.HelpBox(
            "Uses this object's LaunchPoint / GroundPoint children. Reset restores poses from the first cache this session (Awake in Play Mode, or first test in Edit Mode).",
            MessageType.None);

        GUI.backgroundColor = new Color(1f, 0.55f, 0.25f);
        if (GUILayout.Button("▶  Test scatter (all pieces)", big))
        {
            serializedObject.ApplyModifiedProperties();
            scatter.EditorStartPreviewScatter();
        }
        GUI.backgroundColor = old;

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!scatter.IsEditorPreviewActive))
            {
                if (GUILayout.Button("■  Stop scatter"))
                    scatter.EditorStopPreviewScatter(false);
            }

            if (GUILayout.Button("Reset to rest positions"))
                scatter.ResetExplosion();
        }

    }

    void DrawMergeButtons(ExplosionScatter scatter, int maxPerGroup)
    {
        EditorGUILayout.Space(4f);

        var big = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 32 };
        var old = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.45f, 0.85f, 1f);

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Scan & build merge groups", big))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(scatter, "Rebuild Merge Groups");
                var count = scatter.RebuildMergeGroupsFromPieces();
                EditorUtility.SetDirty(scatter);
                Debug.Log("ExplosionScatter: " + count + " merge group(s) on '" + scatter.name + "'.", scatter);
            }
        }

        GUI.backgroundColor = old;

        EditorGUILayout.HelpBox(
            "Groups up to " + maxPerGroup + " nearby pieces (Merge Threshold) into one tween. Editor only — not runtime.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Clear merge groups"))
                {
                    Undo.RecordObject(scatter, "Clear Merge Groups");
                    scatter.ClearMergeGroups();
                    EditorUtility.SetDirty(scatter);
                }
            }
        }
    }
}