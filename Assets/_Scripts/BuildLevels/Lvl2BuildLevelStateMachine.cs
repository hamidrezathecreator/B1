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

    [Header("Fade tracking")]
    [Tooltip("Unscaled seconds before step 1 is shown. Match ElevatorDoorIntro black hold + fade.")]
    [SerializeField] float fadeCompleteSeconds = 4.5f;

    float fadeTimer;

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

    public bool HasValidAssignments => intro != null && tablet != null;

    void Update()
    {
        if (!Application.isPlaying) return;
        SyncFromScene();
    }

    void SyncFromScene()
    {
        if (intro == null) return;

        if (StepIndex <= 0)
        {
            fadeTimer += Time.unscaledDeltaTime;
            if (fadeTimer >= fadeCompleteSeconds && StepIndex < 1)
                TrackStep(1);
        }

        if (!intro.IsFloorPressed)
        {
            if (StepIndex > 1 && StepIndex < 4) return;
        }
        else if (StepIndex < 2)
            TrackStep(2);

        if (intro.AreDoorsOpenForPlayer && StepIndex < 3)
            TrackStep(3);

        if (tablet != null && intro.AreDoorsOpenForPlayer)
            TrackTabletProgress();

        if (intro.IsLoadingNextScene && StepIndex < 7)
            TrackStep(7);
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
        if (StepIndex < target)
            TrackStep(target);
    }

    void OnTabletOptionToggled(int index, bool on)
    {
        if (!on || tablet == null || intro == null || !intro.AreDoorsOpenForPlayer) return;
        TrackTabletProgress();
    }

    protected override void OnEnterStep(int index)
    {
        if (!Application.isPlaying) return;
        switch (index)
        {
            case 2:
                intro?.EditorTestPressElevatorButton();
                break;
            case 4:
                tablet?.EditorTestSetOptionChecked(0, true);
                break;
            case 5:
                tablet?.EditorTestSetOptionChecked(1, true);
                break;
            case 6:
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
