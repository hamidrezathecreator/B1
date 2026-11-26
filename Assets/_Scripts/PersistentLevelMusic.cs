using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class PersistentLevelMusic : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float volume = 0.45f;
    [SerializeField, Min(0f)] float fadeInSeconds = 2f;
    [SerializeField, Min(0f)] float fadeOutSeconds = 2f;
    [SerializeField] string continueInScene = BuildLevelScenes.Lvl2;
    [SerializeField] bool loop = true;

    static PersistentLevelMusic active;
    AudioSource source;
    Coroutine fadeRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() => active = null;

    public void Play()
    {
        source = GetComponent<AudioSource>();
        if (source.clip == null || source.isPlaying) return;
        if (active != null && active != this) Destroy(active.gameObject);
        active = this;
        transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        source.loop = loop;
        source.volume = 0f;
        source.Play();
        BeginFade(volume, fadeInSeconds, false);
    }

    public static void FadeOutActive()
    {
        if (active != null) active.BeginFade(0f, active.fadeOutSeconds, true);
    }

    void BeginFade(float target, float seconds, bool stopAfter)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(target, seconds, stopAfter));
    }

    IEnumerator Fade(float target, float seconds, bool stopAfter)
    {
        var start = source.volume;
        for (var elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            source.volume = Mathf.Lerp(start, target, elapsed / seconds);
            yield return null;
        }
        source.volume = target;
        fadeRoutine = null;
        if (!stopAfter) yield break;
        source.Stop();
        Destroy(gameObject);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single && scene.name != continueInScene)
            Destroy(gameObject);
    }

    void Update()
    {
        if (active == this && source != null && !source.isPlaying && !source.loop && AudioListener.pause == false)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (active == this) active = null;
    }
}
