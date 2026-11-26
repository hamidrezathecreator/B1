using PrimeTween;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
[AddComponentMenu("B1/Helicopter Hover")]
public sealed class HelicopterHover : MonoBehaviour
{
    public enum HoverPreset { Calm, Normal, Rough, Custom }

    [Header("Orbit")]
    [SerializeField] Transform hoverCenter;
    [Tooltip("Use the authored horizontal distance from the center when starting.")]
    [SerializeField] bool useStartingRadius = true;
    [SerializeField, Min(0.1f)] float radius = 125f;
    [Tooltip("World metres above Hover Center. The center can be moved during flight.")]
    [SerializeField] float heightAboveCenter = 71.78836f;
    [SerializeField, Min(1f)] float orbitSeconds = 100f;
    [SerializeField] bool clockwise = true;
    [Tooltip("-90 points the helicopter's right side toward the center; 90 points its left side inward.")]
    [SerializeField] float sidewaysYaw = -90f;
    [SerializeField] bool playOnEnable = true;
    [SerializeField] bool useUnscaledTime;

    [Header("Motion preset")]
    [Tooltip("Changing this applies the preset once. Individual settings remain editable afterwards.")]
    [SerializeField] HoverPreset preset = HoverPreset.Normal;
    [SerializeField, HideInInspector] HoverPreset appliedPreset = HoverPreset.Normal;

    [Header("Normal vibration")]
    [SerializeField] bool shakeEnabled = true;
    [SerializeField] Vector3 shakePosition = new Vector3(0.012f, 0.018f, 0.012f);
    [SerializeField] Vector3 shakeRotation = new Vector3(0.18f, 0.12f, 0.25f);
    [SerializeField, Min(0.01f)] float shakeFrequency = 12f;

    [Header("Air turbulence")]
    [SerializeField] bool turbulenceEnabled = true;
    [SerializeField] Vector3 turbulencePosition = new Vector3(0.1f, 0.16f, 0.08f);
    [SerializeField] Vector3 turbulenceRotation = new Vector3(0.8f, 0.4f, 1.2f);
    [SerializeField, Min(0.01f)] float turbulenceFrequency = 0.65f;
    [SerializeField] Vector2 gustIntervalSeconds = new Vector2(5f, 10f);
    [SerializeField, Min(0.1f)] float gustDuration = 2.5f;
    [SerializeField, Range(0f, 1f)] float turbulenceIntensity = 0.5f;

    [Header("Level start passenger (optional)")]
    [SerializeField] XROrigin origin;
    [Tooltip("The XRParent already placed inside the helicopter. Move this anchor to adjust the seat.")]
    [SerializeField] Transform xrParent;
    [SerializeField, Range(0.1f, 1f)] float xrHeightMultiplier = 0.5f;
    [Tooltip("Mission Start returns the player here at normal height before the bridge tutorial begins.")]
    [SerializeField] Transform missionStartSpawn;
    [SerializeField] Transform missionStartParent;

    [Header("Cabin audio")]
    [Tooltip("Assign the Helicopter SFX source. Set its Clip, Volume and Pitch on that AudioSource.")]
    [SerializeField] AudioSource helicopterSfx;
    [Tooltip("Assign the Helicopter Music source. Set its Clip and Volume independently of the engine sound.")]
    [SerializeField] AudioSource helicopterMusic;

    Tween motion;
    float angle;
    float activeRadius;
    float lastTime;
    float nextGustTime;
    float gustStart = float.NegativeInfinity;
    float noiseSeed;
    Vector3 gustDirection;
    Vector3 cleanPosition;
    Quaternion cleanRotation;
    LocomotionProvider[] providers;
    bool[] providerStates;
    CharacterController body;
    bool bodyWasEnabled;
    bool riding;
    bool rideCompleted;
    XrOriginPlaySettings playSettings;
    float originalCameraHeight;
    float originalBodyHeight;
    Vector3 originalBodyCenter;
    bool movementLocked;
    bool preserveMovementLockOnDisable;
    Vector3 lockedLocalPosition;
    Quaternion lockedLocalRotation;
    float sfxVolume;
    float musicVolume;
    Transform roomScaleCompensation;
    Vector3 lockedEyePosition;
    bool roomScaleLocked;

