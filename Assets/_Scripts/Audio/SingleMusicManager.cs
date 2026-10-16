using UnityEngine;
using UnityEngine.Events;

/// <summary>Controls one music AudioSource directly, fading from its current volume.</summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public sealed class SingleMusicManager : MonoBehaviour
{
    [Tooltip("The single music source. Defaults to the AudioSource on this object.")]
    [SerializeField] AudioSource musicSource;
    [SerializeField] bool playIfStopped = true;
    [Header("Startup fade-in")]
    [SerializeField] bool fadeInOnStart = true;
    [SerializeField, Range(0f, 1f)] float fadeInVolume = 0.2f;
    [Tooltip("Volume units per second. Zero applies the target immediately.")]
    [SerializeField, Min(0f)] float fadeInSpeed = 0.1f;
    public UnityEvent volumeReached = new UnityEvent();
    public bool IsFading { get; private set; }
    float targetVolume;
    float fadeSpeed;
    bool volumeRequested;

    void Awake()
    {
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        if (fadeInOnStart && musicSource != null) musicSource.volume = 0f;
    }

    void Start()
    {
        // An earlier event (for example Fire) takes priority over the default startup fade.
        if (fadeInOnStart && !volumeRequested) FadeIn();
    }

    /// <summary>Starts music silently and fades to the configured initial level. Can also be called by a UnityEvent.</summary>
    public void FadeIn()
    {
        if (!isActiveAndEnabled || musicSource == null || !musicSource.isActiveAndEnabled) return;
        musicSource.volume = 0f;
        if (!musicSource.isPlaying && musicSource.clip != null) musicSource.Play();
        FadeToVolume(fadeInVolume, fadeInSpeed);
    }

    /// <summary>UnityEvent receiver. Speed is volume units per second; zero applies the target immediately.</summary>
    public void FadeToVolume(float volume, float volumePerSecond)
    {
        if (!isActiveAndEnabled || musicSource == null || !musicSource.isActiveAndEnabled) return;
        volumeRequested = true;
        targetVolume = Mathf.Clamp01(volume);
        fadeSpeed = Mathf.Max(0f, volumePerSecond);
        if (playIfStopped && !musicSource.isPlaying && musicSource.clip != null) musicSource.Play();
        if (fadeSpeed <= 0f || musicSource.volume == targetVolume)
        {
            musicSource.volume = targetVolume;
            IsFading = false;
            volumeReached.Invoke();
            return;
        }
        IsFading = true;
    }

    void Update()
    {
        if (!IsFading) return;
        if (musicSource == null || !musicSource.isActiveAndEnabled) { StopFade(); return; }
        musicSource.volume = Mathf.MoveTowards(musicSource.volume, targetVolume, fadeSpeed * Time.unscaledDeltaTime);
        if (musicSource.volume != targetVolume) return;
        IsFading = false;
        volumeReached.Invoke();
    }

    public void StopFade() => IsFading = false;

    void OnDisable() => StopFade();
}
