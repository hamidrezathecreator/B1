using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Two-phase VR missile launcher. The rotate and fire actions are intentionally separate:
/// rotate the container first, then fire the missiles with the second button.
/// </summary>
[System.Serializable]
public sealed class MissileLaunchedEvent : UnityEvent<MissileProjectile> { }

public sealed class MissileLauncherController : MonoBehaviour
{
    [System.Serializable]
    public sealed class MissileSlot
    {
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
    [SerializeField] InputActionReference rotateAction;

    [Header("Phase 2 - missile sequence")]
    [SerializeField] MissileSlot[] missiles = new MissileSlot[0];
    [SerializeField] Vector3 localSkyDirection = Vector3.up;
    [SerializeField, Min(0f)] float skyDistance = 40f;
    [SerializeField, Min(0f)] float arcHeight = 8f;
    [SerializeField] AnimationCurve flightCurve = null;
    [SerializeField, Min(0f)] float delayBetweenMissiles = 0.35f;
    [SerializeField, Min(0f)] float randomDelay = 0.12f;
    [SerializeField] bool requireRotationPhase = true;
    [SerializeField] InputActionReference fireAction;

    [Header("Events")]
    public UnityEvent rotationStarted;
    public UnityEvent rotationCompleted;
    public UnityEvent missileSequenceStarted;
    public UnityEvent missileSequenceCompleted;
    public MissileLaunchedEvent missileLaunched;

    public bool RotationCompleted { get; private set; }
    public bool IsRotating { get; private set; }
    public bool IsLaunching { get; private set; }

    Quaternion initialLocalRotation;
    Coroutine rotationRoutine;
    Coroutine launchRoutine;

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
    }

    void OnEnable()
    {
        Bind(rotateAction, OnRotatePressed);
        Bind(fireAction, OnFirePressed);
    }

    void OnDisable()
    {
        Unbind(rotateAction, OnRotatePressed);
        Unbind(fireAction, OnFirePressed);
    }

    void OnRotatePressed(InputAction.CallbackContext _) => RotateContainer();
    void OnFirePressed(InputAction.CallbackContext _) => LaunchMissiles();

    public void RotateContainer()
    {
        if (IsRotating || (allowRotationOnlyOnce && RotationCompleted)) return;
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        rotationRoutine = StartCoroutine(RotateRoutine());
    }

    public void LaunchMissiles()
    {
        if (IsLaunching || (requireRotationPhase && !RotationCompleted)) return;
        if (launchRoutine != null) StopCoroutine(launchRoutine);
        launchRoutine = StartCoroutine(LaunchRoutine());
    }

    public void ResetLauncher()
    {
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        if (launchRoutine != null) StopCoroutine(launchRoutine);
        IsRotating = false;
        IsLaunching = false;
        RotationCompleted = false;
        container.localRotation = initialLocalRotation;
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
        var direction = container.TransformDirection(localSkyDirection.sqrMagnitude > 0.0001f ? localSkyDirection.normalized : Vector3.up);
        for (var i = 0; i < missiles.Length; i++)
        {
            var slot = missiles[i];
            if (slot == null || slot.prefab == null) continue;
            var point = slot.launchPoint != null ? slot.launchPoint : container;
            var missile = Instantiate(slot.prefab, point.position, point.rotation);
            missile.Launch(point.position + direction * skyDistance, arcHeight, flightCurve);
            missileLaunched?.Invoke(missile);
            var wait = delayBetweenMissiles + slot.extraDelay + Random.Range(-randomDelay, randomDelay);
            if (wait > 0f) yield return new WaitForSeconds(wait);
        }
        IsLaunching = false;
        missileSequenceCompleted?.Invoke();
    }

    static void Bind(InputActionReference reference, System.Action<InputAction.CallbackContext> callback)
    {
        if (reference == null || reference.action == null) return;
        reference.action.performed += callback;
        reference.action.Enable();
    }

    static void Unbind(InputActionReference reference, System.Action<InputAction.CallbackContext> callback)
    {
        if (reference == null || reference.action == null) return;
        reference.action.performed -= callback;
    }
}