    public bool IsRiding => riding;
    public bool IsHovering => motion.isAlive;
    public Camera PassengerCamera => origin != null ? origin.Camera : null;

    public static bool IsMovementLocked(XROrigin passenger) => passenger != null &&
        (IsPassenger(passenger) || passenger.GetComponent<XrOriginPlaySettings>()?.IsLocomotionLocked == true);

    public static bool IsPassenger(XROrigin passenger)
    {
        if (passenger == null) return false;
        var hover = passenger.GetComponentInParent<HelicopterHover>();
        return hover != null && hover.riding && hover.origin == passenger;
    }

    void OnEnable()
    {
        if (playOnEnable) StartHover();
    }

    public void StartHover()
    {
        if (!Application.isPlaying || motion.isAlive) return;
        if (hoverCenter == null || hoverCenter == transform || hoverCenter.IsChildOf(transform))
        {
            Debug.LogError("Helicopter Hover: assign a center outside the helicopter hierarchy.", this);
            return;
        }
        var offset = transform.position - hoverCenter.position;
        activeRadius = useStartingRadius ? new Vector2(offset.x, offset.z).magnitude : radius;
        activeRadius = Mathf.Max(0.1f, activeRadius);
        angle = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;
        lastTime = 0f;
        noiseSeed = Random.Range(0f, 1000f);
        gustStart = float.NegativeInfinity;
        ScheduleGust(0f);
        ApplyMotion(0f);
        BeginRide();
        motion = Tween.Custom(this, 0f, 1f,
            new TweenSettings(1f, Ease.Linear, cycles: -1, cycleMode: CycleMode.Incremental,
                useUnscaledTime: useUnscaledTime),
            (hover, elapsed) => hover.ApplyMotion(elapsed));
    }

    public void StopHover()
    {
        if (!motion.isAlive) return;
        motion.Stop();
        transform.SetPositionAndRotation(cleanPosition, cleanRotation);
    }

    void ApplyMotion(float elapsed)
    {
        if (hoverCenter == null)
        {
            StopHover();
            return;
        }
        angle = Mathf.Repeat(angle + (clockwise ? -1f : 1f) * 360f *
            (elapsed - lastTime) / Mathf.Max(1f, orbitSeconds), 360f);
        lastTime = elapsed;
        float radians = angle * Mathf.Deg2Rad;
        var radial = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
        cleanPosition = hoverCenter.position + radial * activeRadius + Vector3.up * heightAboveCenter;
        cleanRotation = Quaternion.LookRotation(-radial, Vector3.up) * Quaternion.Euler(0f, sidewaysYaw, 0f);

        var positionOffset = Vector3.zero;
        var rotationOffset = Vector3.zero;
        if (shakeEnabled)
        {
            positionOffset += Vector3.Scale(Noise(elapsed * shakeFrequency, 0f), shakePosition);
            rotationOffset += Vector3.Scale(Noise(elapsed * shakeFrequency, 17f), shakeRotation);
        }
        if (turbulenceEnabled)
        {
            if (elapsed >= nextGustTime) TriggerTurbulence();
            float progress = (elapsed - gustStart) / Mathf.Max(0.1f, gustDuration);
            float envelope = progress >= 0f && progress <= 1f ? Mathf.Sin(progress * Mathf.PI) : 0f;
            var wind = Noise(elapsed * turbulenceFrequency, 37f) * 0.2f + gustDirection * envelope;
            positionOffset += Vector3.Scale(wind, turbulencePosition) * turbulenceIntensity;
            rotationOffset += Vector3.Scale(wind, turbulenceRotation) * turbulenceIntensity;
        }
        transform.SetPositionAndRotation(cleanPosition + cleanRotation * positionOffset,
            cleanRotation * Quaternion.Euler(rotationOffset));
    }

