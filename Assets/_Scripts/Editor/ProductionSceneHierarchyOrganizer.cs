using System;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>
/// Groups root objects in Production level scenes into a small set of hierarchy buckets
/// aligned with each level's runtime systems (XR, timeline, environment, gameplay, VFX).
/// </summary>
public static class ProductionSceneHierarchyOrganizer
{
    static readonly string[] ProductionScenePaths =
    {
        "Assets/Scenes/Production/Lvl_1.unity",
        "Assets/Scenes/Production/Lvl2_MissileTunnel.unity",
        "Assets/Scenes/Production/LVL3_MissileLaunch.unity",
    };

    const string BucketXr = "_XR";
    const string BucketSystems = "_Systems";
    const string BucketEnvironment = "_Environment";
    const string BucketGameplay = "_Gameplay";
    const string BucketVfx = "_VFX";
    const string BucketUi = "_UI";
    const string BucketAudio = "_Audio";
    const string BucketLighting = "_Lighting";

    static readonly HashSet<string> BucketNames = new(StringComparer.OrdinalIgnoreCase)
    {
        BucketXr,
        BucketSystems,
        BucketEnvironment,
        BucketGameplay,
        BucketVfx,
        BucketUi,
        BucketAudio,
        BucketLighting,
        "Environment",
        "Lighting",
        "Audio",
        "ENV",
        "Tunnel",
    };

    [MenuItem("B1/Production/Update Build Settings (3 Levels)")]
    public static void UpdateBuildSettingsMenu()
    {
        UpdateBuildSettings();
        Debug.Log("Production build settings now list all three Production scenes.");
    }

