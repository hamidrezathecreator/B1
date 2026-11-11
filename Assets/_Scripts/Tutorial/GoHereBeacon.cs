using UnityEngine;

/// <summary>
/// Drop on any object to show the blue "go here" beacon around it: a glowing ring on the floor,
/// a beam of light rising up to the object, and a bobbing diamond above. Disappears as soon as
/// one of the launch buttons under this object is pressed (or when <see cref="Hide"/> is called).
/// </summary>
[AddComponentMenu("B1/Go Here Beacon")]
public sealed class GoHereBeacon : MonoBehaviour
{
    [Tooltip("Ring on the floor under this object instead of at its bottom.")]
    [SerializeField] bool ringOnGround = true;
    [Tooltip("Child the beacon is centered on. Empty = the launch button under this object, else this object.")]
    [SerializeField] Transform centerOn;
    [Tooltip("Ring radius in meters. 0 = sized to the object's meshes.")]
    [Min(0f)] [SerializeField] float radius = 0.6f;
    [Tooltip("Bobbing diamond above the object.")]
    [SerializeField] bool showDiamond;
    [Tooltip("Center on this object's pivot, not its mesh bounds (cables or parts sticking out shift the bounds).")]
    [SerializeField] bool centerOnPivot = true;
    [Tooltip("Hide when any Button → Move Object A to B under this object is pressed.")]
    [SerializeField] bool hideWhenButtonPressed = true;

    GameObject marker;
    MissileLaunchButton[] buttons;

    System.Collections.IEnumerator Start()
    {
        // Launch buttons can be added by the level setup in its own Start; give it a frame,
        // so the beacon can center on the button.
        yield return null;
        Show();
        if (!hideWhenButtonPressed) yield break;
        buttons = GetComponentsInChildren<MissileLaunchButton>(true);
        foreach (var b in buttons)
            if (b != null) b.onPressed.AddListener(Hide);
    }

    /// <summary>Removes the beacon; also usable from a UnityEvent or Timeline signal.</summary>
    public void Hide()
    {
        if (marker != null) Destroy(marker);
        marker = null;
    }

    public void Show()
    {
        if (marker != null) return;
        var center = centerOn;
        if (center == null)
        {
            var button = GetComponentInChildren<MissileLaunchButton>(true);
            if (button != null) center = button.transform;
            else if (transform.childCount > 0) center = transform.GetChild(0);
            else center = transform;
        }
        marker = TutorialMarker.Create(center, ringOnGround, radius, centerOnPivot, showDiamond);
    }

    void OnValidate()
    {
        // Live preview of radius changes in Play mode.
        if (!Application.isPlaying || marker == null) return;
        Hide();
        Show();
    }

    void OnDestroy()
    {
        if (buttons != null)
            foreach (var b in buttons)
                if (b != null) b.onPressed.RemoveListener(Hide);
        Hide();
    }
}
