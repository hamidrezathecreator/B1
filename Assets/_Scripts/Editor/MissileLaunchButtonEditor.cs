using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MissileLaunchButton))]
public sealed class MissileLaunchButtonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var button = (MissileLaunchButton)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Button position", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Move the button where it should rest and press Capture Rest. " +
                                "Then move it pushed in and press Capture Pressed.", MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Capture Rest")) Record(button, "Capture Rest", button.CaptureRest);
            if (GUILayout.Button("Capture Pressed")) Record(button, "Capture Pressed", button.CapturePressed);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Show Rest")) MoveButton(button, button.PreviewRest);
            if (GUILayout.Button("Show Pressed")) MoveButton(button, button.PreviewPressed);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Path A → B", EditorStyles.boldLabel);
        if (button.MovingObject == null)
            EditorGUILayout.HelpBox("Assign Moving Object first.", MessageType.Info);

        using (new EditorGUI.DisabledScope(button.MovingObject == null))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(button.PointA == null ? "Create Point A" : "Select Point A"))
                SelectOrCreate(button, true);
            if (GUILayout.Button(button.PointB == null ? "Create Point B" : "Select Point B"))
                SelectOrCreate(button, false);
        }

        if (Application.isPlaying && GUILayout.Button("Press (Play Mode)"))
            button.Press();
    }

    static void Record(MissileLaunchButton button, string label, System.Action action)
    {
        Undo.RecordObject(button, label);
        action();
        EditorUtility.SetDirty(button);
    }

    static void MoveButton(MissileLaunchButton button, System.Action action)
    {
        Undo.RecordObject(button.transform, "Preview Button");
        action();
    }

    static void SelectOrCreate(MissileLaunchButton button, bool isA)
    {
        var existing = isA ? button.PointA : button.PointB;
        if (existing != null)
        {
            Selection.activeTransform = existing;
            return;
        }

        var obj = button.MovingObject.transform;
        var point = new GameObject(obj.name + (isA ? " Point A" : " Point B"));
        Undo.RegisterCreatedObjectUndo(point, "Create Path Point");
        // Not parented to the moving object, which would drag the point along with it.
        point.transform.position = isA ? obj.position : obj.position + obj.forward * 10f;

        Undo.RecordObject(button, "Assign Path Point");
        if (isA) button.PointA = point.transform;
        else button.PointB = point.transform;
        EditorUtility.SetDirty(button);
        Selection.activeTransform = point.transform;
    }
}
