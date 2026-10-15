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
    [Min(0f)] [SerializeField] private float fadeOutSeconds = 0.35f;
    [Min(0f)] [SerializeField] private float holdBlackSeconds = 0f;
    [Min(0f)] [SerializeField] private float fadeInSeconds = 0.35f;

    /// <summary>Use this no-argument method for a Signal Receiver or button.</summary>
    public void LoadConfiguredScene() => LoadScene(sceneToLoad);

    /// <summary>Load any scene listed in Build Settings; works without Timeline.</summary>
    public void LoadScene(string sceneName)
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

        SceneTransitionRunner.Begin(sceneName, fade, delayBeforeLoad,
            fadeOutSeconds, holdBlackSeconds, fadeInSeconds);
    }
}

