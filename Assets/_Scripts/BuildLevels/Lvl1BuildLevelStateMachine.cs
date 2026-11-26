using System.Collections;
using PrimeTween;
using UnityEngine.Serialization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lvl_1 post-tutorial sequence. Assign scene refs. No Director, no timeline clocks.
/// Missile arrival owns each strike; launch never triggers an explosion.
/// </summary>
[AddComponentMenu("B1/Lvl1 Build Level State Machine")]
public sealed class Lvl1BuildLevelStateMachine : BuildLevelStateMachineBase
{
    static readonly string[] Steps =
    {
        "Helicopter Start (await Mission Start)",
        "Fade Out + Transfer To Tutorial + Fade In",
        "Bridge Tutorial (move + pickup + placement)",
        "Missile 1 + Explosion 1 + Bridge scatter",
        "Missile 2 + Explosion 2 + Delayed bridge scatter",
        "Fall + Fade Out",
        "Load Lvl 2"
    };

    const int HelicopterStep = 0;
    const int TransferStep = 1;
    const int TutorialStep = 2;
    const int FirstStrikeStep = 3;
    const int SecondStrikeStep = 4;
    const int FallStep = 5;
    const int LoadStep = 6;

    [Header("Sequence")]
    [SerializeField] Lvl1Tutorial tutorial;
    [SerializeField] string nextSceneName = BuildLevelScenes.Lvl2;

    [Header("Helicopter opening")]
    [SerializeField] HelicopterHover helicopterIntro;
    [SerializeField, Min(0f)] float helicopterFadeOutSeconds = 0.65f;
    [SerializeField, Min(0f)] float helicopterBlackSeconds = 0.15f;
    [SerializeField, Min(0f)] float helicopterFadeInSeconds = 0.65f;
    Coroutine helicopterTransfer;
    CanvasGroup helicopterFade;

    [Header("Strike timing (real seconds)")]
    [FormerlySerializedAs("tExplosion13")]
    [SerializeField] float tFirstStrike = 2.55f;
    [Tooltip("Real seconds after the first impact before launching the second salvo. Slow motion finishes before launch.")]
    [SerializeField, Min(0f)] float secondStrikeDelay = 2.5f;

    [SerializeField, Min(0f)] float secondStrikeScatterDelaySeconds = 0.5f;

    [FormerlySerializedAs("fallAfterExplosionTwoSeconds")]
    [SerializeField, Min(0f)] float fallAfterSecondStrikeSeconds = 0.15f;

    [Header("Refs")]
    [SerializeField] GameObject explosionOne;
    [SerializeField] GameObject explosionTwo;
    [SerializeField] MissileLauncherSystem missiles1;
    [SerializeField] MissileLauncherSystem missiles2;
    [SerializeField] SceneObjectSwapper swapBridge;
    [SerializeField] GameObject intactBridge;
    [SerializeField] GameObject destroyedBridge;
    [SerializeField] ExplosionScatter scatterOne;
    [SerializeField] ExplosionScatter scatterTwo;
    [SerializeField] ExplosionScatter scatterObjects;
    [SerializeField] XrShockFallSequence shockFall;

    [Header("Condensation cloud")]
    [SerializeField] bool spawnCondensationCloud = true;
    [SerializeField, Min(0.01f)] float condensationCloudRadius = 400f;
    [SerializeField] float condensationCloudLifetime = 5f;

    [Header("First strike global slow motion")]
    [SerializeField, Min(0f)] float slowMotionDelaySeconds = 0.5f;
    [SerializeField, Range(0.01f, 1f)] float minimumTimeScale = 0.08f;
    [SerializeField, Min(0.05f)] float explosionTimeScaleRampSeconds = 2f;
    [Tooltip("Higher holds near frozen longer, then accelerates sharply.")]
    [SerializeField, Range(1f, 16f)] float slowMotionExponent = 8f;
    [Header("Post-tutorial jet pass")]
    [SerializeField] AudioClip fighterJetSound;
    [SerializeField, Range(0f, 1f)] float fighterJetVolume = 0.85f;
    [Header("Building shake on every strike")]
    [SerializeField] Transform buildingShakeTarget;
    [SerializeField] Vector3 buildingShakeStrength = new Vector3(0.12f, 0.08f, 0.12f);
    [SerializeField, Min(0.01f)] float buildingShakeSeconds = 0.8f;
    [SerializeField, Min(1f)] float buildingShakeFrequency = 18f;

