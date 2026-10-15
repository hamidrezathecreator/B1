using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[System.Serializable]
public sealed class TutorialStringEvent : UnityEvent<string> { }

/// <summary>A press-and-latch physical VR button. Use with an XR Poke Interactor or other XR interactor.</summary>
[RequireComponent(typeof(Collider))]
public sealed class VRPressButton : XRBaseInteractable
{
    [Header("Button identity")]
    [SerializeField] string exactEventId;
    [SerializeField] bool interactableAtStart;
    [Header("Press motion")]
    [SerializeField] Transform pressTransform;
    [SerializeField] Vector3 localPressDirection = Vector3.down;
    [SerializeField, Min(0f)] float pressDistance = 0.02f;
    [Header("Events")]
    public UnityEvent pressed;
    public TutorialStringEvent pressedWithId;

    Vector3 startLocalPosition;
    bool hasBeenPressed;

    public string ExactEventId => exactEventId;
    public bool HasBeenPressed => hasBeenPressed;

    protected override void Awake()
    {
        base.Awake();
        if (pressTransform == null) pressTransform = transform;
        startLocalPosition = pressTransform.localPosition;
        enabled = interactableAtStart;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (pressTransform == null) pressTransform = transform;
        if (!hasBeenPressed) startLocalPosition = pressTransform.localPosition;
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        if (hasBeenPressed) return;
        hasBeenPressed = true;
        Vector3 direction = localPressDirection.sqrMagnitude > 0.0001f ? localPressDirection.normalized : Vector3.down;
        pressTransform.localPosition = startLocalPosition + direction * pressDistance;
        pressed?.Invoke();
        pressedWithId?.Invoke(exactEventId);
        TutorialEventBus.Publish(exactEventId);
    }

    /// <summary>Enables this button for a tutorial step.</summary>
    public void EnableButton() => enabled = true;

    /// <summary>Resets and disables the button for reuse.</summary>
    public void ResetButton()
    {
        hasBeenPressed = false;
        if (pressTransform != null) pressTransform.localPosition = startLocalPosition;
        enabled = false;
    }

    /// <summary>Publishes this button's exact ID without requiring an XR hand.</summary>
    public void PublishEvent() => TutorialEventBus.Publish(exactEventId);
}