    [MenuItem("B1/Production/Organize Hierarchy In Open Scene")]
    public static void OrganizeOpenSceneMenu()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            Debug.LogWarning("Open and save a scene first.");
            return;
        }

        OrganizeScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("B1/Production/Organize Hierarchy In All Production Levels")]
    public static void OrganizeAllProductionScenesMenu()
    {
        foreach (var path in ProductionScenePaths)
            OrganizeSceneAsset(path);
        AssetDatabase.SaveAssets();
        Debug.Log("Finished organizing all Production level hierarchies.");
    }

    public static void RunBatch()
    {
        UpdateBuildSettings();
        foreach (var path in ProductionScenePaths)
            OrganizeSceneAsset(path);
        AssetDatabase.SaveAssets();
        Debug.Log("Batch: build settings + hierarchy organization complete.");
    }

    static void UpdateBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>();
        foreach (var path in ProductionScenePaths)
            scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void OrganizeSceneAsset(string scenePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var moved = OrganizeScene(scene);
        if (moved > 0)
            EditorSceneManager.SaveScene(scene);
        else
            EditorSceneManager.SaveOpenScenes();
        Debug.Log($"{scenePath}: reparented {moved} root object(s).");
    }

    static int OrganizeScene(Scene scene)
    {
        var roots = scene.GetRootGameObjects().OrderBy(go => go.name, StringComparer.OrdinalIgnoreCase).ToArray();
        var buckets = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        var moved = 0;

        foreach (var root in roots)
        {
            if (IsBucketRoot(root))
            {
                NormalizeLegacyBucketName(root);
                buckets[root.name] = root.transform;
                continue;
            }

            var bucket = Classify(root);
            if (string.IsNullOrEmpty(bucket))
                continue;

            var sub = ClassifySubBucket(root);
            var parent = GetOrCreateBucket(scene, buckets, bucket);
            if (!string.IsNullOrEmpty(sub))
                parent = GetOrCreateBucket(scene, buckets, bucket, sub, parent);

            if (root.transform.parent == parent)
                continue;

            Undo.RegisterFullObjectHierarchyUndo(root, "Organize hierarchy");
            root.transform.SetParent(parent, true);
            moved++;
        }

        SortBucketChildren(buckets);
        return moved;
    }

    static void NormalizeLegacyBucketName(GameObject root)
    {
        switch (root.name)
        {
            case "Environment":
            case "ENV":
                root.name = BucketEnvironment;
                break;
            case "Lighting":
                root.name = BucketLighting;
                break;
            case "Audio":
                root.name = BucketAudio;
                break;
        }
    }

    static bool IsBucketRoot(GameObject go) => BucketNames.Contains(go.name);

    static Transform GetOrCreateBucket(
        Scene scene,
        Dictionary<string, Transform> buckets,
        string name,
        Transform parentOverride = null)
        => GetOrCreateBucket(scene, buckets, name, null, parentOverride);

    static Transform GetOrCreateBucket(
        Scene scene,
        Dictionary<string, Transform> buckets,
        string name,
        string subName,
        Transform parentOverride)
    {
        var key = string.IsNullOrEmpty(subName) ? name : $"{name}/{subName}";
        if (buckets.TryGetValue(key, out var existing))
            return existing;

        Transform parent = parentOverride;
        if (parent == null)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    parent = root.transform;
                    buckets[name] = parent;
                    break;
                }
            }
        }

        if (parent == null)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            parent = go.transform;
            buckets[name] = parent;
        }

        if (string.IsNullOrEmpty(subName))
        {
            buckets[key] = parent;
            return parent;
        }

        var child = parent.Find(subName);
        if (child == null)
        {
            var subGo = new GameObject(subName);
            subGo.transform.SetParent(parent, false);
            child = subGo.transform;
        }

        buckets[key] = child;
        return child;
    }

    static void SortBucketChildren(Dictionary<string, Transform> buckets)
    {
        foreach (var bucket in buckets.Values.Distinct())
        {
            if (bucket == null || bucket.childCount < 2)
                continue;

            var children = new List<Transform>(bucket.childCount);
            for (var i = 0; i < bucket.childCount; i++)
                children.Add(bucket.GetChild(i));

            children = children.OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < children.Count; i++)
                children[i].SetSiblingIndex(i);
        }
    }

    static string Classify(GameObject go)
    {
        var name = go.name;

        if (go.GetComponent<XROrigin>() != null
            || name.IndexOf("XR Origin", StringComparison.OrdinalIgnoreCase) >= 0)
            return BucketXr;

        if (IsVfx(name, go))
            return BucketVfx;

        if (IsGameplay(name, go))
            return BucketGameplay;

        if (go.GetComponent<PlayableDirector>() != null)
            return BucketSystems;
        if (go.GetComponent<SceneTransitionManager>() != null)
            return BucketSystems;
        if (go.GetComponent<Lvl1Tutorial>() != null)
            return BucketSystems;
        if (go.GetComponent<ElevatorDoorIntro>() != null)
            return BucketSystems;
        if (go.GetComponent<XrLevelPlaySetup>() != null)
            return BucketSystems;
        if (go.GetComponent<MissileLauncherSystem>() != null)
            return BucketSystems;
        if (go.GetComponent<AToBSystem>() != null)
            return BucketSystems;
        if (go.GetComponent<SceneObjectSwapper>() != null)
            return BucketSystems;
        if (go.GetComponent<UnityEngine.EventSystems.EventSystem>() != null)
            return BucketSystems;
        if (name.Equals("Director", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("SceneTrans", StringComparison.OrdinalIgnoreCase)
            || name.Equals("EventSystem", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Teleport Area Setup", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VrBlinkEffect", StringComparison.OrdinalIgnoreCase))
            return BucketSystems;

        if (go.GetComponent<Light>() != null
            || name.Equals("CenterLights", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PictureFrameLights", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0)
            return BucketLighting;

        if (go.GetComponent<AudioSource>() != null
            || name.Equals("MainMenuMusic", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Audio", StringComparison.OrdinalIgnoreCase))
            return BucketAudio;

        if (go.GetComponent<Canvas>() != null
            || name.Equals("UI", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MenuPanels", StringComparison.OrdinalIgnoreCase))
            return BucketUi;

        if (IsEnvironment(name, go))
            return BucketEnvironment;

        if (name.Equals("Main Camera", StringComparison.OrdinalIgnoreCase))
            return BucketSystems;

        if (name.StartsWith("SM_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Plane", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Terrain", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Columns", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Railing", StringComparison.OrdinalIgnoreCase)
            || name.Equals("FloorDecals", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PictureFrame", StringComparison.OrdinalIgnoreCase)
            || name.Equals("RocketStand", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Lunchers", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Bridg", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Bridge", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Red_Complete", StringComparison.OrdinalIgnoreCase))
            return BucketEnvironment;

        return BucketEnvironment;
    }

    static string ClassifySubBucket(GameObject go)
    {
        var name = go.name;

        if (name.IndexOf("Point A", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Point B", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Flight Path", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("Missiles", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("Launch", StringComparison.OrdinalIgnoreCase) >= 0
            || name.Equals("Lunchers", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Interactables", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AtoBSystem", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("GroundPoint", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("ShootMissile", StringComparison.OrdinalIgnoreCase) >= 0)
            return "MissilesAndPaths";

        if (name.StartsWith("SM_Tunnel", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("SM_Elevator", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Tunnel", StringComparison.OrdinalIgnoreCase))
            return "Tunnel";

        if (name.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("Bridg", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Red_Complete", StringComparison.OrdinalIgnoreCase))
            return "Bridge";

        if (name.IndexOf("Scatter", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Crossfade", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Condensation", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Swap", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Cinematics";

        return null;
    }

    static bool IsVfx(string name, GameObject go)
    {
        if (name.IndexOf("Scatter", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Crossfade", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("CrossFader", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Condensation", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("Swap", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("SwapSystem", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("CharactersSwap", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return go.GetComponent<ExplosionScatter>() != null
            || go.GetComponent<SpriteCrossfadeController>() != null
            || go.GetComponent<ModelCrossfadeController>() != null
            || go.GetComponent<CondensationCloudSystem>() != null;
    }

    static bool IsGameplay(string name, GameObject go)
    {
        if (name.Equals("Interactables", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Missiles", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("GroundPoint", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("LaunchPoint", StringComparison.OrdinalIgnoreCase) >= 0
            || name.Equals("Lunchers", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AtoBSystem", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("Shahed", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("env intractive", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return go.GetComponentInChildren<MissileLaunchButton>(true) != null
            || go.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>(true) != null;
    }

    static bool IsEnvironment(string name, GameObject go)
    {
        if (name.Equals("Environment", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ENV", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Terrain", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Plane", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}