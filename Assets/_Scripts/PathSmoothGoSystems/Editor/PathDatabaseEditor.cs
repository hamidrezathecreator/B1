using System;
using GoWhere;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(PathDatabase))]
public class PathDatabaseEditor : Editor
{
    private int selectedPath;
    private bool placing;
    private PathDatabase Database => (PathDatabase)target;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox(
            "Car routes are open. Cars teleport from END to START; no closing segment is created. " +
            "Direction: Forward follows waypoint 0 to the last; Backward follows the last to waypoint 0. " +
            "Scene arrows show actual travel. Cars always face that direction.",
            MessageType.Info);
        EditorGUI.BeginChangeCheck();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            Database.RebuildCache();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();
        var paths = serializedObject.FindProperty("globalPaths");
        if (GUILayout.Button("Add Car Path"))
        {
            int index = paths.arraySize;
            paths.arraySize++;
            var path = paths.GetArrayElementAtIndex(index);
            string id = "CarPath_" + index;
            int suffix = index;
            while (Database.HasGlobalPath(id)) id = "CarPath_" + (++suffix);
            path.FindPropertyRelative("id").stringValue = id;
            path.FindPropertyRelative("direction").enumValueIndex = (int)PathDirection.Forward;
            path.FindPropertyRelative("legacyBackward").boolValue = false;
            path.FindPropertyRelative("directionMigrated").boolValue = true;
            path.FindPropertyRelative("points").ClearArray();
            path.FindPropertyRelative("pointSharp").ClearArray();
            path.FindPropertyRelative("snapToGround").boolValue = true;
            path.FindPropertyRelative("groundLayers").intValue = 0;
            path.FindPropertyRelative("yOffset").floatValue = 0f;
            path.FindPropertyRelative("groundRayHeight").floatValue = 100f;
            path.FindPropertyRelative("groundRayDistance").floatValue = 1000f;
            path.FindPropertyRelative("overrideSmoothing").boolValue = false;
            path.FindPropertyRelative("smoothMode").enumValueIndex = 0;
            path.FindPropertyRelative("interpolationSteps").intValue = 8;
            serializedObject.ApplyModifiedProperties();
            selectedPath = index;
            placing = true;
            Database.RebuildCache();
            SceneView.RepaintAll();
        }

        if (paths.arraySize == 0) return;
        selectedPath = Mathf.Clamp(selectedPath, 0, paths.arraySize - 1);
        var labels = new string[paths.arraySize];
        for (int i = 0; i < labels.Length; i++)
        {
            var path = paths.GetArrayElementAtIndex(i);
            string direction = path.FindPropertyRelative("direction").enumDisplayNames[
                path.FindPropertyRelative("direction").enumValueIndex];
            labels[i] = i + ": " + path.FindPropertyRelative("id").stringValue + " (" + direction + ")";
        }
        EditorGUI.BeginChangeCheck();
        selectedPath = EditorGUILayout.Popup("Scene Edit Path", selectedPath, labels);
        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

        if (GUILayout.Button(placing ? "Finish Placing (Enter / Escape)" : "Place Waypoints in Scene"))
        {
            placing = !placing;
            SceneView.RepaintAll();
        }
        if (placing)
            EditorGUILayout.HelpBox(
                "Left-click the ground to append waypoints. Enter or Escape finishes placement. " +
                "Without a collider, points use a horizontal plane at the last waypoint's height.",
                MessageType.Info);