    Vector3 Noise(float time, float channel)
    {
        return new Vector3(
            Mathf.PerlinNoise(noiseSeed + channel, time) * 2f - 1f,
            Mathf.PerlinNoise(noiseSeed + channel + 5f, time) * 2f - 1f,
            Mathf.PerlinNoise(noiseSeed + channel + 11f, time) * 2f - 1f);
    }

    void ScheduleGust(float elapsed)
    {
        float minimum = Mathf.Max(0.1f, gustIntervalSeconds.x);
        float maximum = Mathf.Max(minimum, gustIntervalSeconds.y);
        nextGustTime = elapsed + Mathf.Max(0.1f, gustDuration) + Random.Range(minimum, maximum);
    }

    [ContextMenu("Trigger Turbulence (Play Mode)")]
    public void TriggerTurbulence()
    {
        if (!Application.isPlaying) return;
        gustStart = lastTime;
        gustDirection = Random.onUnitSphere;
        ScheduleGust(lastTime);
    }

    void BeginRide()
    {
        if (riding || rideCompleted || origin == null || xrParent == null) return;
        if (!xrParent.IsChildOf(transform) || missionStartSpawn == null || missionStartSpawn.IsChildOf(transform))
        {
            Debug.LogError("Helicopter Hover: assign XRParent inside the cabin and Mission Start Spawn outside it.", this);
            return;
        }
        riding = true;
        movementLocked = true;
        body = origin.GetComponent<CharacterController>();
        bodyWasEnabled = body != null && body.enabled;
        originalBodyHeight = body != null ? body.height : 0f;
        originalBodyCenter = body != null ? body.center : Vector3.zero;
        if (body != null) body.enabled = false;
        BridgeXrPlaySetup.SuspendDeckLock(origin);
        origin.transform.SetParent(xrParent, false);
        origin.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        playSettings = origin.GetComponent<XrOriginPlaySettings>();
        if (playSettings != null) playSettings.SetLocomotionLocked(true);
        originalCameraHeight = origin.CameraYOffset;
        if (playSettings != null) playSettings.SetHeightMultiplier(xrHeightMultiplier);
        else origin.CameraYOffset = originalCameraHeight * xrHeightMultiplier;
        providers = origin.GetComponentsInChildren<LocomotionProvider>(true);
        providerStates = new bool[providers.Length];
        for (int index = 0; index < providers.Length; index++)
        {
            providerStates[index] = providers[index].enabled;
            providers[index].enabled = false;
        }
        lockedLocalPosition = origin.transform.localPosition;
        lockedLocalRotation = origin.transform.localRotation;
        BeginRoomScaleLock();
        sfxVolume = helicopterSfx != null ? helicopterSfx.volume : 0f;
        musicVolume = helicopterMusic != null ? helicopterMusic.volume : 0f;
        PlayCabinSource(helicopterSfx);
        PlayCabinSource(helicopterMusic);
    }

    static void PlayCabinSource(AudioSource source)
    {
        if (source == null || source.clip == null) return;
        source.loop = true;
        source.Play();
    }

    public void SetRideAudioLevel(float level)
    {
        if (helicopterSfx != null) helicopterSfx.volume = sfxVolume * Mathf.Clamp01(level);
        if (helicopterMusic != null) helicopterMusic.volume = musicVolume * Mathf.Clamp01(level);
    }

    void StopCabinAudio()
    {
        if (helicopterSfx != null) helicopterSfx.Stop();
        if (helicopterMusic != null) helicopterMusic.Stop();
        SetRideAudioLevel(1f);
    }

    void LateUpdate() => MaintainPassengerLock();

