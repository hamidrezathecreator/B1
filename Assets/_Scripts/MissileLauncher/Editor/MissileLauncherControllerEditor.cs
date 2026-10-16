using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[CustomEditor(typeof(MissileLauncherController))]
public sealed class MissileLauncherControllerEditor : Editor
{
    const string ButtonPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/DemoAssets/Prefabs/Interactables/Push Button.prefab";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var launcher = (MissileLauncherController)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Missile slots accept prefab assets or scene objects. The local sky direction is each missile model's nose axis after rotation. A tutorial setup coordinates buttons and multiple launchers.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || !launcher.isActiveAndEnabled))
        {
            if (GUILayout.Button("Phase 1 - Rotate Container")) launcher.RotateContainer();
            if (GUILayout.Button("Phase 2 - Launch Missiles")) launcher.LaunchMissiles();
            if (GUILayout.Button("Reset Launcher and Missiles")) launcher.ResetLauncher();
        }
        using (new EditorGUI.DisabledScope(Application.isPlaying))
            if (GUILayout.Button("Create VR Tutorial Setup")) CreateTutorial(launcher);
    }

    [MenuItem("CONTEXT/MissileLauncherController/Create VR Tutorial Setup")]
    static void CreateTutorial(MenuCommand command) => CreateTutorial((MissileLauncherController)command.context);

    static void CreateTutorial(MissileLauncherController launcher)
    {
        if (Application.isPlaying) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
        if (prefab == null)
        {
            Debug.LogError("XR Starter Assets Push Button prefab is missing. Import the Starter Assets sample before creating buttons.", launcher);
            return;
        }
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create missile launcher tutorial");
        var root = new GameObject(launcher.name + " Tutorial");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, launcher.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(root, "Create VR button panel");
        root.transform.SetPositionAndRotation(launcher.transform.position, launcher.transform.rotation);
        var rotate = CreateButton(prefab, root.transform, "Rotate Container", new Vector3(-0.12f, 0f, 0f));
        var fire = CreateButton(prefab, root.transform, "Fire Missiles", new Vector3(0.12f, 0f, 0f));
        var buttons = Undo.AddComponent<MissileLauncherButtons>(root);
        var buttonSettings = new SerializedObject(buttons);
        buttonSettings.FindProperty("rotateButton").objectReferenceValue = rotate;
        buttonSettings.FindProperty("fireButton").objectReferenceValue = fire;
        buttonSettings.ApplyModifiedProperties();
        var actions = Undo.AddComponent<MissileLauncherActions>(root);
        var actionSettings = new SerializedObject(actions);
        var launchers = actionSettings.FindProperty("launchers");
        launchers.arraySize = 1;
        launchers.GetArrayElementAtIndex(0).objectReferenceValue = launcher;
        actionSettings.ApplyModifiedProperties();

        var tutorialAudio = Undo.AddComponent<AudioSource>(root);
        tutorialAudio.playOnAwake = false;
        tutorialAudio.loop = false;
        var orchestrator = Undo.AddComponent<MissileLauncherTutorialOrchestrator>(root);
        var tutorialSettings = new SerializedObject(orchestrator);
        tutorialSettings.FindProperty("buttons").objectReferenceValue = buttons;
        tutorialSettings.FindProperty("actions").objectReferenceValue = actions;
        tutorialSettings.FindProperty("tutorialAudioSource").objectReferenceValue = tutorialAudio;
        tutorialSettings.ApplyModifiedProperties();
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
    }

    static XRSimpleInteractable CreateButton(GameObject prefab, Transform parent, string name, Vector3 position)
    {
        var button = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(button, "Create VR push button");
        button.name = name;
        button.transform.localPosition = position;
        button.transform.localRotation = Quaternion.identity;
        return button.GetComponent<XRSimpleInteractable>();
    }
}
