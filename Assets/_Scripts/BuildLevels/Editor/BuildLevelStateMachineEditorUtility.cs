using UnityEditor;
using UnityEngine;

static class BuildLevelStateMachineEditorUtility
{
    public static void DrawTesterHeader(BuildLevelStateMachineBase machine)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Build level tester", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Play Mode only. Assign scene references on this component. Jump and advance controls are explicit testing shortcuts.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.LabelField("Current", $"{machine.StepIndex}: {machine.StepName}");
            if (GUILayout.Button(machine is Lvl1BuildLevelStateMachine
                    ? "Skip to next state (continues automatically)" : "Advance One Step"))
                machine.EditorTestAdvanceStep();
        }

        EditorGUILayout.Space(4);
        for (var i = 0; i < machine.StepCount; i++)
        {
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button($"Jump → {i}: {machine.StepNamesForEditor(i)}"))
                    machine.EditorTestJumpToStep(i);
            }
        }
    }
}

static class BuildLevelStateMachineEditorExtensions
{
    public static string StepNamesForEditor(this BuildLevelStateMachineBase machine, int index)
    {
        return index >= 0 && index < machine.StepCount ? GetName(machine, index) : "?";
    }

    static string GetName(BuildLevelStateMachineBase machine, int index)
    {
        if (machine is Lvl1BuildLevelStateMachine) return Lvl1Name(index);
        if (machine is Lvl2BuildLevelStateMachine) return Lvl2Name(index);
        if (machine is Lvl3BuildLevelStateMachine) return Lvl3Name(index);
        return $"Step {index}";
    }

    static string Lvl1Name(int i) => i switch
    {
        0 => "Await Start (tutorial)",
        1 => "Await Case Placement (tutorial)",
        2 => "Missile 1 + Explosion 1 + Bridge scatter",
        3 => "Missile 2 + Explosion 2",
        4 => "Fall + Fade Out",
        5 => "Load Lvl 2",
        _ => "?"
    };

    static string Lvl2Name(int i) => i switch
    {
        0 => "Fade In",
        1 => "Await Elevator Floor Button",
        2 => "Elevator Ride + Doors Open",
        3 => "Walk To Table",
        4 => "Tablet Toggle Option 1",
        5 => "Tablet Toggle Option 2",
        6 => "Tablet Toggle Option 3",
        7 => "Transition To Lvl 3",
        _ => "?"
    };

    static string Lvl3Name(int i) => i switch
    {
        0 => "Idle",
        1 => "Tutorial Launch Buttons",
        2 => "Auto Launcher Salvo",
        3 => "Drone Shoots 2 Units",
        4 => "Drone Orbits",
        5 => "Path To Tracker Device",
        6 => "Pick Up Tracker",
        7 => "Lock On Drone",
        8 => "Drone Destroyed",
        9 => "Complete",
        _ => "?"
    };
}

public abstract class BuildLevelStateMachineBaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var machine = (BuildLevelStateMachineBase)target;
        BuildLevelStateMachineEditorUtility.DrawTesterHeader(machine);
        DrawLevelSpecificButtons(machine);
    }

    protected virtual void DrawLevelSpecificButtons(BuildLevelStateMachineBase machine) { }
}

static class Lvl1StateMachinePlayModeMenus
{
    [MenuItem("B1/Lvl1/Play Explosion 1 + Condensation")]
    static void PlayExplosion1() => Run(sm => sm.EditorTestPlayExplosionOne());

    [MenuItem("B1/Lvl1/Play Explosion 2 + Condensation")]
    static void PlayExplosion2() => Run(sm => sm.EditorTestPlayExplosionTwo());

    static void Run(System.Action<Lvl1BuildLevelStateMachine> action)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Lvl 1", "Enter Play Mode in Lvl_1 first.", "OK");
            return;
        }

        var sm = Object.FindAnyObjectByType<Lvl1BuildLevelStateMachine>();
        if (sm == null)
        {
            EditorUtility.DisplayDialog("Lvl 1", "No Lvl1 Build Level State Machine in the loaded scene.", "OK");
            return;
        }

        action(sm);
        Selection.activeGameObject = sm.gameObject;
    }
}

