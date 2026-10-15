using UnityEngine;
using Unity.XR.CoreUtils;

public class PlayerMove : MonoBehaviour
{
    [SerializeField] private XROrigin player;
    [SerializeField] private Transform dummySpot;

    // Call this from your event or Timeline Signal Receiver.
    public void MoveInstantly()
    {
        player.MatchOriginUpCameraForward(Vector3.up, dummySpot.forward);
        player.transform.position = dummySpot.position;

        // Account for the headset's offset within the XR Origin.
        Vector3 offset = dummySpot.position - player.Camera.transform.position;
        offset.y = 0f;
        player.transform.position += offset;
    }
}