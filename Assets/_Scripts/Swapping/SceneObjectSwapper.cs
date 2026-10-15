using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Event-driven swapper for two scene object states. Use Swap and Restore from
/// Timeline Signal Receivers, Activation Tracks, or UnityEvents.
/// </summary>
public sealed class SceneObjectSwapper : MonoBehaviour
{
    [Header("Object States")]
    [SerializeField] private GameObject originalObject;
    [SerializeField] private GameObject replacementObject;
    [Tooltip("When enabled, the original state is applied on Awake.")]
    [SerializeField] private bool applyInitialStateOnAwake = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onSwapped = new UnityEvent();
    [SerializeField] private UnityEvent onRestored = new UnityEvent();

    private void Awake()
    {
        if (applyInitialStateOnAwake)
            Restore(false);
    }

    /// <summary>Activates the replacement state. Call from a Timeline activation event.</summary>
    public void Swap() => Swap(true);

    /// <summary>Restores the original state. Call from Timeline or a UnityEvent.</summary>
    public void Restore() => Restore(true);

    /// <summary>Sets the state directly for Timeline or another event source.</summary>
    public void SetSwapped(bool swapped)
    {
        if (swapped) Swap();
        else Restore();
    }

    /// <summary>Switches between the original and replacement states.</summary>
    public void Toggle()
    {
        if (replacementObject != null && replacementObject.activeSelf)
            Restore();
        else
            Swap();
    }

    [ContextMenu("Swap Objects")]
    private void TestSwap() => Swap();

    [ContextMenu("Restore Objects")]
    private void TestRestore() => Restore();

    private void Swap(bool invokeEvent)
    {
        if (originalObject == null || replacementObject == null)
        {
            Debug.LogWarning("SceneObjectSwapper needs both object references assigned.", this);
            return;
        }

        originalObject.SetActive(false);
        replacementObject.SetActive(true);
        if (invokeEvent) onSwapped?.Invoke();
    }

    private void Restore(bool invokeEvent)
    {
        if (originalObject == null || replacementObject == null)
        {
            Debug.LogWarning("SceneObjectSwapper needs both object references assigned.", this);
            return;
        }

        originalObject.SetActive(true);
        replacementObject.SetActive(false);
        if (invokeEvent) onRestored?.Invoke();
    }
}
