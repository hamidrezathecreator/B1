using System.Collections;
using PrimeTween;
using UnityEngine.Serialization;
using UnityEngine;

/// <summary>
/// Lvl_1 post-tutorial sequence. Assign scene refs. No Director, no timeline clocks.
/// Missile arrival owns each strike; launch never triggers an explosion.
/// </summary>
[AddComponentMenu("B1/Lvl1 Build Level State Machine")]
public sealed class Lvl1BuildLevelStateMachine : BuildLevelStateMachineBase
{
    static readonly string[] Steps =
    {
        "Await Start (tutorial)",
        "Await Case Placement (tutorial)",
        "Missile 1 + Explosion 1 + Bridge scatter",
        "Missile 2 + Explosion 2",
        "Fall + Fade Out",
        "Load Lvl 2"
    };

    [Header("Sequence")]
    [SerializeField] Lvl1Tutorial tutorial;
    [SerializeField] string nextSceneName = BuildLevelScenes.Lvl2;

    [Header("Strike timing (real seconds)")]
    [FormerlySerializedAs("tExplosion13")]
    [SerializeField] float tFirstStrike = 2.55f;
    [Tooltip("Real seconds after the first impact before launching the second salvo. Slow motion finishes before launch.")]
    [SerializeField, Min(0f)] float secondStrikeDelay = 10f;

    [FormerlySerializedAs("fallAfterExplosionTwoSeconds")]
    [SerializeField, Min(0f)] float fallAfterSecondStrikeSeconds = 1f;

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

    [Header("First strike global slow motion: frozen impact to full speed")]
    [SerializeField, Min(0.05f)] float explosionTimeScaleRampSeconds = 6f;
    [Tooltip("Higher holds near frozen longer, then accelerates sharply.")]
    [SerializeField, Range(1f, 16f)] float slowMotionExponent = 8f;
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
    void Awake() => TrackStep(0);
    void OnEnable()
    {
        if (tutorial != null)
        {
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
        BeginAutomaticSequence(2, Mathf.Max(0f, completionMessageSeconds) + Mathf.Max(0f, tFirstStrike));
    }

    void BeginAutomaticSequence(int firstStep, float initialDelay)
    {
        if (sequenceRunning || SequenceComplete) return;
        sequenceRunning = true;
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
        if (firstStep <= 2)
        {
            TrackStep(2);
            LaunchFirstStrike();
            yield return AwaitImpact(missiles1, true);
            if (!firstStrikePlayed) { AbortSequence(); yield break; }

            while (Time.unscaledTime - firstImpactTime < secondStrikeDelay || ownsTimeScale)
                yield return null;
        }
        if (firstStep <= 3)
        {
            TrackStep(3);
            LaunchSecondStrike();
            yield return AwaitImpact(missiles2, false);
            if (!secondStrikePlayed) { AbortSequence(); yield break; }

            yield return new WaitForSecondsRealtime(fallAfterSecondStrikeSeconds);
        }
        if (firstStep <= 4)
        {
            TrackStep(4);
            yield return RunFallPhase();
        }
        TrackStep(5);
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

    protected override int NextEditorStep(int current) => current < 2 ? 2 : current + 1;

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying) return;
        switch (index)
        {
            case 2:
            case 3:
            case 4:
            case 5:
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
        Time.timeScale = 0f;
        slowMotionRoutine = StartCoroutine(RampGlobalTime());
        if (explosionOne != null) explosionOne.transform.position = position;
        PlayExplosionObject(explosionOne, ExplosionTimeScaleSettings.Profile.Identity);
        SpawnCondensationAt(explosionOne, ExplosionTimeScaleSettings.Profile.Identity);
        ShakeBuilding();
        SwapBridgeDestroyed();
        scatterOne?.Explode();
        return true; // This impact already has its explosion and cloud.
    }

    bool OnSecondMissileImpact(Vector3 position)
    {
        if (!isActiveAndEnabled || !secondStrikeLaunched || secondStrikePlayed) return false;
        secondStrikePlayed = true;
        RestoreTimeScale();
        if (explosionTwo != null) explosionTwo.transform.position = position;
        PlayExplosionObject(explosionTwo, ExplosionTimeScaleSettings.Profile.Identity);
        SpawnCondensationAt(explosionTwo, ExplosionTimeScaleSettings.Profile.Identity);
        scatterTwo?.Explode();
        scatterObjects?.Explode();
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
            var main = ps.main;
            main.useUnscaledTime = false;
            ps.Clear(true);
            ps.Play(true);
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
        yield return null; // Keep a frozen impact frame.
        while (elapsed < explosionTimeScaleRampSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, explosionTimeScaleRampSeconds));
            float k = Mathf.Max(1f, slowMotionExponent);
            float scale = (Mathf.Exp(k * t) - 1f) / (Mathf.Exp(k) - 1f);
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
            tutorial.Started -= NotifyTutorialStartPressed;
            tutorial.CasePickedUp -= NotifyTutorialCasePhase;
            tutorial.PlacementCompleted -= OnTutorialPlacementCompleted;
        }
        StopSequence();
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
        if (preferShockFall && shockFall != null) shockFall.BeginFall();
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

    public void NotifyTutorialStartPressed() => TrackStep(1);
    public void NotifyTutorialCasePhase() => TrackStep(1);

    public void EditorTestRunFullPostPlacement()
    {
        if (!Application.isPlaying) return;
        if (sequenceRunning || SequenceComplete) return;
        BeginAutomaticSequence(2, 0f);
    }

    public void EditorTestPlayExplosionOne()
    {
        if (!Application.isPlaying) return;
        StopSequence();
        BeginAutomaticSequence(2, 0f);
    }

    public void EditorTestPlayExplosionTwo()
    {
        if (!Application.isPlaying) return;
        StopSequence();
        BeginAutomaticSequence(3, 0f);
    }

    public void EditorTestShowGlassShard() => ShowGlassShardOverlay();
}
