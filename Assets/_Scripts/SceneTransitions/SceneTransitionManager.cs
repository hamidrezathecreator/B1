using UnityEngine;

/// <summary>
/// Loads build scenes on request. Call LoadConfiguredScene from a Timeline
/// Signal Receiver or UnityEvent, or LoadScene(name) from code or a string event.
/// SceneTransitionRunner handles the transition across the scene change.
/// </summary>
public sealed class SceneTransitionManager : MonoBehaviour
{
    [Header("Scene for no-argument events")]
    [Tooltip("Name or Build Settings path (without .unity). Used by LoadConfiguredScene().")]
    [SerializeField] private string sceneToLoad;

    [Header("Transition")]
    [Min(0f)] [SerializeField] private float delayBeforeLoad = 0f;
    [SerializeField] private bool fade = true;
    [Tooltip("Duration of fading the current scene to black before loading.")]
    [Min(0f)] [SerializeField] private float fadeOutSeconds = 0.35f;
    [Min(0f)] [SerializeField] private float holdBlackSeconds = 0f;
    [Tooltip("Duration of fading both the black overlay and text away after the hold.")]
    [Min(0f)] [SerializeField] private float fadeInSeconds = 0.35f;

    [Header("Audio")]
    [Tooltip("Optional source whose current volume fades to zero with Fade Out Seconds, before loading the next scene.")]
    [SerializeField] private AudioSource audioSourceToFadeOut;

    [Header("Text above fade")]
    [Tooltip("UI text prefab used by the WithText methods. Its text and styling are preserved.")]
    [SerializeField] private RTLTMPro.RTLTextMeshPro transitionTextPrefab;
    [Tooltip("Text fades in during the black hold after loading. The hold extends if needed to finish this fade.")]
    [Min(0f)] [SerializeField] private float textFadeInSeconds = 0.35f;

    /// <summary>Use this no-argument method for a Signal Receiver or button.</summary>
    public void LoadConfiguredScene() => LoadScene(sceneToLoad);

    /// <summary>Load the configured scene with centered text above the fade.</summary>
    public void LoadConfiguredSceneWithText() => LoadSceneWithText(sceneToLoad);

    /// <summary>Load a scene with the assigned RTL text prefab above the fade.</summary>
    public void LoadSceneWithText(string sceneName) => LoadSceneInternal(sceneName, true);

    /// <summary>Load any scene listed in Build Settings; works without Timeline.</summary>
    public void LoadScene(string sceneName) => LoadSceneInternal(sceneName, false);

    private void LoadSceneInternal(string sceneName, bool showText)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("SceneTransitionManager: Set Scene To Load or pass a scene name.", this);
            return;
        }

        sceneName = sceneName.Trim();
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"SceneTransitionManager: '{sceneName}' is not enabled in Build Settings.", this);
            return;
        }

        if (showText && transitionTextPrefab == null)
            Debug.LogWarning("SceneTransitionManager: Assign Transition Text Prefab to show text above the fade.", this);

        SceneTransitionRunner.Begin(sceneName, fade, delayBeforeLoad,
            fadeOutSeconds, holdBlackSeconds, fadeInSeconds,
            showText ? transitionTextPrefab : null, textFadeInSeconds, audioSourceToFadeOut);
    }
}

