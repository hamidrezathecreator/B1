using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.Video;

/// <summary>Shows cards, gates physical buttons, and waits for the entire launcher group between steps.</summary>
[RequireComponent(typeof(AudioSource))]
public sealed class MissileLauncherTutorialOrchestrator : MonoBehaviour
{
    public enum TutorialPhase { Idle, ShowingRotateCard, AwaitingRotate, Rotating, ShowingFireCard, AwaitingFire, Firing, Completed, Failed, PlayingRotateAudio, PlayingFireAudio, WaitingToBegin, WaitingForFinalLogo, PreparingFinalLogo, PlayingFinalLogo, ReturningToMenu }

    [SerializeField] MissileLauncherButtons buttons;
    [SerializeField] MissileLauncherActions actions;
    [Header("Existing world-space UI")]
    [Tooltip("CanvasGroup on your already configured rotation card. Only its alpha is changed.")]
    [SerializeField] CanvasGroup rotateCard;
    [Tooltip("CanvasGroup on your already configured fire card. Only its alpha is changed.")]
    [SerializeField] CanvasGroup fireCard;
    [SerializeField, Min(0f)] float fadeInDuration = 0.35f;
    [SerializeField, Min(0f)] float fadeOutDuration = 0.35f;
    [Header("Audio before each card")]
    [Tooltip("Dedicated source for tutorial narration. Defaults to the AudioSource on this object.")]
    [SerializeField] AudioSource tutorialAudioSource;
    [SerializeField] AudioClip rotateIntroSound;
    [SerializeField] AudioClip fireIntroSound;
    [Header("Music as missile flight sounds fade")]
    [SerializeField] SingleMusicManager musicManager;
    [SerializeField, Range(0f, 1f)] float fireMusicVolume = 0.8f;
    [Tooltip("Volume units per second. For example, 0.2 raises volume by 0.2 each second. Zero sets it immediately.")]
    [SerializeField, Min(0f)] float fireMusicFadeSpeed = 0.2f;
    [Header("Begin delay")]
    [FormerlySerializedAs("beginOnStart")]
    [SerializeField] bool beginAfterDelay = true;
    [Tooltip("Seconds to wait before the first sound/card. Also applied when Begin is called by a Timeline signal.")]
    [SerializeField, Min(0f)] float beginDelay = 3f;
    [Header("Final logo and return to menu")]
    [SerializeField] bool showFinalLogo = true;
    [Tooltip("Existing CanvasGroup containing your logo RawImage. UI/video rendering settings are left as authored.")]
    [SerializeField] CanvasGroup finalLogoGroup;
    [SerializeField] VideoPlayer finalLogoPlayer;
    [Tooltip("Delay after all launcher firing sequences complete, before preparing and showing the logo.")]
    [SerializeField, Min(0f)] float finalLogoDelay = 3f;
    [SerializeField, Min(0f)] float finalLogoFadeInDuration = 1f;
    [SerializeField, Min(0.1f)] float finalLogoPrepareTimeout = 30f;
    [SerializeField] SceneTransitionManager sceneTransitionManager;
    [SerializeField] string menuSceneName = "Menu";
    public UnityEvent tutorialCompleted = new UnityEvent();
    public UnityEvent tutorialFailed = new UnityEvent();
    public TutorialPhase Phase { get; private set; }
    Coroutine cardRoutine;
    Coroutine introRoutine;
    Coroutine beginRoutine;
    Coroutine finalLogoRoutine;
    bool ownsAudioPlayback;
    bool ownsLogoPlayback;
    bool finalLogoFinished;
    string finalLogoError;
    readonly List<MissileProjectile> musicMissiles = new List<MissileProjectile>();
    bool trackingFlightMusic;
    float musicStartVolume;
    float musicRiseProgress;

    void Awake()
    {
        if (tutorialAudioSource == null) tutorialAudioSource = GetComponent<AudioSource>();
        if (tutorialAudioSource == null) tutorialAudioSource = gameObject.AddComponent<AudioSource>();
        tutorialAudioSource.playOnAwake = false;
        if (finalLogoGroup != null) finalLogoGroup.alpha = 0f;
        if (showFinalLogo && finalLogoPlayer != null)
        {
            finalLogoPlayer.playOnAwake = false;
            finalLogoPlayer.Stop();
        }
        HideCardsImmediate();
    }