[CustomEditor(typeof(Lvl1BuildLevelStateMachine))]
sealed class Lvl1BuildLevelStateMachineEditor : BuildLevelStateMachineBaseEditor
{
    protected override void DrawLevelSpecificButtons(BuildLevelStateMachineBase machine)
    {
        var lvl1 = (Lvl1BuildLevelStateMachine)machine;
        var so = new SerializedObject(lvl1);
        bool missingRefs = lvl1.Tutorial == null || !lvl1.Tutorial.HasValidAssignments
            || so.FindProperty("explosionOne").objectReferenceValue == null
            || so.FindProperty("missiles1").objectReferenceValue == null
            || so.FindProperty("scatterOne").objectReferenceValue == null;
        if (missingRefs)
            EditorGUILayout.HelpBox(
                "Assign the scene Tutorial, its pickup objects and placement zones, and the Explosion / Missiles / Scatter references.",
                MessageType.Warning);
        if (lvl1.Tutorial != null && GUILayout.Button("Select tutorial pickup and placement setup"))
            Selection.activeGameObject = lvl1.Tutorial.gameObject;
        EditorGUILayout.HelpBox("Gameplay waits for an assigned pickup to finish snapping into an assigned placement zone before launching missile 1. The shortcuts below explicitly bypass this wait for testing.", MessageType.Info);
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Lvl 1 shortcuts", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            var tutorial = lvl1.Tutorial;
            if (tutorial != null)
            {
                EditorGUILayout.LabelField("Tutorial", tutorial.EditorTutorialStepName);
                if (GUILayout.Button("Tutorial: press Start")) tutorial.EditorTestPressStart();
                if (GUILayout.Button("Tutorial: advance one tutorial step")) tutorial.EditorTestAdvanceTutorialStep();
            }

            if (GUILayout.Button("Play explosion 1 + scatter now"))
                lvl1.EditorTestPlayExplosionOne();
            if (GUILayout.Button("Play explosion 2 + condensation"))
                lvl1.EditorTestPlayExplosionTwo();
            if (GUILayout.Button("Run full B1_V2 parity sequence (no Director)"))
                lvl1.EditorTestRunFullPostPlacement();
            if (GUILayout.Button("Show glass shard overlay"))
                lvl1.EditorTestShowGlassShard();
        }
    }
}

[CustomEditor(typeof(Lvl2BuildLevelStateMachine))]
sealed class Lvl2BuildLevelStateMachineEditor : BuildLevelStateMachineBaseEditor
{
    protected override void DrawLevelSpecificButtons(BuildLevelStateMachineBase machine)
    {
        var lvl2 = (Lvl2BuildLevelStateMachine)machine;
        if (!lvl2.HasValidAssignments) EditorGUILayout.HelpBox("Assign the scene elevator intro and tablet UI.", MessageType.Warning);
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Lvl 2 shortcuts", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Tablet checked", $"{lvl2.TabletCheckedCount} / 3");
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Press elevator floor button")) lvl2.EditorTestPressElevator();
            if (GUILayout.Button("Toggle tablet option 1")) lvl2.EditorTestToggleOption1();
            if (GUILayout.Button("Toggle tablet option 2")) lvl2.EditorTestToggleOption2();
            if (GUILayout.Button("Toggle tablet option 3")) lvl2.EditorTestToggleOption3();
            if (GUILayout.Button("All toggles + load Lvl 3")) lvl2.EditorTestCompleteTabletAndTransition();
        }
    }
}

[CustomEditor(typeof(Lvl3BuildLevelStateMachine))]
sealed class Lvl3BuildLevelStateMachineEditor : BuildLevelStateMachineBaseEditor
{
    protected override void DrawLevelSpecificButtons(BuildLevelStateMachineBase machine)
    {
        var lvl3 = (Lvl3BuildLevelStateMachine)machine;
        if (!lvl3.HasValidAssignments) EditorGUILayout.HelpBox("Assign the tutorial orchestrator, automatic missile entries, drone, strikes, tracker and path target.", MessageType.Warning);
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Lvl 3 shortcuts", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Run full Lvl 3 sequence")) lvl3.EditorTestBeginSequence();
            if (GUILayout.Button("Tutorial only (step 1)")) lvl3.EditorTestTutorialOnly();
            if (GUILayout.Button("Auto launcher salvo")) lvl3.EditorTestAutoSalvoOnly();
            if (GUILayout.Button("Drone shoots 2 units")) lvl3.EditorTestDroneEngagement();
            if (GUILayout.Button("Show path to tracker")) lvl3.EditorTestShowTrackerPath();
            if (GUILayout.Button("Force drone kill (test)")) lvl3.EditorTestForceDroneKill();
        }
    }
}

[CustomEditor(typeof(XrShockFallSequence))]
sealed class XrShockFallSequenceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Test fall (Play Mode)"))
                ((XrShockFallSequence)target).BeginFall();
        }
    }
}
