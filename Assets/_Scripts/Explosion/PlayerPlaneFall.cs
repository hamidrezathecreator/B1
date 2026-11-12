using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Lvl 1: the player and the Plane under them drop together, started from a Timeline signal.
/// While this is in the scene, the Lvl 1 tutorial waits for it instead of its own timed fall.
/// </summary>
[AddComponentMenu("B1/Player And Plane Fall")]
public sealed class PlayerPlaneFall : MonoBehaviour
{
    [Tooltip("Falls together with the player. Empty = the object named below.")]
    [SerializeField] Transform plane;
    [SerializeField] string planeName = "Plane";
    [Min(0f)] [SerializeField] float gravity = 9.81f;
    [Min(0.1f)] [SerializeField] float maxSpeed = 30f;

    public bool HasFallen { get; private set; }

    /// <summary>Use this from the Timeline signal.</summary>
    public void Fall()
    {
        if (HasFallen) return;
        HasFallen = true;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        var origin = FindAnyObjectByType<XROrigin>();
        if (plane == null && !string.IsNullOrWhiteSpace(planeName))
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == planeName && t.gameObject.scene.IsValid()) { plane = t; break; }
        }
        if (plane != null)
        {
            plane.gameObject.SetActive(true);
            // It drops with the player; it must not hold anything up or catch the teleport ray.
            foreach (var col in plane.GetComponentsInChildren<Collider>()) col.enabled = false;
        }

        if (origin != null)
        {
            BridgeXrPlaySetup.SuspendDeckLock(origin);
            var body = origin.GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            foreach (var move in origin.GetComponentsInChildren<DynamicMoveProvider>(true)) move.enabled = false;
            foreach (var provider in origin.GetComponentsInChildren<TeleportationProvider>(true)) provider.enabled = false;
            foreach (var ray in origin.GetComponentsInChildren<XRRayInteractor>(true))
                if (ray.gameObject.name.Contains("Teleport")) ray.enabled = false;
            foreach (var g in origin.GetComponentsInChildren<GravityProvider>(true)) g.enabled = false;
        }

        // One under the other already moves with it.
        var moveOrigin = origin != null && (plane == null || !origin.transform.IsChildOf(plane));
        var movePlane = plane != null && (origin == null || !plane.IsChildOf(origin.transform));

        var speed = 0f;
        while ((moveOrigin && origin != null) || (movePlane && plane != null))
        {
            speed = Mathf.Min(speed + gravity * Time.deltaTime, maxSpeed);
            var step = Vector3.down * (speed * Time.deltaTime);
            if (moveOrigin && origin != null) origin.transform.position += step;
            if (movePlane && plane != null) plane.position += step;
            yield return null;
        }
    }
}