    [Header("Glass shard UI (after 2nd explosion)")]
    [SerializeField] VrFullscreenSpriteOverlay glassShardOverlay;
    [SerializeField] Texture2D glassShardTexture;
    [Min(0f)] [SerializeField] float glassShardFadeInSeconds = 0.35f;
    [SerializeField] bool hideGlassShardOnSceneLoad = true;

    [Header("Fall")]
    [SerializeField] bool preferShockFall = true;
    [Min(0f)] [SerializeField] float fadeOutSeconds = 1f;

    [SerializeField, Min(0f)] float fadeAfterFallSeconds = 0.65f;
    [SerializeField, Min(0.1f)] float maximumFallSeconds = 8f;
    Coroutine slowMotionRoutine;
    bool ownsTimeScale;
    float savedFixedDeltaTime;
    bool firstStrikePlayed, secondStrikePlayed;
    bool firstStrikeLaunched, secondStrikeLaunched;
    float firstImpactTime;
    [SerializeField, Min(1f)] float missileImpactTimeoutSeconds = 30f;
    System.Func<Vector3, bool> previousFirstImpactHandler, previousSecondImpactHandler;
    bool previousFirstPlayback, previousSecondPlayback;
    Tween buildingShake;
    Vector3 buildingRestPosition;
    Coroutine sequenceRoutine;
    bool sequenceRunning;


    protected override string[] StepNames => Steps;

    public Lvl1Tutorial Tutorial => tutorial;
    public event System.Action FirstExplosionPlayed;
    public event System.Action FighterJetPlayed;
    void Awake() => TrackStep(HelicopterStep);
    void OnEnable()
    {
        if (tutorial != null)
        {
            tutorial.StartRequested += BeginHelicopterTransfer;
            tutorial.Started += NotifyTutorialStartPressed;
            tutorial.CasePickedUp += NotifyTutorialCasePhase;
            tutorial.PlacementCompleted += OnTutorialPlacementCompleted;
        }
        if (missiles1 != null)
        {
            previousFirstImpactHandler = missiles1.ImpactPresentationOverride;
            previousFirstPlayback = missiles1.FullSpeedImpactPlayback;
            missiles1.ImpactPresentationOverride = OnFirstMissileImpact;
            missiles1.FullSpeedImpactPlayback = true;
        }
        if (missiles2 != null)
        {
            previousSecondImpactHandler = missiles2.ImpactPresentationOverride;
            previousSecondPlayback = missiles2.FullSpeedImpactPlayback;
            missiles2.ImpactPresentationOverride = OnSecondMissileImpact;
            missiles2.FullSpeedImpactPlayback = true;
        }
    }

    void OnTutorialPlacementCompleted() => NotifyTutorialCasePlaced(tutorial.CompletionMessageSeconds);

    public void NotifyTutorialCasePlaced(float completionMessageSeconds = 0f)
    {
        if (tutorial == null || !tutorial.HasCompletedPlacement) return;
        BeginAutomaticSequence(FirstStrikeStep, Mathf.Max(0f, completionMessageSeconds) + Mathf.Max(0f, tFirstStrike));
    }

    void BeginHelicopterTransfer()
    {
        if (helicopterTransfer != null) return;
        if (helicopterIntro == null)
        {
            Debug.LogError("Lvl1: assign Helicopter Intro on the state machine before starting the mission.", this);
            tutorial?.CancelStartTransition();
            return;
        }
        if (!helicopterIntro.IsRiding)
        {
            tutorial?.CompleteStartTransition();
            return;
        }
        if (helicopterIntro.PassengerCamera == null)
        {
            Debug.LogError("Lvl1: assign the helicopter passenger's headset camera before starting the mission.", this);
            tutorial?.CancelStartTransition();
            return;
        }
        EnsureHelicopterFade();
        TrackStep(TransferStep);
        helicopterTransfer = StartCoroutine(TransferFromHelicopter());
    }