    public void MaintainPassengerLock()
    {
        if (!movementLocked || origin == null) return;
        if (body != null) body.enabled = false;
        foreach (var provider in providers)
            if (provider != null) provider.enabled = false;
        origin.transform.SetLocalPositionAndRotation(lockedLocalPosition, lockedLocalRotation);
        CompensateRoomScalePosition();
    }

    void BeginRoomScaleLock()
    {
        if (origin.Camera == null || origin.CameraFloorOffsetObject == null) return;
        var trackingSpace = origin.CameraFloorOffsetObject.transform;
        if (trackingSpace == origin.transform || !origin.Camera.transform.IsChildOf(trackingSpace)) return;
        if (roomScaleCompensation == null)
        {
            roomScaleCompensation = new GameObject("Helicopter Room Scale Compensation").transform;
            roomScaleCompensation.SetParent(trackingSpace.parent, false);
            trackingSpace.SetParent(roomScaleCompensation, false);
        }
        lockedEyePosition = roomScaleCompensation.parent.InverseTransformPoint(
            origin.transform.TransformPoint(Vector3.up * origin.CameraYOffset));
        roomScaleLocked = true;
        Application.onBeforeRender -= CompensateRoomScalePosition;
        Application.onBeforeRender += CompensateRoomScalePosition;
        CompensateRoomScalePosition();
    }

    [BeforeRenderOrder(10000)]
    void CompensateRoomScalePosition()
    {
        if (!roomScaleLocked || roomScaleCompensation == null || origin == null || origin.Camera == null) return;
        var eyePosition = roomScaleCompensation.parent.InverseTransformPoint(origin.Camera.transform.position);
        roomScaleCompensation.localPosition += lockedEyePosition - eyePosition;
    }

    void EndRoomScaleLock()
    {
        roomScaleLocked = false;
        Application.onBeforeRender -= CompensateRoomScalePosition;
        if (roomScaleCompensation != null) roomScaleCompensation.localPosition = Vector3.zero;
    }

    public void EndRide(bool keepMovementLocked = false)
    {
        if (!riding || origin == null) return;
        riding = false;
        rideCompleted = true;
        EndRoomScaleLock();
        if (body != null) body.enabled = false;
        origin.transform.SetParent(missionStartParent, true);
        if (missionStartSpawn != null)
        {
            origin.transform.SetPositionAndRotation(missionStartSpawn.position, missionStartSpawn.rotation);
            if (origin.Camera != null)
            {
                origin.MatchOriginUpCameraForward(Vector3.up, missionStartSpawn.forward);
                var cameraOffset = missionStartSpawn.position - origin.Camera.transform.position;
                cameraOffset.y = 0f;
                origin.transform.position += cameraOffset;
            }
        }
        if (playSettings != null) playSettings.SetHeightMultiplier(1f);
        else origin.CameraYOffset = originalCameraHeight;
        if (body != null)
        {
            body.height = originalBodyHeight;
            body.center = originalBodyCenter;
        }
        if (origin.gameObject.scene.name == BuildLevelScenes.Lvl1)
            BridgeXrPlaySetup.EnsurePlaySpace(origin);
        lockedLocalPosition = origin.transform.localPosition;
        lockedLocalRotation = origin.transform.localRotation;
        StopCabinAudio();
        if (keepMovementLocked) LateUpdate();
        else ReleaseMovementLock();
    }

    public void ReleaseMovementLock()
    {
        if (!movementLocked) return;
        movementLocked = false;
        preserveMovementLockOnDisable = false;
        if (playSettings != null) playSettings.SetLocomotionLocked(false);
        if (body != null) body.enabled = bodyWasEnabled;
        for (int index = 0; index < providers.Length; index++)
            if (providers[index] != null) providers[index].enabled = providerStates[index];
        if (origin != null && !riding && origin.gameObject.scene.name == BuildLevelScenes.Lvl1)
            BridgeXrPlaySetup.EnsurePlaySpace(origin);
    }

