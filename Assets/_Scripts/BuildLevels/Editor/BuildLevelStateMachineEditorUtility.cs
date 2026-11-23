using UnityEditor;
using UnityEngine;

static class BuildLevelStateMachineEditorUtility
{
    public static void DrawTesterHeader(BuildLevelStateMachineBase machine)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Build level tester", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Enter Play Mode, then use the buttons below to jump steps or advance one step at a time.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.LabelField("Current", $"{machine.StepIndex}: {machine.StepName}");
            if (GUILayout.Button("Advance One Step")) machine.EditorTestAdvanceStep();
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
        2 => "B1_V2: Missile 1 + Swaps + Explosion 13",
        3 => "B1_V2: Bridge Collapse (scatter waves)",
        4 => "B1_V2: Half / Full Blink",
        5 => "B1_V2: Missile 2 + Explosion Two",
        6 => "Fall + Fog Shock",
        7 => "Close Eyes + Load Lvl 2",
        _ => "?"
    };

    static string Lvl2Name(int i) => i switch
    {
        0 => "Fade In",
        1 => "Await Elevator Floor Button",
        2 => "Elevator Ride + Doors",
        3 => "Await Tablet Checklist (3)",
        4 => "Load Lvl 3",
        _ => "?"
    };

    static string Lvl3Name(int i) => i switch
    {
        0 => "Waiting To Begin",
        1 => "Rotate Intro / Card",
        2 => "Awaiting Rotate Press",
        3 => "Rotating Launchers",
        4 => "Fire Intro / Card",
        5 => "Awaiting Fire Press",
        6 => "Firing Missiles",
        7 => "Final Logo / Menu",
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

[CustomEditor(typeof(Lvl1BuildLevelStateMachine))]
sealed class Lvl1BuildLevelStateMachineEditor : BuildLevelStateMachineBaseEditor
{
    protected override void DrawLevelSpecificButtons(BuildLevelStateMachineBase machine)
    {
        var lvl1 = (Lvl1BuildLevelStateMachine)machine;
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Lvl 1 shortcuts", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            Lvl1Tutorial tutorial = null;
            if (Application.isPlaying)
                tutorial = Object.FindAnyObjectByType<Lvl1Tutorial>();
            if (tutorial != null)
            {
                EditorGUILayout.LabelField("Tutorial", tutorial.EditorTutorialStepName);
                if (GUILayout.Button("Tutorial: press Start")) tutorial.EditorTestPressStart();
                if (GUILayout.Button("Tutorial: advance one tutorial step")) tutorial.EditorTestAdvanceTutorialStep();
            }

            if (GUILayout.Button("Run full B1_V2 parity sequence (no Director)"))
                lvl1.EditorTestRunFullPostPlacement();
        }
    }
}

[CustomEditor(typeof(Lvl2BuildLevelStateMachine))]
sealed class Lvl2BuildLevelStateMachineEditor : BuildLevelStateMachineBaseEditor
{
    protected override void DrawLevelSpecificButtons(BuildLevelStateMachineBase machine)
    {
        var lvl2 = (Lvl2BuildLevelStateMachine)machine;
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Press elevator floor button")) lvl2.EditorTestPressElevator();
            if (GUILayout.Button("Complete tablet checklist")) lvl2.EditorTestCompleteTablet();
        }
    }
}

[CustomEditor(typeof(Lvl3BuildLevelStateMachine))]
sealed class Lvl3BuildLevelStateMachineEditor : BuildLevelStateMachineBaseEditor
{
    protected override void DrawLevelSpecificButtons(BuildLevelStateMachineBase machine)
    {
        var lvl3 = (Lvl3BuildLevelStateMachine)machine;
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Begin tutorial")) lvl3.EditorTestBegin();
            if (GUILayout.Button("Simulate rotate press")) lvl3.EditorTestPressRotate();
            if (GUILayout.Button("Simulate fire press")) lvl3.EditorTestPressFire();
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