    IEnumerator TransferFromHelicopter()
    {
        yield return FadeHelicopterView(1f, helicopterFadeOutSeconds);
        yield return null;
        helicopterIntro.EndRide(keepMovementLocked: true);
        helicopterIntro.DisableAfterTransfer();
        if (helicopterBlackSeconds > 0f) yield return new WaitForSecondsRealtime(helicopterBlackSeconds);
        yield return FadeHelicopterView(0f, helicopterFadeInSeconds);
        helicopterIntro.ReleaseMovementLock();
        helicopterTransfer = null;
        tutorial?.CompleteStartTransition();
    }

    void LateUpdate()
    {
        if (helicopterTransfer != null) helicopterIntro?.MaintainPassengerLock();
    }

    void EnsureHelicopterFade()
    {
        if (helicopterFade != null) return;
        var camera = helicopterIntro.PassengerCamera;
        var overlay = new GameObject("Helicopter Mission Fade", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasGroup), typeof(Image));
        overlay.layer = LayerMask.NameToLayer("UI");
        var rect = overlay.GetComponent<RectTransform>();
        rect.SetParent(camera.transform, false);
        float distance = Mathf.Max(camera.nearClipPlane + 0.03f, 0.1f);
        rect.localPosition = Vector3.forward * distance;
        rect.localScale = Vector3.one * (distance * 0.005f);
        rect.sizeDelta = new Vector2(2000f, 2000f);
        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        var image = overlay.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        helicopterFade = overlay.GetComponent<CanvasGroup>();
        helicopterFade.alpha = 0f;
        helicopterFade.blocksRaycasts = false;
        helicopterFade.interactable = false;
    }

    IEnumerator FadeHelicopterView(float target, float seconds)
    {
        float start = helicopterFade.alpha;
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            helicopterFade.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / seconds));
            if (target > 0f) helicopterIntro.SetRideAudioLevel(1f - helicopterFade.alpha);
            yield return null;
        }
        helicopterFade.alpha = target;
        if (target > 0f) helicopterIntro.SetRideAudioLevel(0f);
    }

    void CancelHelicopterTransfer()
    {
        if (helicopterTransfer == null) return;
        StopCoroutine(helicopterTransfer);
        helicopterTransfer = null;
        if (helicopterFade != null) helicopterFade.alpha = 0f;
        if (helicopterIntro != null && helicopterIntro.IsRiding)
        {
            helicopterIntro.SetRideAudioLevel(1f);
            tutorial?.CancelStartTransition();
            TrackStep(HelicopterStep);
        }
        else
        {
            helicopterIntro?.ReleaseMovementLock();
            tutorial?.CompleteStartTransition();
            TrackStep(TutorialStep);
        }
    }

    void CompleteHelicopterOpeningImmediately()
    {
        if (helicopterTransfer != null) StopCoroutine(helicopterTransfer);
        helicopterTransfer = null;
        if (helicopterFade != null) helicopterFade.alpha = 0f;
        helicopterIntro?.EndRide(keepMovementLocked: true);
        helicopterIntro?.DisableAfterTransfer();
        helicopterIntro?.ReleaseMovementLock();
        tutorial?.CompleteStartTransition();
    }

    void OnDestroy()
    {
        if (helicopterFade != null) Destroy(helicopterFade.gameObject);
    }

    void BeginAutomaticSequence(int firstStep, float initialDelay)
    {
        if (sequenceRunning || SequenceComplete) return;
        sequenceRunning = true;
        if (firstStep <= FirstStrikeStep)
        {
            PlayerImpactAudio.PlayFighterJet(fighterJetSound, fighterJetVolume);
            FighterJetPlayed?.Invoke();
        }
        sequenceRoutine = StartCoroutine(RunSequence(firstStep, initialDelay));
    }

    public IEnumerator RunPostPlacementSequence()
    {
        NotifyTutorialCasePlaced();
        while (sequenceRunning) yield return null;
    }
    IEnumerator RunSequence(int firstStep, float initialDelay)
    {
        if (initialDelay > 0f) yield return new WaitForSecondsRealtime(initialDelay);
        if (firstStep <= FirstStrikeStep)
        {
            TrackStep(FirstStrikeStep);
            LaunchFirstStrike();
            yield return AwaitImpact(missiles1, true);
            if (!firstStrikePlayed) { AbortSequence(); yield break; }

            while (Time.unscaledTime - firstImpactTime < secondStrikeDelay || ownsTimeScale)
                yield return null;
        }
        if (firstStep <= SecondStrikeStep)
        {
            TrackStep(SecondStrikeStep);
            LaunchSecondStrike();
            yield return AwaitImpact(missiles2, false);
            if (!secondStrikePlayed) { AbortSequence(); yield break; }

            yield return new WaitForSecondsRealtime(secondStrikeScatterDelaySeconds);
            scatterTwo?.Explode();
            scatterObjects?.Explode();
            yield return new WaitForSecondsRealtime(fallAfterSecondStrikeSeconds);
        }
        if (firstStep <= FallStep)
        {
            TrackStep(FallStep);
            yield return RunFallPhase();
        }
        TrackStep(LoadStep);
        yield return LoadNextScene();
        sequenceRunning = false;
        sequenceRoutine = null;
        SequenceComplete = true;
    }

    IEnumerator AwaitImpact(MissileLauncherSystem launcher, bool first)
    {
        if (launcher == null || !(first ? firstStrikeLaunched : secondStrikeLaunched)) yield break;
        float deadline = Time.unscaledTime + Mathf.Max(missileImpactTimeoutSeconds, launcher.FlightDuration + 2f);
        while (!(first ? firstStrikePlayed : secondStrikePlayed) && launcher.isActiveAndEnabled &&
               Time.unscaledTime < deadline)
            yield return null;
        if (!(first ? firstStrikePlayed : secondStrikePlayed))
            Debug.LogError("Lvl1: missile did not reach its target; stopping the sequence.", this);
    }

    void AbortSequence()
    {
        sequenceRunning = false;
        sequenceRoutine = null;
        RestoreTimeScale();
    }

    protected override void PrepareForEditorStepEnter() => StopSequence();

    protected override int NextEditorStep(int current) => current + 1;

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying) return;
        if (index >= TutorialStep) CompleteHelicopterOpeningImmediately();
        switch (index)
        {
            case TransferStep:
                tutorial?.EditorTestPressStart();
                break;
            case FirstStrikeStep:
            case SecondStrikeStep:
            case FallStep:
            case LoadStep:
                BeginAutomaticSequence(index, 0f);
                break;
        }
    }

    void StopSequence()
    {
        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }
        sequenceRunning = false;
        RestoreTimeScale();
    }

    void LaunchFirstStrike()
    {
        if (firstStrikeLaunched || firstStrikePlayed) return;
        if (missiles1 == null || !missiles1.TryFire())
        {
            Debug.LogError("Lvl1: first missile needs an enabled launcher, missile prefab and target.", this);
            return;
        }
        firstStrikeLaunched = true;
    }

    void LaunchSecondStrike()
    {
        if (secondStrikeLaunched || secondStrikePlayed) return;
        RestoreTimeScale();
        if (missiles2 == null || !missiles2.TryFire())
        {
            Debug.LogError("Lvl1: second missile needs an enabled launcher, missile prefab and target.", this);
            return;
        }
        secondStrikeLaunched = true;
    }

    bool OnFirstMissileImpact(Vector3 position)
    {
        if (!isActiveAndEnabled || !firstStrikeLaunched || firstStrikePlayed) return false;
        firstStrikePlayed = true;
        firstImpactTime = Time.unscaledTime;
        savedFixedDeltaTime = Time.fixedDeltaTime;
        ownsTimeScale = true;
        slowMotionRoutine = StartCoroutine(RampGlobalTime());
        if (explosionOne != null) explosionOne.transform.position = position;
        PlayExplosionObject(explosionOne, ExplosionTimeScaleSettings.Profile.Identity);
        SpawnCondensationAt(explosionOne, ExplosionTimeScaleSettings.Profile.Identity);
        ShakeBuilding();
        SwapBridgeDestroyed();
        scatterOne?.Explode();
        FirstExplosionPlayed?.Invoke();
        return true; // This impact already has its explosion and cloud.
    }

    bool OnSecondMissileImpact(Vector3 position)
    {
        if (!isActiveAndEnabled || !secondStrikeLaunched || secondStrikePlayed) return false;
        secondStrikePlayed = true;
        RestoreTimeScale();
        scatterOne?.StopAndDisablePieces();
        if (explosionTwo != null) explosionTwo.transform.position = position;
        PlayExplosionObject(explosionTwo, ExplosionTimeScaleSettings.Profile.Identity);
        SpawnCondensationAt(explosionTwo, ExplosionTimeScaleSettings.Profile.Identity);
        ShakeBuilding();
        ShowGlassShardOverlay();
        return true;
    }

    void PlayExplosionObject(GameObject explosion, ExplosionTimeScaleSettings.Profile timeScale)
    {
        if (explosion == null) return;
        VrExplosionOptimizer.Apply(explosion);
        var linger = explosion.GetComponent<ExplosionSlowAfterBurst>();
        if (linger != null) linger.enabled = false;

        explosion.SetActive(true);
        foreach (var ps in explosion.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (!ps.gameObject.activeInHierarchy) continue;
            var main = ps.main;
            main.useUnscaledTime = false;
            ps.Clear(false);
            ps.Play(false);
        }
        foreach (var audio in explosion.GetComponentsInChildren<AudioSource>(true))
            if (audio.clip != null && !audio.isPlaying) audio.Play();

        var ramp = explosion.GetComponent<ExplosionPlaybackRamp>();
        if (ramp == null) ramp = explosion.AddComponent<ExplosionPlaybackRamp>();
        ramp.Play(timeScale);
    }

    IEnumerator RampGlobalTime()
    {
        float elapsed = 0f;
        if (slowMotionDelaySeconds > 0f) yield return new WaitForSecondsRealtime(slowMotionDelaySeconds);
        Time.timeScale = minimumTimeScale;
        Time.fixedDeltaTime = savedFixedDeltaTime * minimumTimeScale;
        float rampStartedAt = Time.unscaledTime;
        while (elapsed < explosionTimeScaleRampSeconds)
        {
            elapsed = Time.unscaledTime - rampStartedAt;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, explosionTimeScaleRampSeconds));
            float k = Mathf.Max(1f, slowMotionExponent);
            float scale = Mathf.Lerp(minimumTimeScale, 1f, (Mathf.Exp(k * t) - 1f) / (Mathf.Exp(k) - 1f));
            Time.timeScale = scale;
            Time.fixedDeltaTime = savedFixedDeltaTime * Mathf.Max(0.01f, scale);
            yield return null;
        }
        FinishTimeScale();
    }
    void RestoreTimeScale()
    {
        if (slowMotionRoutine != null) StopCoroutine(slowMotionRoutine);
        FinishTimeScale();
    }
    void FinishTimeScale()
    {
        if (ownsTimeScale) { Time.timeScale = 1f; Time.fixedDeltaTime = savedFixedDeltaTime; }
        ownsTimeScale = false;
        slowMotionRoutine = null;
    }
    void ShakeBuilding()
    {
        if (buildingShakeTarget == null) return;
        if (buildingShake.isAlive) { buildingShake.Stop(); buildingShakeTarget.localPosition = buildingRestPosition; }
        buildingRestPosition = buildingShakeTarget.localPosition;
        buildingShake = Tween.ShakeLocalPosition(buildingShakeTarget, buildingShakeStrength,
            buildingShakeSeconds, buildingShakeFrequency, useUnscaledTime: false);
    }
    void OnDisable()
    {
        if (tutorial != null)
        {
            tutorial.StartRequested -= BeginHelicopterTransfer;
            tutorial.Started -= NotifyTutorialStartPressed;
            tutorial.CasePickedUp -= NotifyTutorialCasePhase;
            tutorial.PlacementCompleted -= OnTutorialPlacementCompleted;
        }
        StopSequence();
        CancelHelicopterTransfer();
        if (missiles1 != null && missiles1.ImpactPresentationOverride == OnFirstMissileImpact)
        {
            missiles1.ImpactPresentationOverride = previousFirstImpactHandler;
            missiles1.FullSpeedImpactPlayback = previousFirstPlayback;
        }
        if (missiles2 != null && missiles2.ImpactPresentationOverride == OnSecondMissileImpact)
        {
            missiles2.ImpactPresentationOverride = previousSecondImpactHandler;
            missiles2.FullSpeedImpactPlayback = previousSecondPlayback;
        }
        if (buildingShake.isAlive)
        {
            buildingShake.Stop();
            if (buildingShakeTarget != null) buildingShakeTarget.localPosition = buildingRestPosition;
        }
    }

    void SpawnCondensationAt(GameObject explosion, ExplosionTimeScaleSettings.Profile timeScale)
    {
        if (!spawnCondensationCloud || explosion == null) return;
        if (CondensationCloudSystem.Instance == null) return;
        CondensationCloudSystem.SpawnAt(
            explosion.transform.position, condensationCloudRadius, condensationCloudLifetime, timeScale);
    }

    void SwapBridgeDestroyed()
    {
        if (intactBridge != null) intactBridge.SetActive(false);
        if (destroyedBridge != null) destroyedBridge.SetActive(true);
        swapBridge?.Swap();
    }

    IEnumerator RunFallPhase()
    {
        if (preferShockFall && shockFall != null)
        {
            shockFall.BeginFall();
            float deadline = Time.unscaledTime + maximumFallSeconds;
            while (shockFall.IsRunning && Time.unscaledTime < deadline) yield return null;
        }
        yield return new WaitForSecondsRealtime(fadeAfterFallSeconds);
        // The drop continues through the scene transition fade.
    }

    void ShowGlassShardOverlay()
    {

        if (glassShardOverlay == null)
        {
            var overlay = new GameObject("Glass Shard Overlay");
            overlay.transform.SetParent(transform, false);
            glassShardOverlay = overlay.AddComponent<VrFullscreenSpriteOverlay>();
        }
        if (glassShardTexture != null) glassShardOverlay.SetTexture(glassShardTexture);
        glassShardOverlay.Show(glassShardFadeInSeconds);
    }

    IEnumerator LoadNextScene()
    {
        RestoreTimeScale();
        if (string.IsNullOrWhiteSpace(nextSceneName) || !Application.CanStreamedLevelBeLoaded(nextSceneName))
            yield break;

        SceneTransitionRunner.Begin(nextSceneName, true, 0f, fadeOutSeconds, 0.4f, 0.6f);
        yield return new WaitForSecondsRealtime(fadeOutSeconds);
        if (hideGlassShardOnSceneLoad && glassShardOverlay != null) glassShardOverlay.Hide(0f);
    }

    public void NotifyTutorialStartPressed() => TrackStep(TutorialStep);
    public void NotifyTutorialCasePhase() => TrackStep(TutorialStep);

    public void EditorTestRunFullPostPlacement()
    {
        if (!Application.isPlaying) return;
        if (sequenceRunning || SequenceComplete) return;
        CompleteHelicopterOpeningImmediately();
        BeginAutomaticSequence(FirstStrikeStep, 0f);
    }

    public void EditorTestPlayExplosionOne()
    {
        if (!Application.isPlaying) return;
        StopSequence();
        CompleteHelicopterOpeningImmediately();
        BeginAutomaticSequence(FirstStrikeStep, 0f);
    }

    public void EditorTestPlayExplosionTwo()
    {
        if (!Application.isPlaying) return;
        StopSequence();
        CompleteHelicopterOpeningImmediately();
        BeginAutomaticSequence(SecondStrikeStep, 0f);
    }

    public void EditorTestShowGlassShard() => ShowGlassShardOverlay();
}
