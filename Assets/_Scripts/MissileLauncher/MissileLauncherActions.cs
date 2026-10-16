using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Commands a group of launchers and reports when every assigned launcher finishes a phase.</summary>
public sealed class MissileLauncherActions : MonoBehaviour
{
    [SerializeField] MissileLauncherController[] launchers = new MissileLauncherController[0];
    public UnityEvent allRotationsCompleted = new UnityEvent();
    [Tooltip("All launch sequences dispatched their missiles; this does not wait for flights to end.")]
    public UnityEvent allLaunchSequencesCompleted = new UnityEvent();
    public UnityEvent actionFailed = new UnityEvent();

    public bool IsBusy => handlers.Count > 0;
    readonly Dictionary<MissileLauncherController, UnityAction> handlers = new Dictionary<MissileLauncherController, UnityAction>();
    readonly HashSet<MissileLauncherController> pending = new HashSet<MissileLauncherController>();
    readonly List<MissileLauncherController> participants = new List<MissileLauncherController>();
    bool rotating;
    bool dispatching;

    [ContextMenu("Test/Rotate All (Play Mode)")]
    public void RotateAll() => StartPhase(true);
    [ContextMenu("Test/Fire All (Play Mode)")]
    public void FireAll() => StartPhase(false);

    void StartPhase(bool rotation)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsBusy) return;
        participants.Clear();
        var unique = new HashSet<MissileLauncherController>();
        if (launchers != null)
            foreach (var launcher in launchers)
            {
                if (launcher == null || !launcher.isActiveAndEnabled || launcher.IsRotating || launcher.IsLaunching ||
                    (!rotation && !launcher.RotationCompleted))
                {
                    Fail("Every assigned launcher must be active and idle; firing also requires every rotation to be complete.");
                    return;
                }
                if (unique.Add(launcher)) participants.Add(launcher);
            }
        if (participants.Count == 0) { Fail("Assign at least one launcher."); return; }

        rotating = rotation;
        // Subscribe and populate the entire barrier before commands: a zero-duration phase can finish synchronously.
        foreach (var launcher in participants)
        {
            if (rotation && launcher.RotationCompleted) continue;
            var participant = launcher;
            UnityAction callback = () => OnCompleted(participant);
            handlers.Add(participant, callback);
            pending.Add(participant);
            PhaseEvent(participant).AddListener(callback);
        }
        dispatching = true;
        foreach (var launcher in participants)
        {
            if (!pending.Contains(launcher)) continue;
            if (rotation) launcher.RotateContainer();
            else launcher.LaunchMissiles();
        }
        dispatching = false;
        TryComplete();
    }

    UnityEvent PhaseEvent(MissileLauncherController launcher) => rotating ? launcher.rotationCompleted : launcher.missileSequenceCompleted;

    void OnCompleted(MissileLauncherController launcher)
    {
        if (!pending.Remove(launcher)) return;
        TryComplete();
    }

    void TryComplete()
    {
        if (dispatching || pending.Count > 0) return;
        var completed = rotating ? allRotationsCompleted : allLaunchSequencesCompleted;
        ClearListeners();
        completed.Invoke();
    }

    void Update()
    {
        foreach (var launcher in pending)
            if (launcher == null || !launcher.isActiveAndEnabled || (rotating ? !launcher.IsRotating : !launcher.IsLaunching))
            {
                Fail("A launcher became unavailable or its phase was interrupted. Reset the tutorial to retry.");
                return;
            }
    }

    void Fail(string message)
    {
        ClearListeners();
        Debug.LogError(message, this);
        actionFailed.Invoke();
    }

    [ContextMenu("Test/Reset All (Play Mode)")]
    public void ResetAll()
    {
        if (!Application.isPlaying) return;
        ClearListeners();
        if (launchers != null)
            foreach (var launcher in launchers)
                if (launcher != null && launcher.gameObject.activeInHierarchy) launcher.ResetLauncher();
    }

    void ClearListeners()
    {
        foreach (var pair in handlers)
            if (pair.Key != null) PhaseEvent(pair.Key).RemoveListener(pair.Value);
        handlers.Clear();
        pending.Clear();
        dispatching = false;
    }

    void OnDisable()
    {
        var interrupted = IsBusy;
        ClearListeners();
        if (interrupted) actionFailed.Invoke();
    }
}
