using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Lvl2_MissileTunnel flow mirror with editor step buttons.</summary>
[AddComponentMenu("B1/Lvl2 Build Level State Machine")]
public sealed class Lvl2BuildLevelStateMachine : BuildLevelStateMachineBase
{
    static readonly string[] Steps =
    {
        "Fade In",
        "Await Elevator Floor Button",
        "Elevator Ride + Doors",
        "Await Tablet Checklist (3)",
        "Load Lvl 3"
    };

    ElevatorDoorIntro intro;
    MilitaryTabletLcdUi tablet;
    float fadeTimer;

    protected override string[] StepNames => Steps;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != BuildLevelScenes.Lvl2) return;
        if (FindAnyObjectByType<Lvl2BuildLevelStateMachine>() != null) return;
        var go = new GameObject("Lvl2 Build Level State Machine");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<Lvl2BuildLevelStateMachine>();
    }

    void Start()
    {
        intro = FindAnyObjectByType<ElevatorDoorIntro>();
        tablet = FindAnyObjectByType<MilitaryTabletLcdUi>();
        SetStep(0);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        SyncFromScene();
    }

    void SyncFromScene()
    {
        if (intro == null) intro = FindAnyObjectByType<ElevatorDoorIntro>();
        if (intro == null) return;

        if (StepIndex <= 0)
        {
            fadeTimer += Time.unscaledDeltaTime;
            if (fadeTimer > 4.5f && StepIndex < 1) SetStep(1);
        }

        if (intro.IsFloorPressed && StepIndex < 2) SetStep(2);
        if (intro.IsLoadingNextScene && StepIndex < 4) SetStep(4);
        else if (intro.AreDoorsOpenForPlayer && StepIndex < 3 && StepIndex >= 2) SetStep(3);
    }

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying) return;
        switch (index)
        {
            case 1:
                break;
            case 2:
                intro ??= FindAnyObjectByType<ElevatorDoorIntro>();
                intro?.EditorTestPressElevatorButton();
                break;
            case 3:
                tablet ??= FindAnyObjectByType<MilitaryTabletLcdUi>();
                tablet?.EditorTestActivateAllOptions();
                break;
            case 4:
                intro?.EditorTestLoadNextSceneNow();
                break;
        }
    }

    public void EditorTestPressElevator() => EditorTestJumpToStep(2);
    public void EditorTestCompleteTablet() => EditorTestJumpToStep(3);
}