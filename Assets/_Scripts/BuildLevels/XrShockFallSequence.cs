using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Scripted XR drop (not physics) with exponential fog darkening and a random orientation shock on the rig.
/// </summary>
[AddComponentMenu("B1/XR Shock Fall Sequence")]
public sealed class XrShockFallSequence : MonoBehaviour
{
    [SerializeField] Transform plane;
    [SerializeField] string planeName = "Plane";
    [Min(0f)] [SerializeField] float gravity = 19.62f;
    [Min(0.1f)] [SerializeField] float maxSpeed = 60f;

    [Header("Exponential fog (blackout)")]
    [SerializeField] bool driveFog = true;
    [Tooltip("Fog density at the end of the ramp (Exponential mode).")]
    [Min(0f)] [SerializeField] float targetFogDensity = 0.18f;
    [Tooltip("Higher = fog stays low longer, then climbs quickly.")]
    [Min(0.1f)] [SerializeField] float fogCurveExponent = 2.8f;
    [Min(0.05f)] [SerializeField] float fogRampSeconds = 4f;
    [SerializeField] Color fogColor = Color.black;

    [Header("Shock rotation (degrees)")]
    [SerializeField] Vector2 pitchRange = new Vector2(-25f, 25f);
    [SerializeField] Vector2 yawRange = new Vector2(-40f, 40f);
    [SerializeField] Vector2 rollRange = new Vector2(-35f, 35f);
    [Min(0.05f)] [SerializeField] float rotationBlendSeconds = 2.5f;

    bool running;
    float fogStartDensity;
    bool fogWasEnabled;
    FogMode fogStartMode;
    Color fogStartColor;

    public bool IsRunning => running;

    public void BeginFall()
    {
        if (running) return;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        running = true;
        var origin = FindAnyObjectByType<XROrigin>();
        ResolvePlane();
        if (plane != null)
        {
            plane.gameObject.SetActive(true);
            foreach (var col in plane.GetComponentsInChildren<Collider>()) col.enabled = false;
        }

        Quaternion startRot = origin != null ? origin.transform.rotation : Quaternion.identity;
        var shockRot = startRot * Quaternion.Euler(
            Random.Range(pitchRange.x, pitchRange.y),
            Random.Range(yawRange.x, yawRange.y),
            Random.Range(rollRange.x, rollRange.y));

        if (origin != null) SuspendLocomotion(origin);

        if (driveFog) CaptureFog();

        var moveOrigin = origin != null && (plane == null || !origin.transform.IsChildOf(plane));
        var movePlane = plane != null && (origin == null || !plane.IsChildOf(origin.transform));

        var speed = 0f;
        var elapsed = 0f;
        while ((moveOrigin && origin != null) || (movePlane && plane != null))
        {
            elapsed += Time.deltaTime;
            speed = Mathf.Min(speed + gravity * Time.deltaTime, maxSpeed);
            var step = Vector3.down * (speed * Time.deltaTime);
            if (moveOrigin && origin != null)
            {
                origin.transform.position += step;
                if (rotationBlendSeconds > 0f)
                {
                    var rt = Mathf.Clamp01(elapsed / rotationBlendSeconds);
                    origin.transform.rotation = Quaternion.Slerp(startRot, shockRot, rt);
                }
            }

            if (movePlane && plane != null) plane.position += step;

            if (driveFog && fogRampSeconds > 0f)
            {
                var t = Mathf.Clamp01(elapsed / fogRampSeconds);
                var curved = Mathf.Pow(t, fogCurveExponent);
                RenderSettings.fogDensity = Mathf.Lerp(fogStartDensity, targetFogDensity, curved);
                RenderSettings.fogColor = Color.Lerp(fogStartColor, fogColor, curved);
            }

            yield return null;
        }

        if (driveFog)
        {
            RenderSettings.fogDensity = targetFogDensity;
            RenderSettings.fogColor = fogColor;
        }

        running = false;
    }

    void ResolvePlane()
    {
        if (plane != null) return;
        if (string.IsNullOrWhiteSpace(planeName)) return;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == planeName && t.gameObject.scene.IsValid()) { plane = t; break; }
    }

    void CaptureFog()
    {
        fogWasEnabled = RenderSettings.fog;
        fogStartMode = RenderSettings.fogMode;
        fogStartDensity = RenderSettings.fogDensity;
        fogStartColor = RenderSettings.fogColor;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = fogStartDensity;
        RenderSettings.fogColor = fogStartColor;
    }

    public void RestoreFog()
    {
        if (!driveFog) return;
        RenderSettings.fog = fogWasEnabled;
        RenderSettings.fogMode = fogStartMode;
        RenderSettings.fogDensity = fogStartDensity;
        RenderSettings.fogColor = fogStartColor;
    }

    static void SuspendLocomotion(XROrigin origin)
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
}