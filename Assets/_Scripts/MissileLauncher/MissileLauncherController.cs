using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Two-phase VR missile launcher. The rotate and fire actions are intentionally separate:
/// rotate the container first, then fire the missiles with the second button.
/// </summary>
[System.Serializable]
public sealed class MissileLaunchedEvent : UnityEvent<MissileProjectile> { }

public sealed class MissileLauncherController : MonoBehaviour
{
    /// <summary>One notification per missile dispatched by this container launcher.</summary>
    public static event System.Action<MissileLauncherController> MissileFired;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetFireEvent() => MissileFired = null;

    [System.Serializable]
    public sealed class MissileSlot
    {
        [Tooltip("A prefab asset is spawned; a scene object is launched in place and restored by Reset Launcher.")]
        [InspectorName("Projectile (Prefab or Scene Object)")]
        public MissileProjectile prefab;
        public Transform launchPoint;
        [Min(0f)] public float extraDelay;
    }

    [Header("Phase 1 - container rotation")]
    [SerializeField] Transform container;
    [SerializeField] Vector3 localRotationAxis = Vector3.right;
    [SerializeField] float rotationDegrees = 70f;
    [SerializeField, Min(0f)] float rotationDuration = 1.25f;
    [SerializeField] AnimationCurve rotationCurve = null;
    [SerializeField] bool allowRotationOnlyOnce = true;

    [Header("Phase 2 - missile sequence")]
    [SerializeField] MissileSlot[] missiles = new MissileSlot[0];
    [Tooltip("The missile model's local nose axis. Transformed by each missile's pose after container rotation, not by the container.")]
    [SerializeField] Vector3 localSkyDirection = Vector3.up;
    [SerializeField, Min(0f)] float skyDistance = 40f;
    [SerializeField, Min(0f)] float arcHeight = 8f;
    [SerializeField] AnimationCurve flightCurve = null;
    [SerializeField, Min(0f)] float delayBetweenMissiles = 0.35f;
    [SerializeField, Min(0f)] float randomDelay = 0.12f;
    [SerializeField] bool requireRotationPhase = true;

    [Header("Events")]
    public UnityEvent rotationStarted = new UnityEvent();
    public UnityEvent rotationCompleted = new UnityEvent();
    public UnityEvent missileSequenceStarted = new UnityEvent();
    public UnityEvent missileSequenceCompleted = new UnityEvent();
    public MissileLaunchedEvent missileLaunched = new MissileLaunchedEvent();

    public bool RotationCompleted { get; private set; }
    public bool IsRotating { get; private set; }
    public bool IsLaunching { get; private set; }

    Quaternion initialLocalRotation;
    Coroutine rotationRoutine;
    Coroutine launchRoutine;
    readonly List<MissileProjectile> spawnedMissiles = new List<MissileProjectile>();

    void Reset()
    {
        container = transform;
        rotationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        flightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    void Awake()
    {
        if (container == null) container = transform;
        initialLocalRotation = container.localRotation;
        if (rotationCurve == null || rotationCurve.length == 0) rotationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        if (flightCurve == null || flightCurve.length == 0) flightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        foreach (var slot in missiles)
            if (slot != null && slot.prefab != null && slot.prefab.gameObject.scene.IsValid())
                slot.prefab.CaptureInitialPose();
    }

    [ContextMenu("Test/Phase 1 - Rotate Container (Play Mode)")]
    public void RotateContainer()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsLaunching || IsRotating || (allowRotationOnlyOnce && RotationCompleted)) return;
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        rotationRoutine = StartCoroutine(RotateRoutine());
    }

    [ContextMenu("Test/Phase 2 - Launch Missiles (Play Mode)")]
    public void LaunchMissiles()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsRotating || IsLaunching || (requireRotationPhase && !RotationCompleted)) return;
        if (launchRoutine != null) StopCoroutine(launchRoutine);
        launchRoutine = StartCoroutine(LaunchRoutine());
    }

    [ContextMenu("Test/Reset Launcher and Missiles (Play Mode)")]
    public void ResetLauncher()
    {
        if (!Application.isPlaying) return;
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        if (launchRoutine != null) StopCoroutine(launchRoutine);
        rotationRoutine = null;
        launchRoutine = null;
        IsRotating = false;
        IsLaunching = false;
        RotationCompleted = false;
        container.localRotation = initialLocalRotation;
        foreach (var missile in spawnedMissiles)
            if (missile != null) { missile.ResetProjectile(); missile.gameObject.SetActive(false); Destroy(missile.gameObject); }
        spawnedMissiles.Clear();
        foreach (var slot in missiles)
            if (slot != null && slot.prefab != null && slot.prefab.gameObject.scene.IsValid())
                slot.prefab.ResetProjectile();
    }

    IEnumerator RotateRoutine()
    {
        IsRotating = true;
        rotationStarted?.Invoke();
        var start = container.localRotation;
        var axis = localRotationAxis.sqrMagnitude > 0.0001f ? localRotationAxis.normalized : Vector3.right;
        var end = initialLocalRotation * Quaternion.AngleAxis(rotationDegrees, axis);
        var elapsed = 0f;
        while (elapsed < rotationDuration)
        {
            elapsed += Time.deltaTime;
            var t = rotationDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / rotationDuration);
            container.localRotation = Quaternion.SlerpUnclamped(start, end, rotationCurve.Evaluate(t));
            yield return null;
        }
        container.localRotation = end;
        IsRotating = false;
        RotationCompleted = true;
        rotationCompleted?.Invoke();
    }

    IEnumerator LaunchRoutine()
    {
        IsLaunching = true;
        missileSequenceStarted?.Invoke();
        for (var i = 0; i < missiles.Length; i++)
        {
            var slot = missiles[i];
            if (slot == null || slot.prefab == null) continue;
            var point = slot.launchPoint != null ? slot.launchPoint : container;
            var isSceneMissile = slot.prefab.gameObject.scene.IsValid();
            if (isSceneMissile && slot.prefab.HasLaunched) continue;
            var missile = isSceneMissile ? slot.prefab : Instantiate(slot.prefab, point.position, point.rotation);
            if (!isSceneMissile) spawnedMissiles.Add(missile);
            // Scene missiles keep their authored pose, even when a launch point is assigned.
            var direction = missile.transform.TransformDirection(localSkyDirection.sqrMagnitude > 0.0001f ? localSkyDirection.normalized : Vector3.up);
            missile.Launch(missile.transform.position + direction * skyDistance, arcHeight, flightCurve);
            MissileFired?.Invoke(this);
            missileLaunched?.Invoke(missile);
            var wait = delayBetweenMissiles + slot.extraDelay + Random.Range(-randomDelay, randomDelay);
            if (wait > 0f) yield return new WaitForSeconds(wait);
        }
        IsLaunching = false;
        missileSequenceCompleted?.Invoke();
    }

}



