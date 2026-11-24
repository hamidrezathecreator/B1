using UnityEngine;

/// <summary>Trigger relay on each option bar; forwards finger contacts to the tablet UI.</summary>
public sealed class TabletOptionTouch : MonoBehaviour
{
    [SerializeField] MilitaryTabletLcdUi owner;
    [SerializeField] int index;

    public void Bind(MilitaryTabletLcdUi ui, int optionIndex)
    {
        owner = ui;
        index = optionIndex;
    }

    void OnTriggerEnter(Collider other)
    {
        if (owner != null && other.GetComponent<TabletFingerTip>() != null)
            owner.FingerEnter(index);
    }

    void OnTriggerExit(Collider other)
    {
        if (owner != null && other.GetComponent<TabletFingerTip>() != null)
            owner.FingerExit(index);
    }
}

