using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(MissileLaunchButton))]
public sealed class MissileLaunchButtonEditor : Editor
{
    const string PendingKey = "B1.MissileLaunchButton.PendingTest";
    const float PlayModeDelay = 1.5f;

    [MenuItem("B1/Wire Launch Buttons")]
    static void WireLaunchButtons()
    {
        var count = MissileLaunchButton.AutoWireScene(
            (pointName, position, parent) =>
            {
                var go = new GameObject(pointName);
                Undo.RegisterCreatedObjectUndo(go, "Wire Launch Buttons");
                go.transform.SetParent(parent, false);
                go.transform.position = position;
                return go.transform;
            },
            obj => Undo.RecordObject(obj, "Wire Launch Buttons"));

        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("Wire Launch Buttons: " + count + " button(s) wired. Move the 'Point B' objects under 'Launch Paths' to change where each one flies.");
    }

    [MenuItem("B1/Test All Launch Buttons")]
    static void TestAll()
    {
        if (Application.isPlaying)
        {
            foreach (var b in FindObjectsByType<MissileLaunchButton>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                b.TestLaunch();
            return;
        }
        StartPlayAndTest("*");
    }

    public override void OnInspectorGUI()
    {
        var button = (MissileLaunchButton)target;

        var big = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, fixedHeight = 34 };
        var old = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
        var label = Application.isPlaying ? "▶  Test Launch" : "▶  Test Launch (enters Play Mode)";
        if (GUILayout.Button(label, big))
        {
            if (Application.isPlaying) button.TestLaunch();
            else StartPlayAndTest(PathOf(button.transform));
        }
        GUI.backgroundColor = old;
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Opens Play Mode, then presses this button after " + PlayModeDelay +
                                    " s. In Play Mode each click resets the object to Point A and launches again.",
                MessageType.None);

        EditorGUILayout.Space();
        DrawDefaultInspector();

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
    }

    static void StartPlayAndTest(string buttonPath)
    {
        SessionState.SetString(PendingKey, buttonPath);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void HookPlayMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode) return;
        var pending = SessionState.GetString(PendingKey, "");
        if (string.IsNullOrEmpty(pending)) return;
        SessionState.EraseString(PendingKey);

        // Give the level setup (hands, ground, auto wiring) a moment before pressing.
        var fireAt = EditorApplication.timeSinceStartup + PlayModeDelay;
        void Tick()
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (EditorApplication.timeSinceStartup < fireAt) return;
            EditorApplication.update -= Tick;

            foreach (var b in FindTargets(pending))
                b.TestLaunch();
        }
        EditorApplication.update += Tick;
    }

    static IEnumerable<MissileLaunchButton> FindTargets(string path)
    {
        foreach (var b in FindObjectsByType<MissileLaunchButton>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (path == "*" || PathOf(b.transform) == path)
                yield return b;
        }
    }

    static string PathOf(Transform t)
    {
        var path = t.name + "#" + t.GetSiblingIndex();
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "#" + t.GetSiblingIndex() + "/" + path;
        }
        return path;
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
