using UnityEngine;

/// <summary>
/// Lvl2: fade → elevator floor button → ride/doors → military tablet 3 toggles → load Lvl 3.
/// </summary>
[AddComponentMenu("B1/Lvl2 Build Level State Machine")]
public sealed class Lvl2BuildLevelStateMachine : BuildLevelStateMachineBase
{
    static readonly string[] Steps =
    {
        "Fade In",
        "Await Elevator Floor Button",
        "Elevator Ride + Doors Open",
        "Walk To Table",
        "Tablet Toggle Option 1",
        "Tablet Toggle Option 2",
        "Tablet Toggle Option 3",
        "Transition To Lvl 3"
    };

    [Header("Scene references")]
    [SerializeField] ElevatorDoorIntro intro;
    [SerializeField] MilitaryTabletLcdUi tablet;

    protected override string[] StepNames => Steps;

    public int TabletCheckedCount => tablet != null ? tablet.CheckedCount : 0;

    void OnEnable()
    {
        if (tablet != null) tablet.OptionChanged += OnTabletOptionToggled;
    }

    void OnDisable()
    {
        if (tablet != null) tablet.OptionChanged -= OnTabletOptionToggled;
    }

    void Start()
    {
        if (!HasValidAssignments)
        {
            Debug.LogError("Lvl2: assign the elevator intro and tablet UI in the scene.", this);
            enabled = false;
            return;
        }
        SetStep(0);
    }

    public bool HasValidAssignments => intro != null && intro.HasValidAssignments && tablet != null;

    void Update()
    {
        if (!Application.isPlaying) return;
        SyncFromScene();
    }

    void SyncFromScene()
    {
        if (intro == null) return;

        if (intro.IsLoadingNextScene)
            TrackStep(7);
        else if (tablet != null && intro.AreDoorsOpenForPlayer)
            TrackTabletProgress();
        else if (intro.IsFloorPressed)
            TrackStep(2);
        else
            TrackStep(intro.IsFadeComplete ? 1 : 0);
    }

    void TrackTabletProgress()
    {
        var count = tablet.CheckedCount;
        var target = count switch
        {
            0 => 3,
            1 => 4,
            2 => 5,
            _ => 6
        };
        TrackStep(target);
    }

    void OnTabletOptionToggled(int index, bool on)
    {
        if (tablet == null || intro == null || !intro.AreDoorsOpenForPlayer || intro.IsLoadingNextScene) return;
        TrackTabletProgress();
    }

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying) return;
        if (index >= 3) intro?.EditorTestCompleteElevatorRide();
        switch (index)
        {
            case 1:
                intro?.EditorTestFinishFadeIn();
                break;
            case 2:
                intro?.EditorTestPressElevatorButton();
                break;
            case 4:
                tablet?.EditorTestSetOptionChecked(0, true);
                break;
            case 5:
                tablet?.EditorTestSetOptionChecked(0, true);
                tablet?.EditorTestSetOptionChecked(1, true);
                break;
            case 6:
                tablet?.EditorTestSetOptionChecked(0, true);
                tablet?.EditorTestSetOptionChecked(1, true);
                tablet?.EditorTestSetOptionChecked(2, true);
                break;
            case 7:
                if (tablet != null && !tablet.AllOptionsChecked)
                    tablet.EditorTestActivateAllOptions();
                intro?.EditorTestLoadNextSceneNow();
                break;
        }
    }

    public void EditorTestPressElevator() => EditorTestJumpToStep(2);
    public void EditorTestToggleOption1() => EditorTestJumpToStep(4);
    public void EditorTestToggleOption2() => EditorTestJumpToStep(5);
    public void EditorTestToggleOption3() => EditorTestJumpToStep(6);
    public void EditorTestCompleteTabletAndTransition() => EditorTestJumpToStep(7);
}
