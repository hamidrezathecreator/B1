using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>
/// Lvl_1 post-tutorial sequence mirroring B1_V2.playable beats via scene receivers (no Director).
/// </summary>
[AddComponentMenu("B1/Lvl1 Build Level State Machine")]
public sealed class Lvl1BuildLevelStateMachine : BuildLevelStateMachineBase
{
    static readonly string[] Steps =
    {
        "Await Start (tutorial)",
        "Await Case Placement (tutorial)",
        "B1_V2: Missile 1 + Swaps + Explosion 13",
        "B1_V2: Bridge Collapse (scatter waves)",
        "B1_V2: Half / Full Blink",
        "B1_V2: Missile 2 + Explosion Two",
        "Fall + Fog Shock",
        "Close Eyes + Load Lvl 2"
    };

    [Header("Sequence")]
    [SerializeField] bool replaceTimeline = true;
    [Tooltip("Keeps the Director component off while this level runs.")]
    [SerializeField] bool disableDirectorOnLoad = true;
    [SerializeField] string directorObjectName = "Director";
    [SerializeField] string nextSceneName = BuildLevelScenes.Lvl2;

    [Header("B1_V2 timing (seconds from sequence start)")]
    [SerializeField] float tExplosion13 = 2.55f;
    [SerializeField] float tMissile1 = 2.867f;
    [SerializeField] float tModelSwap = 3.367f;
    [SerializeField] float tSwapBridge = 3.633f;
    [SerializeField] float tSwapSprites = 4f;
    [SerializeField] float tBridgeFallTwo = 3.633f;
    [SerializeField] float tBridgeFallOne = 4.383f;
    [SerializeField] float tOthersBridgeFall = 4.933f;
    [SerializeField] float tHalfBlink = 8.45f;
    [SerializeField] float tFullBlink = 10.7f;
    [SerializeField] float tMissile2 = 12.567f;
    [SerializeField] float tExplosionTwo = 12.567f;
    [SerializeField] float tSlowEyeClose = 13.1f;
    [SerializeField] float fallAfterExplosionTwoSeconds = 3f;

    [Header("Explosion object names")]
    [SerializeField] string explosionOneName = "Explosion 13";
    [SerializeField] string explosionTwoName = "Explosion_Two";
    [SerializeField] string[] intactBridgeNames = { "BridgMain", "BridgeMain", "Bridge Main" };
    [SerializeField] string destroyedBridgeName = "BridgeDestroyed";

    [Header("Glass shard UI (after 2nd explosion)")]
    [Tooltip("Optional existing overlay component. Created at runtime on this object if empty.")]
    [SerializeField] VrFullscreenSpriteOverlay glassShardOverlay;
    [Tooltip("Assign your glass shard PNG here when ready.")]
    [SerializeField] Texture2D glassShardTexture;
    [Min(0f)] [SerializeField] float glassShardFadeInSeconds = 0.35f;
    [SerializeField] bool hideGlassShardOnSceneLoad = true;

    [Header("Fall")]
    [SerializeField] XrShockFallSequence shockFall;
    [SerializeField] bool preferShockFall = true;
    [Min(0.1f)] [SerializeField] float maxEyeCloseSeconds = 2.5f;
    [Min(0f)] [SerializeField] float fadeOutSeconds = 1f;

    MissileLauncherSystem missiles1;
    MissileLauncherSystem missiles2;
    SceneObjectSwapper swapBridge;
    ModelCrossfadeController modelSwap;
    SpriteCrossfadeController spriteSwap;
    ExplosionScatter scatterObjects;
    ExplosionScatter scatterOne;
    ExplosionScatter scatterTwo;
    VrBlinkEffect blink;
    GameObject explosionOne;

    Coroutine sequenceRoutine;
    bool sequenceRunning;
    float sequenceClock;
    ExplosionCue fallCue;
    float fallExplosionShownAt = -1f;

    public bool ReplaceTimeline => replaceTimeline;