        var selected = Database.GlobalPaths[selectedPath];
        if (selected != null)
        {
            if (GUILayout.Button("Snap Selected Path to Ground"))
            {
                Undo.RecordObject(Database, "Snap Car Path to Ground");
                selected.snapToGround = true;
                Physics.SyncTransforms();
                Changed();
                SceneView.RepaintAll();
            }
            EditorGUILayout.HelpBox(
                "Set Snap To Ground, Ground Layers and Y Offset on the path above. " +
                "They apply to the entire route, including smoothed points. " +
                "Choose ground-only layers to exclude cars and other objects.", MessageType.Info);
            if (selected.points == null || selected.points.Count < 2)
                EditorGUILayout.HelpBox("Add at least two distinct waypoints.", MessageType.Warning);
            int matches = 0;
            foreach (var path in Database.GlobalPaths)
                if (path != null && string.Equals(path.id, selected.id, StringComparison.OrdinalIgnoreCase)) matches++;
            if (string.IsNullOrWhiteSpace(selected.id) || matches > 1)
                EditorGUILayout.HelpBox("Give each route a unique, non-empty id.", MessageType.Warning);
        }
        if (GUILayout.Button("Rebuild Smoothed Routes"))
        {
            Database.RebuildCache();
            SceneView.RepaintAll();
        }
    }

    private void OnSceneGUI()
    {
        if (Database.GlobalPaths.Count == 0) return;
        selectedPath = Mathf.Clamp(selectedPath, 0, Database.GlobalPaths.Count - 1);
        var path = Database.GlobalPaths[selectedPath];
        if (path == null || path.points == null) return;
        var points = path.points;
        Color pathColor = Database.Editor_GetPathColor(path);
        var labelStyle = new GUIStyle(EditorStyles.label);
        labelStyle.normal.textColor = pathColor;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 placedPoint = Database.GetPlacedPoint(path, points[i]);
            EditorGUI.BeginChangeCheck();
            Vector3 point = Handles.PositionHandle(placedPoint, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Database, "Move Car Waypoint");
                points[i] = point - Vector3.up * path.yOffset;
                Changed();
            }
            bool backward = path.direction == PathDirection.Backward;
            bool isStart = i == (backward ? points.Count - 1 : 0);
            bool isEnd = i == (backward ? 0 : points.Count - 1);
            string label = isStart ? "START" : isEnd ? "END (snap to START)" : i.ToString();
            Handles.Label(placedPoint + Vector3.up * 0.4f, label, labelStyle);
        }

        if (!placing) return;
        Event current = Event.current;
        if (current.type == EventType.KeyDown &&
            (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter ||
             current.keyCode == KeyCode.Escape))
        {
            placing = false;
            current.Use();
            Repaint();
            return;
        }
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
        Vector3 position;
        int mask = path.groundLayers.value;
        if (mask == 0) mask = LayerMask.GetMask("Ground");
        if (mask == 0) mask = Physics.DefaultRaycastLayers;
        if (Physics.Raycast(ray, out var hit, Mathf.Infinity, mask, QueryTriggerInteraction.Ignore))
            position = hit.point;
        else
        {
            float height = points.Count > 0 ? points[points.Count - 1].y : Database.transform.position.y;
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (!plane.Raycast(ray, out float distance)) return;
            position = ray.GetPoint(distance);
        }
        Color previousColor = Handles.color;
        Handles.color = pathColor;
        Vector3 placedPosition = Database.GetPlacedPoint(path, position);
        Handles.SphereHandleCap(0, placedPosition, Quaternion.identity, 0.3f, EventType.Repaint);
        if (points.Count > 0)
            Handles.DrawDottedLine(Database.GetPlacedPoint(path, points[points.Count - 1]), placedPosition, 4f);
        Handles.color = previousColor;
        if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
        {
            Undo.RecordObject(Database, "Add Car Waypoint");
            points.Add(position);
            if (path.pointSharp == null) path.pointSharp = new System.Collections.Generic.List<bool>();
            while (path.pointSharp.Count < points.Count) path.pointSharp.Add(false);
            Changed();
            current.Use();
            Repaint();
        }
        SceneView.RepaintAll();
    }

    private void Changed()
    {
        Database.RebuildCache();
        EditorUtility.SetDirty(Database);
        PrefabUtility.RecordPrefabInstancePropertyModifications(Database);
        if (Database.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(Database.gameObject.scene);
    }

    private void OnEnable() => Undo.undoRedoPerformed += OnUndo;
    private void OnDisable() => Undo.undoRedoPerformed -= OnUndo;
    private void OnUndo()
    {
        if (target == null) return;
        Database.RebuildCache();
        Repaint();
        SceneView.RepaintAll();
    }
}
