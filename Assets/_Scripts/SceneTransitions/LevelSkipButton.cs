using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Skip to the next level with the upper button on the left controller (Y on Quest, the button in
/// the same place as B on the right). Fades to black, then loads the next scene in Build Settings.
/// Runs in every scene by itself.
/// </summary>
public sealed class LevelSkipButton : MonoBehaviour
{
    // Scenes that are not in Build Settings still need to know where to go.
    static readonly Dictionary<string, string> NextByName = new Dictionary<string, string>
    {
        { "Lvl_1", "Lvl2_MissileTunnel" },
        { "Lvl2_MissileTunnel", "LVL3_MissileLaunch" },
    };

    readonly List<InputDevice> devices = new List<InputDevice>();
    bool wasPressed;
    bool skipping;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<LevelSkipButton>() != null) return;
        var go = new GameObject("Level Skip Button");
        DontDestroyOnLoad(go);
        go.AddComponent<LevelSkipButton>();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        skipping = false;
        // Ignore a press still held from the previous level.
        wasPressed = true;
    }

    void Update()
    {
        var pressed = LeftUpperButtonPressed();
        if (pressed && !wasPressed && !skipping) Skip();
        wasPressed = pressed;
    }

    bool LeftUpperButtonPressed()
    {
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, devices);
        foreach (var device in devices)
        {
            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out var down) && down)
                return true;
        }
        return false;
    }

    void Skip()
    {
        var next = NextSceneName();
        if (string.IsNullOrEmpty(next))
        {
            Debug.Log("LevelSkipButton: no next level after '" + SceneManager.GetActiveScene().name + "'.");
            return;
        }

        skipping = true;
        SceneTransitionRunner.Begin(next, true, 0f, 0.6f, 0.3f, 0.6f);
    }

    static string NextSceneName()
    {
        var active = SceneManager.GetActiveScene();
        if (active.buildIndex >= 0 && active.buildIndex + 1 < SceneManager.sceneCountInBuildSettings)
        {
            var path = SceneUtility.GetScenePathByBuildIndex(active.buildIndex + 1);
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }

        if (NextByName.TryGetValue(active.name, out var byName) && Application.CanStreamedLevelBeLoaded(byName))
            return byName;
        return null;
    }
}
