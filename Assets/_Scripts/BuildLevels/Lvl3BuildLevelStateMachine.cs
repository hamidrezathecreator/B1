using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>LVL3_MissileLaunch tutorial phases with editor shortcuts.</summary>
[AddComponentMenu("B1/Lvl3 Build Level State Machine")]
public sealed class Lvl3BuildLevelStateMachine : BuildLevelStateMachineBase
{
    static readonly string[] Steps =
    {
        "Waiting To Begin",
        "Rotate Intro / Card",
        "Awaiting Rotate Press",
        "Rotating Launchers",
        "Fire Intro / Card",
        "Awaiting Fire Press",
        "Firing Missiles",
        "Final Logo / Menu"
    };

    MissileLauncherTutorialOrchestrator orchestrator;
    MissileLauncherButtons buttons;

    protected override string[] StepNames => Steps;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != BuildLevelScenes.Lvl3) return;
        if (FindAnyObjectByType<Lvl3BuildLevelStateMachine>() != null) return;
        var go = new GameObject("Lvl3 Build Level State Machine");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<Lvl3BuildLevelStateMachine>();
    }

    void Start()
    {
        orchestrator = FindAnyObjectByType<MissileLauncherTutorialOrchestrator>();
        buttons = FindAnyObjectByType<MissileLauncherButtons>();
        SetStep(0);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        orchestrator ??= FindAnyObjectByType<MissileLauncherTutorialOrchestrator>();
        if (orchestrator == null) return;

        var phase = orchestrator.Phase;
        var index = PhaseToStep(phase);
        if (index >= 0 && index != StepIndex) SetStep(index);
    }

    static int PhaseToStep(MissileLauncherTutorialOrchestrator.TutorialPhase phase)
    {
        return phase switch
        {
            MissileLauncherTutorialOrchestrator.TutorialPhase.Idle => 0,
            MissileLauncherTutorialOrchestrator.TutorialPhase.WaitingToBegin => 0,
            MissileLauncherTutorialOrchestrator.TutorialPhase.PlayingRotateAudio => 1,
            MissileLauncherTutorialOrchestrator.TutorialPhase.ShowingRotateCard => 1,
            MissileLauncherTutorialOrchestrator.TutorialPhase.AwaitingRotate => 2,
            MissileLauncherTutorialOrchestrator.TutorialPhase.Rotating => 3,
            MissileLauncherTutorialOrchestrator.TutorialPhase.PlayingFireAudio => 4,
            MissileLauncherTutorialOrchestrator.TutorialPhase.ShowingFireCard => 4,
            MissileLauncherTutorialOrchestrator.TutorialPhase.AwaitingFire => 5,
            MissileLauncherTutorialOrchestrator.TutorialPhase.Firing => 6,
            MissileLauncherTutorialOrchestrator.TutorialPhase.WaitingForFinalLogo => 7,
            MissileLauncherTutorialOrchestrator.TutorialPhase.PreparingFinalLogo => 7,
            MissileLauncherTutorialOrchestrator.TutorialPhase.PlayingFinalLogo => 7,
            MissileLauncherTutorialOrchestrator.TutorialPhase.ReturningToMenu => 7,
            MissileLauncherTutorialOrchestrator.TutorialPhase.Completed => 7,
            _ => -1
        };
    }

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying) return;
        orchestrator ??= FindAnyObjectByType<MissileLauncherTutorialOrchestrator>();
        buttons ??= FindAnyObjectByType<MissileLauncherButtons>();

        switch (index)
        {
            case 2:
                buttons?.EnableRotate();
                buttons?.PressRotate();
                break;
            case 5:
                buttons?.EnableFire();
                buttons?.PressFire();
                break;
        }
    }

    public void EditorTestBegin()
    {
        if (!Application.isPlaying) return;
        orchestrator ??= FindAnyObjectByType<MissileLauncherTutorialOrchestrator>();
        orchestrator?.Begin();
        SetStep(0);
    }
    public void EditorTestPressRotate() => EditorTestJumpToStep(2);
    public void EditorTestPressFire() => EditorTestJumpToStep(5);
}