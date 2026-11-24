using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
[AddComponentMenu("XR/Controller Grab Pose")]
public sealed class ControllerGrabPose : XRBaseGrabTransformer
{
    [Tooltip("Optional hand rig template for the editor preview. Uses Resources/ControllerHandsRig when empty.")]
    [SerializeField] XrHandsRig handRigTemplate;
    [SerializeField] ControllerHandPose leftHand = new ControllerHandPose();
    [SerializeField] ControllerHandPose rightHand = new ControllerHandPose();

    XRGrabInteractable grab;
    ControllerHandRig posedHand;
    IXRSelectInteractor selectingInteractor;

    public XrHandsRig HandRigTemplate => handRigTemplate != null
        ? handRigTemplate : Resources.Load<XrHandsRig>("ControllerHandsRig");
    public ControllerHandPose GetPose(bool isLeft) => isLeft ? leftHand : rightHand;

    protected override RegistrationMode registrationMode => RegistrationMode.None;

    void OnEnable()
    {
        grab = GetComponent<XRGrabInteractable>();
        grab.selectExited.AddListener(OnReleased);
    }

    protected override void Start()
    {
        base.Start();
        if (grab.singleGrabTransformersCount == 0)
        {
            var general = GetComponent<XRGeneralGrabTransformer>();
            if (general == null) general = gameObject.AddComponent<XRGeneralGrabTransformer>();
            grab.AddSingleGrabTransformer(general);
        }
        grab.RemoveSingleGrabTransformer(this);
        grab.AddSingleGrabTransformer(this);
    }

    public override void Process(XRGrabInteractable grabInteractable,
        XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
    {
        if (grabInteractable.interactorsSelecting.Count != 1)
        {
            ReleaseHand();
            return;
        }

        var interactor = grabInteractable.interactorsSelecting[0];
        var hand = selectingInteractor == interactor && posedHand != null ? posedHand : FindHand(interactor);
        var pose = hand != null ? GetPose(hand.IsLeftHand) : null;
        if (pose == null || !pose.saved || hand.IsHandTracked)
        {
            ReleaseHand();
            return;
        }

        if (posedHand != hand)
        {
            ReleaseHand();
            posedHand = hand;
            selectingInteractor = interactor;
        }
        hand.SetGrabPose(this, pose);
        targetPose.rotation = hand.transform.rotation * Quaternion.Inverse(pose.rotation);
        targetPose.position = hand.transform.position -
            targetPose.rotation * Vector3.Scale(pose.position, grabInteractable.transform.lossyScale);
    }

    static ControllerHandRig FindHand(IXRSelectInteractor interactor)
    {
        for (var ancestor = interactor.transform; ancestor != null; ancestor = ancestor.parent)
        {
            var hands = ancestor.GetComponentsInChildren<ControllerHandRig>();
            if (hands.Length == 1) return hands[0];
            if (hands.Length > 1) return null;
        }
        return null;
    }

    void OnReleased(SelectExitEventArgs args) => ReleaseHand();

    public override void OnGrabCountChanged(XRGrabInteractable grabInteractable, Pose targetPose, Vector3 localScale)
    {
        if (grabInteractable.interactorsSelecting.Count != 1) ReleaseHand();
    }

    void ReleaseHand()
    {
        if (posedHand != null) posedHand.ReleaseGrabPose(this);
        posedHand = null;
        selectingInteractor = null;
    }

    void OnDisable()
    {
        if (grab != null) grab.selectExited.RemoveListener(OnReleased);
        ReleaseHand();
    }
}