    void OnEnable()
    {
        if (buttons != null)
        {
            buttons.DisableButtons();
            buttons.rotatePressed.AddListener(OnRotatePressed);
            buttons.firePressed.AddListener(OnFirePressed);
        }
        if (actions != null)
        {
            actions.allRotationsCompleted.AddListener(OnAllRotationsCompleted);
            actions.allLaunchSequencesCompleted.AddListener(OnAllLaunchSequencesCompleted);
            actions.actionFailed.AddListener(Fail);
            actions.missileLaunched.AddListener(OnMissileLaunched);
        }
    }

    // Start runs after all scene buttons and guide components have initialized.
    void Start() { if (beginAfterDelay && Phase == TutorialPhase.Idle) Begin(); }

    [ContextMenu("Test/Begin or Restart Tutorial (Play Mode)")]
    public void Begin()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        StopFlightMusicTracking();
        StopBeginDelay();
        StopIntroAudio();
        StopFinalLogo();
        if (buttons == null || !buttons.isActiveAndEnabled || !buttons.IsConfigured || actions == null || !actions.isActiveAndEnabled ||
            rotateCard == null || fireCard == null || rotateCard == fireCard ||
            !rotateCard.gameObject.activeInHierarchy || !fireCard.gameObject.activeInHierarchy)
        {
            Debug.LogError("Assign the button adapter, launcher actions, and two distinct, active card CanvasGroups from your existing world-space UI.", this);
            Fail();
            return;
        }
        if ((rotateIntroSound != null || fireIntroSound != null) &&
            (tutorialAudioSource == null || !tutorialAudioSource.isActiveAndEnabled))
        {
            Debug.LogError("Tutorial intro sounds require an active, enabled AudioSource.", this);
            Fail();
            return;
        }
        buttons.DisableButtons();
        actions.ResetAll();
        StopCardFade();
        HideCardsImmediate();
        if (beginDelay <= 0f) StartCardIntro(true);
        else
        {
            Phase = TutorialPhase.WaitingToBegin;
            beginRoutine = StartCoroutine(BeginAfterDelay());
        }
    }

    IEnumerator BeginAfterDelay()
    {
        var delay = beginDelay;
        var elapsed = 0f;
        while (elapsed < delay)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        beginRoutine = null;
        StartCardIntro(true);
    }

    void StopBeginDelay()
    {
        if (beginRoutine != null) StopCoroutine(beginRoutine);
        beginRoutine = null;
    }

    void OnRotatePressed()
    {
        if (Phase != TutorialPhase.AwaitingRotate) return;
        Phase = TutorialPhase.Rotating;
        buttons.DisableButtons();
        TransitionCards(null);
        actions.RotateAll();
    }

    void OnAllRotationsCompleted()
    {
        if (Phase != TutorialPhase.Rotating) return;
        StartCardIntro(false);
    }

    void StartCardIntro(bool rotation)
    {
        StopIntroAudio();
        buttons.DisableButtons();
        var clip = rotation ? rotateIntroSound : fireIntroSound;
        if (clip == null) { ShowTutorialCard(rotation); return; }
        Phase = rotation ? TutorialPhase.PlayingRotateAudio : TutorialPhase.PlayingFireAudio;
        introRoutine = StartCoroutine(PlayIntro(clip, rotation));
    }

    IEnumerator PlayIntro(AudioClip clip, bool rotation)
    {
        if (tutorialAudioSource == null || !tutorialAudioSource.isActiveAndEnabled)
        {
            Fail();
            yield break;
        }
        tutorialAudioSource.loop = false;
        tutorialAudioSource.clip = clip;
        ownsAudioPlayback = true;
        tutorialAudioSource.Play();
        // Wait for actual playback, including listener pauses, rather than a timer based on clip length.
        while (tutorialAudioSource != null &&
            (tutorialAudioSource.isPlaying || (AudioListener.pause && !tutorialAudioSource.ignoreListenerPause)))
        {
            if (!tutorialAudioSource.isActiveAndEnabled) { Fail(); yield break; }
            yield return null;
        }
        ownsAudioPlayback = false;
        introRoutine = null;
        if (tutorialAudioSource == null || !tutorialAudioSource.isActiveAndEnabled) { Fail(); yield break; }
        ShowTutorialCard(rotation);
    }

    void ShowTutorialCard(bool rotation)
    {
        var showingPhase = rotation ? TutorialPhase.ShowingRotateCard : TutorialPhase.ShowingFireCard;
        Phase = showingPhase;
        TransitionCards(rotation ? rotateCard : fireCard, () =>
        {
            if (!isActiveAndEnabled || Phase != showingPhase) return;
            Phase = rotation ? TutorialPhase.AwaitingRotate : TutorialPhase.AwaitingFire;
            if (rotation) buttons.EnableRotate();
            else buttons.EnableFire();
        });
    }

    void StopIntroAudio()
    {
        if (introRoutine != null) StopCoroutine(introRoutine);
        introRoutine = null;
        if (ownsAudioPlayback && tutorialAudioSource != null) tutorialAudioSource.Stop();
        ownsAudioPlayback = false;
    }

    void OnFirePressed()
    {
        if (Phase != TutorialPhase.AwaitingFire) return;
        Phase = TutorialPhase.Firing;
        buttons.DisableButtons();
        TransitionCards(null);
        musicMissiles.Clear();
        musicRiseProgress = 0f;
        musicStartVolume = musicManager != null ? musicManager.CurrentVolume : 0f;
        trackingFlightMusic = musicManager != null;
        // Hold the current level while nearby missiles are loud, including during the startup fade.
        if (musicManager != null) musicManager.FadeToVolume(musicStartVolume, 0f);
        actions.FireAll();
    }

    void OnMissileLaunched(MissileProjectile missile)
    {
        if (trackingFlightMusic && missile != null) musicMissiles.Add(missile);
    }

    void LateUpdate()
    {
        if (!trackingFlightMusic || musicManager == null || musicMissiles.Count == 0 || Camera.main == null) return;
        var loudestFlight = 0f;
        foreach (var missile in musicMissiles)
            if (missile != null) loudestFlight = Mathf.Max(loudestFlight, missile.FlightSoundDistanceVolume);

        // Keep the cinematic build rising even when a later missile launches nearby.
        var progress = 1f - loudestFlight;
        if (progress <= musicRiseProgress) return;
        musicRiseProgress = progress;
        var target = Mathf.Lerp(musicStartVolume, Mathf.Max(musicStartVolume, fireMusicVolume), progress);
        musicManager.FadeToVolume(target, fireMusicFadeSpeed);
    }

    void StopFlightMusicTracking()
    {
        trackingFlightMusic = false;
        musicMissiles.Clear();
    }

    void OnAllLaunchSequencesCompleted()
    {
        if (Phase != TutorialPhase.Firing) return;
        if (showFinalLogo)
        {
            Phase = TutorialPhase.WaitingForFinalLogo;
            finalLogoRoutine = StartCoroutine(PlayFinalLogoAndReturn());
            return;
        }
        Phase = TutorialPhase.Completed;
        tutorialCompleted.Invoke();
    }

    bool FinalLogoReferencesReady() => finalLogoGroup != null && finalLogoGroup != rotateCard && finalLogoGroup != fireCard &&
        finalLogoGroup.gameObject.activeInHierarchy && finalLogoPlayer != null && finalLogoPlayer.isActiveAndEnabled &&
        sceneTransitionManager != null && sceneTransitionManager.isActiveAndEnabled;

    IEnumerator PlayFinalLogoAndReturn()
    {
        if (!FinalLogoReferencesReady() || string.IsNullOrWhiteSpace(menuSceneName) || !Application.CanStreamedLevelBeLoaded(menuSceneName.Trim()))
        {
            Debug.LogError("Assign an active final-logo CanvasGroup, VideoPlayer, SceneTransitionManager, and a menu scene enabled in Build Settings.", this);
            Fail();
            yield break;
        }
        buttons.DisableButtons();
        finalLogoGroup.alpha = 0f;
        var elapsed = 0f;
        while (elapsed < finalLogoDelay)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        if (!FinalLogoReferencesReady()) { Fail(); yield break; }
        Phase = TutorialPhase.PreparingFinalLogo;
        finalLogoFinished = false;
        finalLogoError = null;
        finalLogoPlayer.loopPointReached += OnFinalLogoFinished;
        finalLogoPlayer.errorReceived += OnFinalLogoError;
        ownsLogoPlayback = true;
        finalLogoPlayer.Stop();
        finalLogoPlayer.isLooping = false;
        finalLogoPlayer.Prepare();
        elapsed = 0f;
        while (FinalLogoReferencesReady() && !finalLogoPlayer.isPrepared && finalLogoError == null && elapsed < finalLogoPrepareTimeout)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        if (!FinalLogoReferencesReady() || finalLogoError != null || !finalLogoPlayer.isPrepared)
        {
            Debug.LogError("Final logo could not be prepared: " + (finalLogoError ?? "player unavailable or preparation timed out."), this);
            Fail();
            yield break;
        }
        Phase = TutorialPhase.PlayingFinalLogo;
        finalLogoPlayer.Play();
        elapsed = 0f;
        while (elapsed < finalLogoFadeInDuration)
        {
            if (!FinalLogoReferencesReady() || finalLogoError != null) { Fail(); yield break; }
            elapsed += Time.unscaledDeltaTime;
            finalLogoGroup.alpha = Mathf.Clamp01(elapsed / finalLogoFadeInDuration);
            yield return null;
        }
        finalLogoGroup.alpha = 1f;
        while (!finalLogoFinished && finalLogoError == null && FinalLogoReferencesReady()) yield return null;
        if (!FinalLogoReferencesReady() || finalLogoError != null) { Fail(); yield break; }

        // Keep the last video frame visible while the scene manager fades to the menu.
        UnbindFinalLogoEvents();
        finalLogoRoutine = null;
        Phase = TutorialPhase.ReturningToMenu;
        sceneTransitionManager.LoadScene(menuSceneName.Trim());
        tutorialCompleted.Invoke();
    }

    void OnFinalLogoFinished(VideoPlayer _) => finalLogoFinished = true;
    void OnFinalLogoError(VideoPlayer _, string message) => finalLogoError = message;

    void UnbindFinalLogoEvents()
    {
        if (finalLogoPlayer == null) return;
        finalLogoPlayer.loopPointReached -= OnFinalLogoFinished;
        finalLogoPlayer.errorReceived -= OnFinalLogoError;
    }

    void StopFinalLogo()
    {
        if (finalLogoRoutine != null) StopCoroutine(finalLogoRoutine);
        finalLogoRoutine = null;
        UnbindFinalLogoEvents();
        if (ownsLogoPlayback && finalLogoPlayer != null) finalLogoPlayer.Stop();
        ownsLogoPlayback = false;
        finalLogoFinished = false;
        finalLogoError = null;
        if (finalLogoGroup != null) finalLogoGroup.alpha = 0f;
    }

    void Fail()
    {
        StopFlightMusicTracking();
        StopFinalLogo();
        StopBeginDelay();
        StopIntroAudio();
        Phase = TutorialPhase.Failed;
        if (buttons != null) buttons.DisableButtons();
        TransitionCards(null);
        tutorialFailed.Invoke();
    }

    void TransitionCards(CanvasGroup shownCard, System.Action onShown = null)
    {
        StopCardFade();
        if (isActiveAndEnabled) cardRoutine = StartCoroutine(FadeCards(shownCard, onShown));
        else HideCardsImmediate();
    }

    IEnumerator FadeCards(CanvasGroup shownCard, System.Action onShown)
    {
        float rotateStart = rotateCard != null ? rotateCard.alpha : 0f;
        float fireStart = fireCard != null ? fireCard.alpha : 0f;
        float rotateDuration = rotateCard == shownCard ? fadeInDuration : fadeOutDuration;
        float fireDuration = fireCard == shownCard ? fadeInDuration : fadeOutDuration;
        float elapsed = 0f;
        float duration = Mathf.Max(rotateDuration, fireDuration);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            FadeCard(rotateCard, shownCard, rotateStart, elapsed, rotateDuration);
            FadeCard(fireCard, shownCard, fireStart, elapsed, fireDuration);
            yield return null;
        }
        if (rotateCard != null) rotateCard.alpha = rotateCard == shownCard ? 1f : 0f;
        if (fireCard != null) fireCard.alpha = fireCard == shownCard ? 1f : 0f;
        cardRoutine = null;
        onShown?.Invoke();
    }

    static void FadeCard(CanvasGroup card, CanvasGroup shownCard, float from, float elapsed, float duration)
    {
        if (card != null)
            card.alpha = Mathf.Lerp(from, card == shownCard ? 1f : 0f, duration <= 0f ? 1f : elapsed / duration);
    }

    void StopCardFade()
    {
        if (cardRoutine != null) StopCoroutine(cardRoutine);
        cardRoutine = null;
    }

    void HideCardsImmediate()
    {
        if (rotateCard != null) rotateCard.alpha = 0f;
        if (fireCard != null) fireCard.alpha = 0f;
    }

    void OnDisable()
    {
        StopFinalLogo();
        StopFlightMusicTracking();
        StopBeginDelay();
        if (buttons != null)
        {
            buttons.DisableButtons();
            buttons.rotatePressed.RemoveListener(OnRotatePressed);
            buttons.firePressed.RemoveListener(OnFirePressed);
        }
        if (actions != null)
        {
            actions.allRotationsCompleted.RemoveListener(OnAllRotationsCompleted);
            actions.allLaunchSequencesCompleted.RemoveListener(OnAllLaunchSequencesCompleted);
            actions.actionFailed.RemoveListener(Fail);
            actions.missileLaunched.RemoveListener(OnMissileLaunched);
        }
        StopCardFade();
        HideCardsImmediate();
        Phase = TutorialPhase.Idle;
        StopIntroAudio();
    }
}
