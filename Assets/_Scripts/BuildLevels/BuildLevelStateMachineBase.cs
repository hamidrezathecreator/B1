using System;
using UnityEngine;

/// <summary>Shared step tracking and editor test hooks for production build scenes.</summary>
public abstract class BuildLevelStateMachineBase : MonoBehaviour
{
    [SerializeField] bool logStepChanges = true;

    public int StepIndex { get; private set; } = -1;
    public string StepName => StepIndex >= 0 && StepIndex < StepCount ? StepNames[StepIndex] : "—";
    public int StepCount => StepNames.Length;
    public bool SequenceComplete { get; protected set; }

    public event Action<int, string> StepEntered;

    protected abstract string[] StepNames { get; }

    protected void SetStep(int index) => ApplyStep(index, invokeEnter: true);

    /// <summary>Updates displayed step from gameplay without firing test actions in <see cref="OnEnterStep"/>.</summary>
    protected void TrackStep(int index) => ApplyStep(index, invokeEnter: false);

    void ApplyStep(int index, bool invokeEnter)
    {
        if (index < 0 || index >= StepCount) return;
        if (index == StepIndex && !invokeEnter) return;
        StepIndex = index;
        var name = StepNames[index];
        if (logStepChanges) Debug.Log($"[{GetType().Name}] Step {index}: {name}", this);
        StepEntered?.Invoke(index, name);
        if (invokeEnter) OnEnterStep(index);
    }

    protected abstract void OnEnterStep(int index);

    /// <summary>Play Mode: run the next step's enter logic (or start at 0).</summary>
    public bool EditorTestAdvanceStep()
    {
        if (!Application.isPlaying) return false;
        var next = StepIndex + 1;
        if (next >= StepCount) return false;
        SetStep(next);
        return true;
    }

    public bool EditorTestJumpToStep(int index)
    {
        if (!Application.isPlaying) return false;
        if (index < 0 || index >= StepCount) return false;
        SetStep(index);
        return true;
    }

    public void EditorTestJumpToStepByName(string stepName)
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(stepName)) return;
        for (var i = 0; i < StepCount; i++)
        {
            if (string.Equals(StepNames[i], stepName, StringComparison.OrdinalIgnoreCase))
            {
                SetStep(i);
                return;
            }
        }
    }
}