using UnityEngine;

/// <summary>Timeline/UnityEvent bridge for publishing one exact tutorial event ID.</summary>
public sealed class TutorialEventEmitter : MonoBehaviour
{
    [SerializeField] string exactEventId;

    /// <summary>Bind this no-argument method to a Timeline Signal Receiver.</summary>
    public void Raise() => TutorialEventBus.Publish(exactEventId);

    public void RaiseId(string eventId) => TutorialEventBus.Publish(eventId);
}
