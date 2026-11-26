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

    [Header("Falling sound")]
    [Tooltip("Empty uses a generated falling wind sound.")]
    [SerializeField] AudioClip fallingSound;
    [SerializeField, Range(0f, 1f)] float fallingSoundVolume = 0.65f;

    [Header("Exponential fog (blackout)")]
    [SerializeField] bool driveFog = true;
    [Tooltip("Fog density at the end of the ramp (Exponential mode).")]
    [Min(0f)] [SerializeField] float targetFogDensity = 0.18f;
    [Tooltip("Higher = fog stays low longer, then climbs quickly.")]
    [Min(0.1f)] [SerializeField] float fogCurveExponent = 2.8f;
    [Min(0.05f)] [SerializeField] float fogRampSeconds = 4f;
    [SerializeField] Color fogColor = Color.black;

    [Header("Shock rotation (degrees)")]
    [SerializeField] Vector2 pitchRange = new Vector2(60f, 85f);
    [SerializeField] Vector2 yawRange = new Vector2(-40f, 40f);
    [SerializeField] Vector2 rollRange = new Vector2(-35f, 35f);
    [Min(0.05f)] [SerializeField] float rotationBlendSeconds = 2.5f;
    [SerializeField, Min(1f)] float rotationDegreesPerSecond = 65f;
    [SerializeField, Min(0.1f)] float groundProbeDistance = 250f;
    [SerializeField] LayerMask groundLayers = ~0;

    [Header("Ground impact")]
    [SerializeField] Vector3 landingPositionShake = new Vector3(0.4f, 0.3f, 0.25f);
    [SerializeField] Vector3 landingRotationShake = new Vector3(18f, 12f, 22f);
    [SerializeField, Min(0.01f)] float landingShakeSeconds = 1.2f;
    [SerializeField, Min(1f)] float landingShakeFrequency = 28f;
    XRExplosionShake fallShake;
    ControllerFallHaptics fallHaptics;

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
        if (origin != null) PlayerImpactAudio.PlayFall(fallingSound, fallingSoundVolume);
        fallHaptics = origin != null ? ControllerFallHaptics.Begin(this) : null;
        ResolvePlane();
        if (plane != null)
        {
            plane.gameObject.SetActive(true);
            foreach (var col in plane.GetComponentsInChildren<Collider>()) col.enabled = false;
        }

        Quaternion startRot = origin != null ? Quaternion.Euler(0f, origin.transform.eulerAngles.y, 0f) : Quaternion.identity;
        var shockRot = startRot * Quaternion.Euler(
            Random.Range(pitchRange.x, pitchRange.y),
            Random.Range(yawRange.x, yawRange.y),
            Random.Range(rollRange.x, rollRange.y));

        if (origin != null) SuspendLocomotion(origin);
        if (origin != null)
        {
            fallShake = origin.GetComponent<XRExplosionShake>();
            if (fallShake != null) fallShake.BeginFallShake();
        }
        float groundHeight = float.NegativeInfinity;
        if (origin != null)
        {
            foreach (var hit in Physics.RaycastAll(origin.transform.position + Vector3.down * 0.5f,
                Vector3.down, groundProbeDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(origin.transform) || (plane != null && hit.transform.IsChildOf(plane))) continue;
                groundHeight = Mathf.Max(groundHeight, hit.point.y);
            }
        }

        if (driveFog) CaptureFog();

        var moveOrigin = origin != null && (plane == null || !origin.transform.IsChildOf(plane));
        var movePlane = plane != null && (origin == null || !plane.IsChildOf(origin.transform));

        var speed = 0f;
        var elapsed = 0f;
        var nextRotationTime = rotationBlendSeconds;
        bool landed = false;
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
                    if (elapsed >= nextRotationTime)
                    {
                        shockRot = startRot * Quaternion.Euler(Random.Range(pitchRange.x, pitchRange.y),
                            Random.Range(yawRange.x, yawRange.y), Random.Range(rollRange.x, rollRange.y));
                        nextRotationTime = elapsed + rotationBlendSeconds;
                    }
                    origin.transform.rotation = Quaternion.RotateTowards(origin.transform.rotation, shockRot,
                        rotationDegreesPerSecond * Time.deltaTime);
                }
            }

            if (movePlane && plane != null) plane.position += step;
            if (origin != null && origin.transform.position.y <= groundHeight)
            {
                var landingCorrection = Vector3.up * (groundHeight - origin.transform.position.y);
                origin.transform.position += landingCorrection;
                if (movePlane && plane != null) plane.position += landingCorrection;
                landed = true;
                break;
            }

            if (driveFog && fogRampSeconds > 0f)
            {
                var t = Mathf.Clamp01(elapsed / fogRampSeconds);
                var curved = Mathf.Pow(t, fogCurveExponent);
                RenderSettings.fogDensity = Mathf.Lerp(fogStartDensity, targetFogDensity, curved);
                RenderSettings.fogColor = Color.Lerp(fogStartColor, fogColor, curved);
            }

            yield return null;
        }

        if (fallHaptics != null) fallHaptics.StopVibration();
        if (fallShake != null) fallShake.StopImpactMotion();
        PlayerImpactAudio.StopFall();
        if (landed)
        {
            if (fallShake != null) fallShake.LandingImpact(landingPositionShake, landingRotationShake, landingShakeSeconds, landingShakeFrequency);
            ControllerHaptics.Pulse(1f, landingShakeSeconds);
            PlayerImpactAudio.PlayCloudHit(volume: 1f);
        }

        if (driveFog)
        {
            RenderSettings.fogDensity = targetFogDensity;
            RenderSettings.fogColor = fogColor;
        }

        running = false;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (fallHaptics != null) fallHaptics.StopVibration();
        if (fallShake != null) fallShake.StopImpactMotion();
        PlayerImpactAudio.StopFall();
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