    protected override string[] StepNames => Steps;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != BuildLevelScenes.Lvl1) return;
        if (FindAnyObjectByType<Lvl1BuildLevelStateMachine>() != null) return;
        var go = new GameObject("Lvl1 Build Level State Machine");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<Lvl1BuildLevelStateMachine>();
    }

    void Awake()
    {
        ResolveRefs();
        if (disableDirectorOnLoad) DisableDirector();
        SetStep(0);
    }

    void ResolveRefs()
    {
        explosionOne = FindByName(explosionOneName);
        missiles1 = FindComponentOn<MissileLauncherSystem>("Missiles_1");
        missiles2 = FindComponentOn<MissileLauncherSystem>("Missiles_2");
        swapBridge = FindComponentOn<SceneObjectSwapper>("SwapBridgeOne");
        modelSwap = FindComponentOn<ModelCrossfadeController>("CharactersSwapSystem_Two");
        spriteSwap = FindComponentOn<SpriteCrossfadeController>("SpriteCrossFader");
        scatterObjects = FindComponentOn<ExplosionScatter>("BridgeFallScatterObjects");
        scatterOne = FindComponentOn<ExplosionScatter>("BridgeFallScatterOne");
        scatterTwo = FindComponentOn<ExplosionScatter>("BridgeFallScatterTwo");
        blink = FindAnyObjectByType<VrBlinkEffect>();
        if (shockFall == null) shockFall = FindAnyObjectByType<XrShockFallSequence>();
        if (preferShockFall && shockFall == null)
        {
            var host = new GameObject("XR Shock Fall");
            shockFall = host.AddComponent<XrShockFallSequence>();
        }
    }

    public IEnumerator RunPostPlacementSequence()
    {
        if (sequenceRunning) yield break;
        sequenceRunning = true;
        DisableDirector();
        ResetScatters();
        WatchFallExplosion();
        yield return RunB1V2Beats();
        sequenceRunning = false;
        SequenceComplete = true;
    }

    IEnumerator RunB1V2Beats()
    {
        sequenceClock = 0f;
        var sequenceStart = Time.time;
        yield return WaitUntil(tExplosion13);
        SetStep(2);
        PlayFirstExplosion();

        yield return WaitUntil(tMissile1);
        missiles1?.Fire();

        yield return WaitUntil(tModelSwap);
        modelSwap?.BroadcastToSecond();

        yield return WaitUntil(tSwapBridge);
        SetBridgeDestroyed(true);
        swapBridge?.Swap();

        yield return WaitUntil(tBridgeFallTwo);
        scatterTwo?.ExplodePartly();

        yield return WaitUntil(tSwapSprites);
        spriteSwap?.BroadcastCrossfade();

        yield return WaitUntil(tBridgeFallOne);
        SetStep(3);
        scatterOne?.ExplodePartly();

        yield return WaitUntil(tOthersBridgeFall);
        scatterObjects?.Explode();

        yield return WaitUntil(tHalfBlink);
        SetStep(4);
        blink?.HalfBlink();

        yield return WaitUntil(tFullBlink);
        blink?.FullBlink();

        yield return WaitUntil(tMissile2);
        SetStep(5);
        missiles2?.Fire();
        PlaySecondExplosion();
        ShowGlassShardOverlay();

        yield return new WaitForSeconds(fallAfterExplosionTwoSeconds);
        SetStep(6);
        yield return RunFallPhase();

        while (Time.time - sequenceStart < tSlowEyeClose) yield return null;
        SetStep(7);
        yield return CloseEyesPhase();
        yield return LoadNextScene();
    }

    IEnumerator WaitUntil(float targetTime)
    {
        var wait = Mathf.Max(0f, targetTime - sequenceClock);
        if (wait > 0f) yield return new WaitForSeconds(wait);
        sequenceClock = targetTime;
    }

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying || sequenceRunning) return;
        switch (index)
        {
            case 2:
                if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
                sequenceRoutine = StartCoroutine(RunPostPlacementSequence());
                break;
            case 3:
                scatterTwo?.ExplodePartly();
                scatterOne?.ExplodePartly();
                scatterObjects?.Explode();
                break;
            case 4:
                blink?.HalfBlink();
                blink?.FullBlink();
                break;
            case 5:
                missiles2?.Fire();
                PlaySecondExplosion();
                ShowGlassShardOverlay();
                break;
            case 6:
                if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
                sequenceRoutine = StartCoroutine(RunFallPhase());
                break;
            case 7:
                if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
                sequenceRoutine = StartCoroutine(CloseThenLoad());
                break;
        }
    }

    IEnumerator CloseThenLoad()
    {
        yield return CloseEyesPhase();
        yield return LoadNextScene();
    }

    void DisableDirector()
    {
        var director = FindDirector();
        if (director == null) return;
        director.Stop();
        director.time = 0;
        director.enabled = false;
    }

    void PlayFirstExplosion()
    {
        if (explosionOne == null) explosionOne = FindByName(explosionOneName);
        if (explosionOne == null) return;
        VrExplosionOptimizer.Apply(explosionOne);
        explosionOne.SetActive(true);
        foreach (var ps in explosionOne.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(true);
            ps.Play(true);
        }
        foreach (var audio in explosionOne.GetComponentsInChildren<AudioSource>(true))
            if (audio.clip != null && !audio.isPlaying) audio.Play();
    }

    void PlaySecondExplosion()
    {
        var two = FindByName(explosionTwoName);
        if (two == null) return;
        VrExplosionOptimizer.Apply(two);
        two.SetActive(true);
        foreach (var ps in two.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(true);
            ps.Play(true);
        }
        if (fallExplosionShownAt < 0f) fallExplosionShownAt = Time.time;
    }

    IEnumerator RunFallPhase()
    {
        if (preferShockFall && shockFall != null)
        {
            shockFall.BeginFall();
            while (shockFall.IsRunning) yield return null;
        }
    }

    IEnumerator CloseEyesPhase()
    {
        blink ??= FindAnyObjectByType<VrBlinkEffect>();
        if (blink == null) blink = new GameObject("Eye Blink").AddComponent<VrBlinkEffect>();
        blink.CloseSlowly();
        var waited = 0f;
        while (blink != null && blink.Amount < 0.99f && waited < maxEyeCloseSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    void ShowGlassShardOverlay()
    {
        if (glassShardTexture == null) return;
        EnsureGlassShardOverlay();
        if (glassShardOverlay == null) return;
        glassShardOverlay.SetTexture(glassShardTexture);
        glassShardOverlay.Show(glassShardFadeInSeconds);
    }

    void EnsureGlassShardOverlay()
    {
        if (glassShardOverlay != null) return;
        glassShardOverlay = GetComponent<VrFullscreenSpriteOverlay>();
        if (glassShardOverlay == null)
            glassShardOverlay = gameObject.AddComponent<VrFullscreenSpriteOverlay>();
    }

    IEnumerator LoadNextScene()
    {
        if (hideGlassShardOnSceneLoad && glassShardOverlay != null)
            glassShardOverlay.Hide(0.15f);
        if (shockFall != null) shockFall.RestoreFog();
        if (string.IsNullOrWhiteSpace(nextSceneName) || !Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogWarning("Lvl1 state machine: next scene not in build settings.", this);
            yield break;
        }

        SceneTransitionRunner.Begin(nextSceneName, true, 0f, Mathf.Min(0.3f, fadeOutSeconds), 0.4f, 0.6f);
    }

    void WatchFallExplosion()
    {
        fallExplosionShownAt = -1f;
        var go = FindByName(explosionTwoName);
        if (go == null) { fallCue = null; return; }
        VrExplosionOptimizer.Apply(go);
        fallCue = ExplosionCue.AddTo(go);
        fallCue.Shown -= OnFallExplosionShown;
        fallCue.Shown += OnFallExplosionShown;
    }

    void OnFallExplosionShown(ExplosionCue _) => fallExplosionShownAt = Time.time;

    void SetBridgeDestroyed(bool destroyed)
    {
        var intact = FindByName(intactBridgeNames);
        var broken = FindByName(destroyedBridgeName);
        if (intact != null) intact.SetActive(!destroyed);
        if (broken != null) broken.SetActive(destroyed);
    }

    static void ResetScatters()
    {
        foreach (var scatter in FindObjectsByType<ExplosionScatter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (scatter.gameObject.scene.IsValid()) scatter.ResetExplosion();
    }

    PlayableDirector FindDirector()
    {
        if (string.IsNullOrWhiteSpace(directorObjectName)) return null;
        var go = GameObject.Find(directorObjectName);
        return go != null ? go.GetComponent<PlayableDirector>() : null;
    }

    static GameObject FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
        return null;
    }

    static GameObject FindByName(string[] names)
    {
        if (names == null) return null;
        foreach (var n in names)
        {
            var go = FindByName(n);
            if (go != null) return go;
        }
        return null;
    }

    static T FindComponentOn<T>(string objectName) where T : Component
    {
        var go = FindByName(objectName);
        return go != null ? go.GetComponent<T>() : null;
    }

    public void NotifyTutorialStartPressed() => SetStep(0);
    public void NotifyTutorialCasePhase() => SetStep(1);

    public void EditorTestRunFullPostPlacement()
    {
        if (!Application.isPlaying) return;
        if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
        sequenceRoutine = StartCoroutine(RunPostPlacementSequence());
    }

    public void EditorTestShowGlassShard() => ShowGlassShardOverlay();
}