using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ElevatorDoorIntro))]
sealed class ElevatorDoorIntroEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        DrawPathTools();
    }

    void DrawPathTools()
    {
        serializedObject.Update();
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Path waypoint tools", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Drag the yellow spheres in the Scene view to set the route. Path Height Offset lifts the arrows if they sit under the ground mesh. Moving a waypoint's Y does not raise the arrows; the offset does.",
            MessageType.Info);

        DrawSelectRow(serializedObject.FindProperty("pathStart"), "Start");
        var waypoints = serializedObject.FindProperty("pathWaypoints");
        if (waypoints != null)
        {
            for (var i = 0; i < waypoints.arraySize; i++)
                DrawSelectRow(waypoints.GetArrayElementAtIndex(i), "Corner " + (i + 1));
        }
        DrawSelectRow(serializedObject.FindProperty("pathTarget"), "Destination");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add corner waypoint"))
            AddWaypoint();
        using (new EditorGUI.DisabledScope(waypoints == null || waypoints.arraySize == 0))
        {
            if (GUILayout.Button("Remove last corner"))
                RemoveLastWaypoint();
        }
        EditorGUILayout.EndHorizontal();
        serializedObject.ApplyModifiedProperties();
    }

    void DrawSelectRow(SerializedProperty property, string label)
    {
        if (property == null) return;
        var point = property.objectReferenceValue as Transform;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, point != null ? point.name : "(unassigned)");
        using (new EditorGUI.DisabledScope(point == null))
        {
            if (GUILayout.Button("Frame", GUILayout.Width(64)))
            {
                EditorGUIUtility.PingObject(point.gameObject);
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.Frame(new Bounds(point.position, Vector3.one * 3f), false);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    void OnSceneGUI()
    {
        serializedObject.Update();
        DrawHandle(serializedObject.FindProperty("pathStart"), "Start");
        var waypoints = serializedObject.FindProperty("pathWaypoints");
        if (waypoints != null)
        {
            for (var i = 0; i < waypoints.arraySize; i++)
                DrawHandle(waypoints.GetArrayElementAtIndex(i), "Corner " + (i + 1));
        }
        DrawHandle(serializedObject.FindProperty("pathTarget"), "Destination");
    }

    void DrawHandle(SerializedProperty property, string label)
    {
        var point = property != null ? property.objectReferenceValue as Transform : null;
        if (point == null) return;

        var offset = serializedObject.FindProperty("pathHeightOffset");
        var lift = offset != null ? offset.floatValue : 0f;
        var handlePos = point.position + Vector3.up * lift;

        Handles.color = new Color(0.98f, 0.80f, 0.20f, 1f);
        Handles.Label(handlePos + Vector3.up * 0.12f, label);

        EditorGUI.BeginChangeCheck();
        var next = Handles.Slider2D(handlePos, Vector3.up, Vector3.right, Vector3.forward, 0.18f, Handles.SphereHandleCap, Vector2.zero);
        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(point, "Move Path Waypoint");
        point.position = new Vector3(next.x, point.position.y, next.z);
        EditorUtility.SetDirty(point);
    }

    void AddWaypoint()
    {
        var intro = (ElevatorDoorIntro)target;
        var waypoints = serializedObject.FindProperty("pathWaypoints");
        var start = serializedObject.FindProperty("pathStart").objectReferenceValue as Transform;
        var targetPoint = serializedObject.FindProperty("pathTarget").objectReferenceValue as Transform;

        var go = new GameObject("Tablet Path Corner");
        Undo.RegisterCreatedObjectUndo(go, "Add Path Waypoint");
        go.transform.SetParent(intro.transform, false);

        Transform last = null;
        if (waypoints.arraySize > 0)
            last = waypoints.GetArrayElementAtIndex(waypoints.arraySize - 1).objectReferenceValue as Transform;
        var from = last != null ? last.position : start != null ? start.position : intro.transform.position;
        var to = targetPoint != null ? targetPoint.position : from + intro.transform.forward;
        go.transform.position = Vector3.Lerp(from, to, 0.5f);

        waypoints.arraySize++;
        waypoints.GetArrayElementAtIndex(waypoints.arraySize - 1).objectReferenceValue = go.transform;
        serializedObject.ApplyModifiedProperties();
    }

    void RemoveLastWaypoint()
    {
        var waypoints = serializedObject.FindProperty("pathWaypoints");
        if (waypoints == null || waypoints.arraySize == 0) return;
        var last = waypoints.GetArrayElementAtIndex(waypoints.arraySize - 1).objectReferenceValue as Transform;
        waypoints.arraySize--;
        serializedObject.ApplyModifiedProperties();
        if (last != null)
            Undo.DestroyObjectImmediate(last.gameObject);
    }
}