    public void DisableAfterTransfer()
    {
        if (riding) EndRide(keepMovementLocked: true);
        preserveMovementLockOnDisable = movementLocked;
        gameObject.SetActive(false);
    }

    void OnDisable()
    {
        StopHover();
        EndRoomScaleLock();
        if (preserveMovementLockOnDisable) return;
        EndRide();
        ReleaseMovementLock();
    }

    void OnValidate()
    {
        if (preset != appliedPreset) ApplyPreset(preset);
    }

    public void ApplyPreset(HoverPreset selection)
    {
        preset = appliedPreset = selection;
        if (selection == HoverPreset.Custom) return;
        float amount = selection == HoverPreset.Calm ? 0.3f : selection == HoverPreset.Rough ? 2f : 1f;
        shakeEnabled = turbulenceEnabled = true;
        shakePosition = new Vector3(0.012f, 0.018f, 0.012f) * amount;
        shakeRotation = new Vector3(0.18f, 0.12f, 0.25f) * amount;
        shakeFrequency = selection == HoverPreset.Rough ? 16f : 12f;
        turbulencePosition = new Vector3(0.1f, 0.16f, 0.08f) * amount;
        turbulenceRotation = new Vector3(0.8f, 0.4f, 1.2f) * amount;
        turbulenceFrequency = selection == HoverPreset.Rough ? 1f : 0.65f;
        gustIntervalSeconds = selection == HoverPreset.Rough ? new Vector2(3f, 6f) : new Vector2(5f, 10f);
        gustDuration = selection == HoverPreset.Rough ? 3f : 2.5f;
        turbulenceIntensity = 0.5f;
    }

    [ContextMenu("Apply Calm Preset")]
    void ApplyCalm() => ApplyPreset(HoverPreset.Calm);

    [ContextMenu("Apply Normal Preset")]
    void ApplyNormal() => ApplyPreset(HoverPreset.Normal);

    [ContextMenu("Apply Rough Preset")]
    void ApplyRough() => ApplyPreset(HoverPreset.Rough);

    void OnDrawGizmosSelected()
    {
        if (hoverCenter == null) return;
        float drawRadius = Application.isPlaying ? activeRadius : useStartingRadius
            ? new Vector2(transform.position.x - hoverCenter.position.x, transform.position.z - hoverCenter.position.z).magnitude
            : radius;
        var center = hoverCenter.position + Vector3.up * heightAboveCenter;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(hoverCenter.position, 1f);
        Gizmos.DrawLine(hoverCenter.position, center);
        var previous = center + Vector3.right * drawRadius;
        for (int segment = 1; segment <= 64; segment++)
        {
            float radians = segment * Mathf.PI * 2f / 64f;
            var next = center + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * drawRadius;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(transform.position, center);
        Gizmos.color = Color.yellow;
        if (xrParent != null)
        {
            float eyeHeight = origin != null && origin.GetComponent<XrOriginPlaySettings>() != null
                ? origin.GetComponent<XrOriginPlaySettings>().EyeHeightMeters * (Application.isPlaying && riding ? 1f : xrHeightMultiplier)
                : 1.8f * xrHeightMultiplier;
            Gizmos.DrawWireCube(xrParent.position + Vector3.up * eyeHeight * 0.5f, new Vector3(0.4f, eyeHeight, 0.4f));
            Gizmos.DrawLine(xrParent.position + Vector3.up * eyeHeight, xrParent.position + Vector3.up * eyeHeight + xrParent.forward);
        }
        if (missionStartSpawn != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(missionStartSpawn.position + Vector3.up * 0.05f, new Vector3(0.6f, 0.1f, 0.6f));
            Gizmos.DrawLine(missionStartSpawn.position, missionStartSpawn.position + missionStartSpawn.forward * 2f);
        }
    }
}
