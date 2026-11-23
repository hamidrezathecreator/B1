using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Persist build-level state machines in production scenes (optional; they also auto-spawn at runtime).</summary>
static class BuildLevelSceneSetup
{
    [MenuItem("B1/Build Levels/Add State Machines To Open Scene")]
    static void AddToOpenScene()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("Build levels", "Open a production scene first.", "OK");
            return;
        }

        switch (scene.name)
        {
            case BuildLevelScenes.Lvl1:
                Ensure<Lvl1BuildLevelStateMachine>("Lvl1 Build Level State Machine");
                EnsureOnPlayerFall<XrShockFallSequence>();
                break;
            case BuildLevelScenes.Lvl2:
                Ensure<Lvl2BuildLevelStateMachine>("Lvl2 Build Level State Machine");
                break;
            case BuildLevelScenes.Lvl3:
                Ensure<Lvl3BuildLevelStateMachine>("Lvl3 Build Level State Machine");
                break;
            default:
                EditorUtility.DisplayDialog("Build levels",
                    "This menu only applies to Lvl_1, Lvl2_MissileTunnel, or LVL3_MissileLaunch.", "OK");
                return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.DisplayDialog("Build levels", "State machine object(s) added to " + scene.name + ".", "OK");
    }

    static void Ensure<T>(string objectName) where T : Component
    {
        if (Object.FindAnyObjectByType<T>() != null) return;
        var go = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(go, "Add build level state machine");
        go.AddComponent<T>();
    }

    static void EnsureOnPlayerFall<T>() where T : Component
    {
        if (Object.FindAnyObjectByType<T>() != null) return;
        GameObject host = null;
        foreach (var fall in Object.FindObjectsByType<PlayerPlaneFall>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!fall.gameObject.scene.IsValid()) continue;
            host = fall.gameObject;
            break;
        }

        if (host == null)
        {
            var root = GameObject.Find("Bridge Sequence");
            host = root != null ? root : new GameObject("Player And Plane Fall");
            if (root == null)
            {
                Undo.RegisterCreatedObjectUndo(host, "Add shock fall");
                host.AddComponent<PlayerPlaneFall>();
            }
        }

        if (host.GetComponent<T>() == null)
            Undo.AddComponent<T>(host);
    }
}