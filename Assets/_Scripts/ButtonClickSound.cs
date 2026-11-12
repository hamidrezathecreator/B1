using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Click sound for every button press: UI buttons in each loaded scene are wired automatically,
/// physical/poke buttons call <see cref="Play"/> themselves.
/// </summary>
public static class ButtonClickSound
{
    const string ClipPath = "Audio/click";
    const float Volume = 1f;
    const float MinInterval = 0.08f;

    static AudioClip clip;
    static AudioSource source;
    static float lastPlayTime = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button.gameObject.scene != scene) continue;
            button.onClick.RemoveListener(PlayUi);
            button.onClick.AddListener(PlayUi);
        }
    }

    static void PlayUi() => Play();

    public static void Play()
    {
        // One press often reports through several paths (hover, select, finger overlap) in the same moment.
        if (Time.unscaledTime - lastPlayTime < MinInterval) return;
        if (clip == null) clip = Resources.Load<AudioClip>(ClipPath);
        if (clip == null) return;

        if (source == null)
        {
            var go = new GameObject("Button Click Sound");
            Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
        lastPlayTime = Time.unscaledTime;
        source.PlayOneShot(clip, Volume);
    }
}
