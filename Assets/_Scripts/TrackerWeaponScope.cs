using System.Collections.Generic;
using PrimeTween;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Grabbable tracker weapon with a zoom scope: a camera just past the front looks where you aim,
/// shows the view on the weapon screen, tracks a naive lock on the drone, then fires a homing missile.
/// Assign the scene camera, target and launch origin in the Inspector.
/// </summary>
[AddComponentMenu("B1/Tracker Weapon Scope")]
public sealed class TrackerWeaponScope : MonoBehaviour
{
    public enum Axis { Auto, PlusX, MinusX, PlusY, MinusY, PlusZ, MinusZ }
    public enum ReticleState { Idle, Acquisition, FullyLocked }

    [Header("Scope camera")]
    [Tooltip("Assigned scene camera that renders the scope picture and aims the lock ray.")]
    [SerializeField] Camera laserCamera;
    [SerializeField] XROrigin origin;
    [SerializeField] Transform rightController;
    [Tooltip("How far past the front end of the weapon the camera sits, meters.")]
    [Min(0f)] [SerializeField] float cameraAhead = 0.3f;
    [Tooltip("Direction the weapon fires in its own space. Auto = its long axis, away from the screen.")]
    [SerializeField] Axis aimAxis = Axis.Auto;
    [Tooltip("Extra yaw on the scope aim, degrees. Positive = turn right. TrackerWeapon mesh often needs +90.")]
    [SerializeField] float aimYawOffset = 90f;
    [Min(1f)] [SerializeField] float farClip = 3000f;
    [Tooltip("Render texture height in pixels; the width follows the screen's shape.")]
    [Range(128, 1024)] [SerializeField] int textureHeight = 512;

    [Header("Screen")]
    [Tooltip("Material slot of the white screen on the weapon mesh (M_Glass).")]
    [Min(0)] [SerializeField] int screenMaterialIndex = 1;
    [SerializeField] Material scopeScreenMaterial;

    [Header("Grab")]
    [SerializeField] bool grabbable = true;

    [Header("Target lock")]
    [Tooltip("Assigned scene target for the scope lock.")]
    [SerializeField] Transform target;

    [Tooltip("Seconds the target must stay in the crosshair before the missile goes.")]
    [Min(0.1f)] [SerializeField] float lockSeconds = 1f;
    [Tooltip("How far off the camera's line the target may be and still count, degrees. 0 = the ray must hit it.")]
    [Range(0f, 15f)] [SerializeField] float aimToleranceDegrees = 2.5f;
    [Tooltip("Only lock while the weapon is in the player's hand.")]
    [SerializeField] bool requireHeld = true;
    [SerializeField] Color crosshairColor = Color.white;
    [SerializeField] Color lockedColor = new Color(0.2f, 1f, 0.3f, 1f);
    [SerializeField] Color fullLockColor = new Color(1f, 0.15f, 0.1f, 1f);
    [Range(0.1f, 1f)] [SerializeField] float crosshairScale = 0.6f;

    [Header("Tracker canvas states")]
    [Tooltip("Your canvas for the tracker UI. Its authored position, rotation and size are preserved.")]
    [SerializeField] Canvas trackerCanvas;
    [Tooltip("Optional Image under your canvas. Its layout and color are preserved; only its sprite and feedback motion change. Empty creates an image when state sprites are assigned.")]
    [SerializeField] UnityEngine.UI.Image trackerStateImage;
    [SerializeField] Sprite idleSprite;
    [SerializeField] Sprite acquisitionSprite;
    [SerializeField] Sprite fullyLockedSprite;

    [Header("Crosshair feedback")]
    [Min(0.2f)] [SerializeField] float breathSeconds = 1.2f;
    [Range(0.005f, 0.1f)] [SerializeField] float breathAmount = 0.025f;
    [Range(0f, 2f)] [SerializeField] float acquisitionShake = 0.65f;
    [Tooltip("Maximum shake pulses per uninterrupted acquisition. Losing aim resets the count. Zero disables acquisition shakes.")]
    [Min(0)] [SerializeField] int acquisitionShakeCount = 6;
    [Range(0f, 3f)] [SerializeField] float confirmationShake = 1.1f;
    [Tooltip("Shake and beep pulses at full lock before launch. Zero launches immediately after acquisition.")]
    [Min(0)] [SerializeField] int confirmationShakeCount = 3;
    [Min(0.08f)] [SerializeField] float confirmationShakeSeconds = 0.16f;
    [SerializeField] AudioClip trackingBeep;
    [SerializeField] AudioClip lockedBeep;
    [Range(0f, 1f)] [SerializeField] float beepVolume = 0.25f;

    [Header("Missile on lock")]
    [Tooltip("Missile model, nose along +Z. Empty = a simple built-in missile.")]
    [SerializeField] GameObject missilePrefab;
    [Min(0.01f)] [SerializeField] float missileScale = 0.33333334f;
    [SerializeField] Transform missileLaunchOrigin;
    [SerializeField] Vector3 missileLaunchOffset = new Vector3(0f, 0f, 0.5f);
    [Min(0.1f)] [SerializeField] float missileSeconds = 1.2f;
    [SerializeField] AudioClip missileLaunchSound;
    [Range(0f, 1f)] [SerializeField] float missileLaunchVolume = 1f;
    [SerializeField] AudioClip missileImpactSound;
    [Range(0f, 1f)] [SerializeField] float missileImpactVolume = 1f;

    [Header("Scope thermal")]
    [Tooltip("FLIR-style view on the weapon screen camera only (HMD stays normal).")]
    [SerializeField] bool scopeThermalView = true;
    [Tooltip("Keep Camera Lase rendering to the weapon screen even when not held (needed to see the feed).")]
    [SerializeField] bool keepScopeFeedLive = true;
    [Tooltip("Quest context button (B / Y) toggles scope thermal. Does not require holding the weapon.")]
    [SerializeField] bool thermalToggledByContextButton = true;
    [SerializeField] bool thermalContextButtonRequiresHeld;
    [SerializeField] Material scopeThermalPostMaterial;
    [SerializeField] Material thermalReplacementMaterial;
    [SerializeField] Material droneHeatmapMaterial;

    [Header("Kill feedback")]
    [Tooltip("Shown above the right controller after SM_Shahed149 is destroyed.")]
    [SerializeField] string killMessage = "پهباد جاسوسی رو زدی\nآفرین";
    [SerializeField] TMPro.TMP_FontAsset killMessageFont;
    [Min(0.5f)] [SerializeField] float killMessageSeconds = 4f;
    [Min(0.5f)] [SerializeField] float killExplosionScale = 1.4f;
    [SerializeField] GameObject impactParticlePrefab;
    [Min(1f)] [SerializeField] float impactParticleLifetime = 12f;

    Camera scopeCamera;
    readonly System.Collections.Generic.List<UnityEngine.UI.Image> crosshair = new System.Collections.Generic.List<UnityEngine.UI.Image>();
    Renderer[] targetRenderers;
    float lockTimer;
    bool missileAway;
    bool targetDown;
    public bool TargetDestroyed => targetDown;
    public bool IsHeld => grab != null && grab.isSelected;
    public event System.Action TargetEliminated;
    RenderTexture texture;
    MeshRenderer weaponRenderer;
    Renderer screenRenderer;
    Material screenMaterial;
    XRGrabInteractable grab;
    Rigidbody body;
    Vector3 homePosition;
    Quaternion homeRotation;
    TrackerScopeThermalVision scopeThermalDriver;
    bool thermalEngaged;
    bool engagementEnabled = true;
    bool scopeReady;
    bool wasContextButton;
    RectTransform trackingReticle;
    RectTransform fullLockReticle;
    RectTransform reticleMotion;
    Vector3 reticleRestPosition;
    Vector3 reticleRestScale;
    Sprite defaultReticleSprite;
    int acquisitionShakesPlayed;
    public ReticleState CurrentReticleState { get; private set; }
    Tween breathTween;
    Tween shakeTween;
    Tween missileTween;
    bool targetVisible;
    bool confirmingLock;
    float nextBeep;
    float nextShake;
    AudioSource feedbackAudio;
    AudioClip generatedBeep;
    GameObject activeMissile;
    ParticleSystem activeTrail;
    readonly List<InputDevice> contextInputDevices = new List<InputDevice>();

    public bool ThermalEngaged => thermalEngaged;

    public void ConfigureFeedback(AudioClip launchSound, AudioClip impactSound, TMPro.TMP_FontAsset font)
    {
        if (launchSound != null) missileLaunchSound = launchSound;
        if (impactSound != null) missileImpactSound = impactSound;
        if (font != null) killMessageFont = font;
    }

    public void AssignThermalMaterials(Material scopeThermalPost, Material droneHeatmap) =>
        AssignThermalMaterials(scopeThermalPost, null, droneHeatmap);

    public void AssignThermalMaterials(Material scopeThermalPost, Material replacement, Material droneHeatmap)
    {
        if (scopeThermalPost != null) scopeThermalPostMaterial = scopeThermalPost;
        if (replacement != null) thermalReplacementMaterial = replacement;
        if (droneHeatmap != null) droneHeatmapMaterial = droneHeatmap;
    }

    void Start()
    {
        if (laserCamera == null || origin == null || missileLaunchOrigin == null || target == null ||
            (grabbable && (GetComponent<XRGrabInteractable>() == null || GetComponent<Rigidbody>() == null)))
        {
            Debug.LogError("TrackerWeaponScope: assign the scope camera, player, launch origin, target and authored grab components.", this);
            enabled = false;
            return;
        }
        homePosition = transform.position;
        homeRotation = transform.rotation;

        var filter = GetComponent<MeshFilter>();
        weaponRenderer = GetComponent<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || weaponRenderer == null)
        {
            Debug.LogWarning(name + ": Tracker Weapon Scope needs the weapon's MeshFilter and MeshRenderer on this object.", this);
            return;
        }

        var mesh = filter.sharedMesh;
        var whole = mesh.bounds;
        var hasScreen = screenMaterialIndex < mesh.subMeshCount;
        var screenBounds = hasScreen ? mesh.GetSubMesh(screenMaterialIndex).bounds : whole;
        var bodyBounds = mesh.subMeshCount > 1 && screenMaterialIndex != 0 ? mesh.GetSubMesh(0).bounds : whole;
        if (bodyBounds.size.sqrMagnitude < 1e-8f) bodyBounds = whole;

        var aimLocal = AimDirection(bodyBounds, screenBounds, hasScreen);
        var screenNormal = hasScreen ? ScreenNormal(screenBounds, whole) : -aimLocal;
        var screenUp = ScreenUp(screenNormal, aimLocal);

        texture = CreateTexture(screenBounds, screenNormal, screenUp, hasScreen);
        UseSceneCamera(bodyBounds, aimLocal, screenUp);
        if (hasScreen)
        {
            CreateScreen(weaponRenderer, screenBounds, screenNormal, screenUp);
            ApplyTrackerScreenTexture(weaponRenderer);
        }
        if (hasScreen || trackerCanvas != null || trackerStateImage != null)
            CreateCrosshair(screenBounds, screenNormal, screenUp);
        if (grabbable) MakeGrabbable(whole);
        CacheTargetRenderers();
        scopeReady = true;
        SetupScopeThermal();
        WakeScopeCamera();
    }

    void SetupScopeThermal()
    {
        if (scopeCamera == null) return;
        if (scopeThermalView && target != null && droneHeatmapMaterial != null)
            ThermalDroneHeatmap.Apply(target, droneHeatmapMaterial);
        WakeScopeCamera();
    }

    void LinkScopeThermalDriver()
    {
        scopeThermalDriver = null;
        if (!scopeThermalView || scopeCamera == null) return;

        scopeThermalDriver = scopeCamera.GetComponent<TrackerScopeThermalVision>();
        if (scopeThermalDriver == null)
        {
            Debug.LogError(
                name + ": Assign TrackerScopeThermalVision + ThermalMonitorCamera on the scope camera (e.g. Camera Lase).",
                this);
            return;
        }

        scopeThermalDriver.Prepare(texture, thermalReplacementMaterial);
        scopeThermalDriver.SetUserThermalEnabled(thermalEngaged);
    }

    public void SetThermalEngaged(bool engaged)
    {
        thermalEngaged = engaged && scopeThermalView && engagementEnabled;
        scopeThermalDriver?.SetUserThermalEnabled(thermalEngaged);
        if (thermalEngaged && scopeCamera != null)
            ThermalDroneHeatmap.Apply(target, droneHeatmapMaterial);
        WakeScopeCamera();
        Debug.Log(
            name + ": tracker scope thermal " + (thermalEngaged ? "ON" : "OFF") +
            (scopeCamera != null ? " (camera: " + scopeCamera.name + ", enabled=" + scopeCamera.enabled + ")" : " (no scope camera)"),
            this);
    }

    public void SetEngagementEnabled(bool enabled)
    {
        engagementEnabled = enabled;
        lockTimer = 0f;
        SetThermalEngaged(enabled);
    }

    /// <summary>Called after XR headset camera setup so scope cameras keep their render texture.</summary>
    public void WakeScopeCamera()
    {
        if (!scopeReady || scopeCamera == null || texture == null) return;
        scopeCamera.gameObject.SetActive(true);
        scopeCamera.targetTexture = texture;
        scopeCamera.rect = new Rect(0f, 0f, 1f, 1f);
        ConfigureScopeUrpCamera(scopeCamera.GetComponent<UniversalAdditionalCameraData>(), scopeCamera);
        UpdateScopeCameraEnabled();
        RefreshScopeDisplays();
        LinkScopeThermalDriver();
    }

    void UpdateScopeCameraEnabled()
    {
        if (scopeCamera == null) return;
        var held = grab != null && grab.isSelected;
        scopeCamera.enabled = isActiveAndEnabled && (keepScopeFeedLive || held || thermalEngaged);
    }

    void RefreshScopeDisplays()
    {
        if (texture == null) return;
        if (weaponRenderer != null) ApplyTrackerScreenTexture(weaponRenderer);
        if (screenRenderer == null) return;
        var mat = screenRenderer.sharedMaterial;
        if (mat == null) return;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
        mat.mainTexture = texture;
    }

    public void ToggleThermalEngaged() => SetThermalEngaged(!thermalEngaged);

    [ContextMenu("Enable Thermal Vision (Play Mode)")]
    void ContextEnableThermal()
    {
        if (!Application.isPlaying) return;
        SetThermalEngaged(true);
    }

    [ContextMenu("Disable Thermal Vision (Play Mode)")]
    void ContextDisableThermal()
    {
        if (!Application.isPlaying) return;
        SetThermalEngaged(false);
    }

    [ContextMenu("Toggle Thermal Vision (Play Mode)")]
    void ContextToggleThermal()
    {
        if (!Application.isPlaying) return;
        ToggleThermalEngaged();
    }

    /// <summary>Renders the scope with the scene's laser camera, if there is one.</summary>
    bool UseSceneCamera(Bounds bodyBounds, Vector3 aimLocal, Vector3 upLocal)
    {
        if (laserCamera == null) return false;

        scopeCamera = laserCamera;
        // Child of the weapon so it turns with the grip; identity world pose was often +Z, not the barrel.


        // Level setup switches off every MainCamera except the headset; this one only draws the scope.
        if (scopeCamera.CompareTag("MainCamera")) scopeCamera.tag = "Untagged";
        var listener = scopeCamera.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = false;

        scopeCamera.gameObject.SetActive(true);
        scopeCamera.targetTexture = texture;
        scopeCamera.rect = new Rect(0f, 0f, 1f, 1f);
        scopeCamera.depth = -10f;
        ConfigureScopeUrpCamera(scopeCamera.GetComponent<UniversalAdditionalCameraData>(), scopeCamera);
        farClip = Mathf.Max(farClip, scopeCamera.farClipPlane);
        scopeCamera.farClipPlane = farClip;
        UpdateScopeCameraEnabled();

        // Extra laser cameras under the weapon must not also render into the headset.
        foreach (var cam in GetComponentsInChildren<Camera>(true))
        {
            if (cam == scopeCamera) continue;
            cam.enabled = false;
            var otherListener = cam.GetComponent<AudioListener>();
            if (otherListener != null) otherListener.enabled = false;
            ConfigureScopeUrpCamera(cam.GetComponent<UniversalAdditionalCameraData>(), cam);
        }

        Debug.Log(name + ": scope uses the scene camera '" + scopeCamera.name + "' along aim " + aimLocal + ".", this);
        return true;
    }

    void AlignScopeCamera(Transform cam, Bounds bodyBounds, Vector3 aimLocal, Vector3 upLocal)
    {
        var aimWorld = transform.TransformDirection(aimLocal).normalized;
        var upWorld = transform.TransformDirection(upLocal).normalized;
        if (aimWorld.sqrMagnitude < 1e-8f) aimWorld = transform.forward;
        if (Mathf.Abs(Vector3.Dot(upWorld, aimWorld)) > 0.98f)
            upWorld = Vector3.Cross(aimWorld, transform.right).normalized;
        if (upWorld.sqrMagnitude < 1e-8f) upWorld = Vector3.up;

        // Turn the aim around "up" so the RT matches the barrel (mesh Auto was 90° left).
        if (Mathf.Abs(aimYawOffset) > 0.01f)
            aimWorld = Quaternion.AngleAxis(aimYawOffset, upWorld) * aimWorld;

        var muzzle = transform.TransformPoint(bodyBounds.center + Vector3.Scale(aimLocal, bodyBounds.extents));
        cam.SetPositionAndRotation(muzzle + aimWorld * cameraAhead, Quaternion.LookRotation(aimWorld, upWorld));
    }

    void CacheTargetRenderers()
    {
        targetRenderers = target != null ? target.GetComponentsInChildren<Renderer>(true) : null;
    }

    bool TargetBounds(out Bounds bounds)
    {
        bounds = default;
        if (targetRenderers == null) return false;
        var has = false;
        foreach (var r in targetRenderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return has;
    }

    /// <summary>White cross in the middle of the scope picture, drawn with UI on top of the screen.</summary>
    void CreateCrosshair(Bounds screen, Vector3 normal, Vector3 up)
    {
        if (trackerStateImage != null)
        {
            if (trackerCanvas == null) trackerCanvas = trackerStateImage.canvas;
            InitializeReticleMotion(trackerStateImage.rectTransform);
            return;
        }
        var right = Vector3.Cross(up, normal);
        var width = Mathf.Abs(Vector3.Dot(screen.size, Abs(right))) * 0.96f;
        var height = Mathf.Abs(Vector3.Dot(screen.size, Abs(up))) * 0.96f;
        var thickness = Mathf.Abs(Vector3.Dot(screen.extents, Abs(normal)));
        var lift = 0.004f / Mathf.Max(0.0001f, transform.lossyScale.magnitude / 1.732f);
        if (height < 1e-5f) return;

        const float canvasHeight = 200f;
        if (trackerCanvas == null)
            foreach (var candidate in GetComponentsInChildren<Canvas>(true))
                if (candidate.renderMode == RenderMode.WorldSpace)
                {
                    trackerCanvas = candidate;
                    break;
                }
        if (trackerCanvas == null)
        {
            var canvasObject = new GameObject("Tracker Screen Canvas", typeof(RectTransform), typeof(Canvas));
            trackerCanvas = canvasObject.GetComponent<Canvas>();
            trackerCanvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.SetParent(transform, false);
            canvasRect.localPosition = screen.center + normal * (thickness + lift);
            canvasRect.localRotation = Quaternion.LookRotation(-normal, up);
            canvasRect.localScale = Vector3.one * (height / canvasHeight);
            canvasRect.sizeDelta = new Vector2(canvasHeight * width / height, canvasHeight);
        }
        var go = new GameObject("Scope Crosshair", typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(trackerCanvas.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(canvasHeight, canvasHeight);
        rect.localScale = Vector3.one * (Mathf.Abs(((RectTransform)trackerCanvas.transform).rect.height) / canvasHeight * crosshairScale);
        var motion = new GameObject("Crosshair Motion", typeof(RectTransform)).GetComponent<RectTransform>();
        motion.SetParent(rect, false);
        motion.sizeDelta = rect.sizeDelta;
        if (idleSprite != null || acquisitionSprite != null || fullyLockedSprite != null)
        {
            trackerStateImage = new GameObject("Tracker State Sprite", typeof(RectTransform),
                typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
            trackerStateImage.rectTransform.SetParent(motion, false);
            trackerStateImage.rectTransform.anchorMin = Vector2.zero;
            trackerStateImage.rectTransform.anchorMax = Vector2.one;
            trackerStateImage.rectTransform.sizeDelta = Vector2.zero;
            trackerStateImage.raycastTarget = false;
            trackerStateImage.preserveAspect = true;
            InitializeReticleMotion(motion);
            return;
        }
        trackingReticle = new GameObject("Tracking Reticle", typeof(RectTransform)).GetComponent<RectTransform>();
        trackingReticle.SetParent(motion, false);
        fullLockReticle = new GameObject("Full Lock Reticle", typeof(RectTransform)).GetComponent<RectTransform>();
        fullLockReticle.SetParent(motion, false);

        const float gap = 12f;
        const float length = 26f;
        const float bar = 4f;
        AddBar(trackingReticle, new Vector2(0f, gap + length * 0.5f), new Vector2(bar, length));
        AddBar(trackingReticle, new Vector2(0f, -gap - length * 0.5f), new Vector2(bar, length));
        AddBar(trackingReticle, new Vector2(gap + length * 0.5f, 0f), new Vector2(length, bar));
        AddBar(trackingReticle, new Vector2(-gap - length * 0.5f, 0f), new Vector2(length, bar));
        AddBar(trackingReticle, Vector2.zero, new Vector2(bar, bar));
        const float corner = 23f;
        const float bracketLength = 12f;
        foreach (var horizontalSign in new[] { -1f, 1f })
            foreach (var verticalSign in new[] { -1f, 1f })
            {
                AddBar(fullLockReticle, new Vector2(horizontalSign * (corner - bracketLength * 0.5f), verticalSign * corner),
                    new Vector2(bracketLength, bar));
                AddBar(fullLockReticle, new Vector2(horizontalSign * corner, verticalSign * (corner - bracketLength * 0.5f)),
                    new Vector2(bar, bracketLength));
            }
        AddBar(fullLockReticle, Vector2.zero, new Vector2(bar, bar));
        InitializeReticleMotion(motion);
        SetCrosshairColor(crosshairColor);
    }

    void InitializeReticleMotion(RectTransform motion)
    {
        reticleMotion = motion;
        reticleRestPosition = motion.localPosition;
        reticleRestScale = motion.localScale;
        defaultReticleSprite = trackerStateImage != null ? trackerStateImage.sprite : null;
        SetReticleState(ReticleState.Idle);
    }

    void SetReticleState(ReticleState state)
    {
        CurrentReticleState = state;
        if (trackerStateImage != null)
        {
            var sprite = state == ReticleState.FullyLocked ? fullyLockedSprite
                : state == ReticleState.Acquisition ? acquisitionSprite : idleSprite;
            if (sprite == null) sprite = idleSprite != null ? idleSprite : defaultReticleSprite;
            trackerStateImage.sprite = sprite;
            trackerStateImage.enabled = sprite != null;
        }
        if (trackingReticle != null) trackingReticle.gameObject.SetActive(state != ReticleState.FullyLocked);
        if (fullLockReticle != null) fullLockReticle.gameObject.SetActive(state == ReticleState.FullyLocked);
    }

    void StartBreathing(bool visible)
    {
        if (breathTween.isAlive) breathTween.Stop();
        if (reticleMotion == null) return;
        reticleMotion.localScale = reticleRestScale;
        breathTween = Tween.Scale(reticleMotion, reticleRestScale * (1f - breathAmount), reticleRestScale * (1f + breathAmount),
            Mathf.Max(0.1f, breathSeconds) * (visible ? 0.35f : 0.5f),
            ease: Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo);
    }

    void ShakeCrosshair(float strength, float seconds)
    {
        if (shakeTween.isAlive) shakeTween.Stop();
        if (reticleMotion == null) return;
        reticleMotion.localPosition = reticleRestPosition;
        if (strength <= 0f) return;
        shakeTween = Tween.ShakeLocalPosition(reticleMotion, new Vector3(strength, strength * 0.7f, 0f), seconds, 24f);
    }

    void PlayFeedbackBeep(bool locked, float progress)
    {
        if (feedbackAudio == null)
        {
            feedbackAudio = gameObject.AddComponent<AudioSource>();
            feedbackAudio.playOnAwake = false;
            feedbackAudio.spatialBlend = 1f;
            feedbackAudio.minDistance = 0.5f;
            feedbackAudio.maxDistance = 8f;
            feedbackAudio.rolloffMode = AudioRolloffMode.Linear;
        }
        var clip = locked ? lockedBeep : trackingBeep;
        if (clip == null)
        {
            if (generatedBeep == null)
            {
                const int sampleRate = 24000;
                var samples = new float[1920];
                for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    var phase = (float)sampleIndex / (samples.Length - 1);
                    samples[sampleIndex] = Mathf.Sin(2f * Mathf.PI * 1100f * sampleIndex / sampleRate) *
                        Mathf.Sin(Mathf.PI * phase) * 0.5f;
                }
                generatedBeep = AudioClip.Create("Tracker Beep", samples.Length, 1, sampleRate, false);
                generatedBeep.SetData(samples, 0);
            }
            clip = generatedBeep;
        }
        feedbackAudio.pitch = locked ? 1.45f : Mathf.Lerp(0.9f, 1.25f, progress);
        feedbackAudio.PlayOneShot(clip, beepVolume);
    }

    void AddBar(RectTransform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Bar", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.raycastTarget = false;
        crosshair.Add(image);
    }

    void SetCrosshairColor(Color color)
    {
        foreach (var image in crosshair)
            if (image != null) image.color = color;
    }

    Vector3 AimDirection(Bounds bodyBounds, Bounds screenBounds, bool hasScreen)
    {
        switch (aimAxis)
        {
            case Axis.PlusX: return Vector3.right;
            case Axis.MinusX: return Vector3.left;
            case Axis.PlusY: return Vector3.up;
            case Axis.MinusY: return Vector3.down;
            case Axis.PlusZ: return Vector3.forward;
            case Axis.MinusZ: return Vector3.back;
        }

        // Barrel = longest local axis that stays mostly horizontal in the world.
        // The tracker weapon mesh is taller in Y, so "longest = Y" wrongly aimed the scope at the sky.
        var size = bodyBounds.size;
        var axis = Vector3.forward;
        var bestScore = float.NegativeInfinity;
        foreach (var candidate in new[] { Vector3.right, Vector3.up, Vector3.forward })
        {
            var length = Vector3.Dot(size, Abs(candidate));
            var world = transform.TransformDirection(candidate).normalized;
            // 1 = flat along the ground, 0 = straight up/down (the green arrow the user rejected).
            var horizontal = 1f - Mathf.Abs(world.y);
            var score = length * (0.35f + horizontal);
            if (score <= bestScore) continue;
            bestScore = score;
            axis = candidate;
        }

        var half = Vector3.Dot(bodyBounds.extents, axis);
        if (hasScreen)
        {
            // Muzzle = the end where the screen sits behind the camera (LCD toward the grip).
            var muzzle = bodyBounds.center + axis * half;
            if (Vector3.Dot(muzzle - screenBounds.center, axis) < 0f) return -axis;
            return axis;
        }

        // Prefer the end that points along the object's forward, else the flatter world direction.
        var worldAxis = transform.TransformDirection(axis);
        if (Mathf.Abs(Vector3.Dot(worldAxis, transform.forward)) > 0.15f)
            return Vector3.Dot(worldAxis, transform.forward) >= 0f ? axis : -axis;
        return worldAxis.y >= 0f ? axis : -axis;
    }

    /// <summary>The thin side of the flat screen, pointing out of the weapon.</summary>
    static Vector3 ScreenNormal(Bounds screen, Bounds whole)
    {
        var s = screen.size;
        var axis = s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
        var side = Vector3.Dot(screen.center - whole.center, axis);
        return side >= 0f ? axis : -axis;
    }

    /// <summary>Up of the picture: the screen's side axis that runs least along the barrel.</summary>
    static Vector3 ScreenUp(Vector3 normal, Vector3 aim)
    {
        Vector3 best = Vector3.up;
        var bestScore = float.MaxValue;
        foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
        {
            if (Mathf.Abs(Vector3.Dot(axis, normal)) > 0.5f) continue;
            // Prefer the axis across the barrel; on a tie, the mesh's own up.
            var score = Mathf.Abs(Vector3.Dot(axis, aim)) - (axis == Vector3.up ? 0.01f : 0f);
            if (score >= bestScore) continue;
            bestScore = score;
            best = axis;
        }
        return best;
    }

    RenderTexture CreateTexture(Bounds screen, Vector3 normal, Vector3 up, bool hasScreen)
    {
        var aspect = 1f;
        if (hasScreen)
        {
            var right = Vector3.Cross(up, normal);
            var width = Mathf.Abs(Vector3.Dot(screen.size, Abs(right)));
            var height = Mathf.Abs(Vector3.Dot(screen.size, Abs(up)));
            if (width > 1e-5f && height > 1e-5f) aspect = Mathf.Clamp(width / height, 0.25f, 4f);
        }
        var rt = new RenderTexture(Mathf.RoundToInt(textureHeight * aspect), textureHeight, 24, RenderTextureFormat.ARGB32)
        {
            name = "Tracker Weapon Scope",
            antiAliasing = 1,
            dimension = TextureDimension.Tex2D,
            volumeDepth = 1,
            vrUsage = VRTextureUsage.None,
            useDynamicScale = false,
            useMipMap = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        rt.Create();
        return rt;
    }


    static void ConfigureScopeUrpCamera(UniversalAdditionalCameraData data, Camera cam)
    {
        if (cam == null) return;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.allowDynamicResolution = false;
        if (data != null)
            data.allowXRRendering = false;
        else if (GraphicsSettings.currentRenderPipeline == null)
            cam.stereoTargetEye = StereoTargetEyeMask.None;

        if (data == null) return;
        data.renderType = CameraRenderType.Base;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.On;
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        cam.forceIntoRenderTexture = true;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 0.1f);
    }

    void ApplyTrackerScreenTexture(MeshRenderer weaponRenderer)
    {
        if (texture == null || weaponRenderer == null || screenMaterialIndex >= weaponRenderer.sharedMaterials.Length)
            return;
        var mats = weaponRenderer.sharedMaterials;
        var mat = screenMaterial != null ? screenMaterial : mats[screenMaterialIndex];
        if (mat == null) return;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
        mat.mainTexture = texture;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        mats[screenMaterialIndex] = mat;
        weaponRenderer.sharedMaterials = mats;
    }

    void CreateScreen(MeshRenderer weaponRenderer, Bounds screen, Vector3 normal, Vector3 up)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Scope Screen";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, false);

        var right = Vector3.Cross(up, normal);
        var width = Mathf.Abs(Vector3.Dot(screen.size, Abs(right)));
        var height = Mathf.Abs(Vector3.Dot(screen.size, Abs(up)));
        var thickness = Mathf.Abs(Vector3.Dot(screen.extents, Abs(normal)));
        // A millimetre or two above the glass so it never flickers through it.
        var lift = 0.002f / Mathf.Max(0.0001f, transform.lossyScale.magnitude / 1.732f);

        // The quad's visible side is -Z, so -Z points out of the screen.
        quad.transform.localPosition = screen.center + normal * (thickness + lift);
        quad.transform.localRotation = Quaternion.LookRotation(-normal, up);
        quad.transform.localScale = new Vector3(width * 0.96f, height * 0.96f, 1f);

        if (scopeScreenMaterial == null)
        {
            Debug.LogError("TrackerWeaponScope: assign the scope screen material so its shader is included in VR builds.", this);
            return;
        }
        var material = new Material(scopeScreenMaterial) { name = "Scope Screen" };
        screenMaterial = material;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        material.mainTexture = texture;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

        screenRenderer = quad.GetComponent<MeshRenderer>();
        screenRenderer.sharedMaterial = material;
        screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        screenRenderer.receiveShadows = false;
    }

    void MakeGrabbable(Bounds localBounds)
    {
        grab = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();
        if (grab == null || body == null) return;
        foreach (var collider in grab.colliders)
            foreach (var own in origin.GetComponentsInChildren<Collider>(true))
                if (collider != null && own != null) Physics.IgnoreCollision(collider, own, true);
        grab.selectExited.AddListener(args =>
        {
            if (grab.isSelected) return;
            body.isKinematic = false;
            body.useGravity = true;
        });
    }

    void LateUpdate()
    {
        if (scopeCamera == null) return;
        if (texture != null && (!texture.IsCreated() || scopeCamera.targetTexture != texture))
        {
            if (!texture.IsCreated()) texture.Create();
            WakeScopeCamera();
        }
        UpdateScopeCameraEnabled();
        var held = grab != null && grab.isSelected;
        UpdateThermalContextButton(held); // B/Y works without grab unless thermalContextButtonRequiresHeld
        UpdateLock(held);

        // Dropped through the ground or thrown far away: back to where it started.
        if (body != null && !held && transform.position.y < homePosition.y - 30f)
        {
            body.isKinematic = true;
            body.useGravity = false;
            transform.SetPositionAndRotation(homePosition, homeRotation);
        }
    }

    void UpdateThermalContextButton(bool held)
    {
        if (!engagementEnabled || !thermalToggledByContextButton || !scopeThermalView) return;
        if (thermalContextButtonRequiresHeld && !held)
        {
            wasContextButton = false;
            return;
        }

        var pressed = false;
        contextInputDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, contextInputDevices);
        foreach (var device in contextInputDevices)
        {
            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out var down) && down)
            {
                pressed = true;
                break;
            }
        }

        if (pressed && !wasContextButton)
            ToggleThermalEngaged();
        wasContextButton = pressed;
    }

    void UpdateLock(bool held)
    {
        if (confirmingLock || missileAway) return;
        var visible = engagementEnabled && !targetDown && (held || !requireHeld) && IsTargetVisible();
        if (visible != targetVisible || !breathTween.isAlive)
        {
            targetVisible = visible;
            StartBreathing(visible);
            nextBeep = 0f;
        }
        var onTarget = visible && IsOnTarget();
        SetReticleState(onTarget ? ReticleState.Acquisition : ReticleState.Idle);
        lockTimer = onTarget ? lockTimer + Time.deltaTime : 0f;
        var progress = Mathf.Clamp01(lockTimer / Mathf.Max(0.1f, lockSeconds));
        SetCrosshairColor(Color.Lerp(crosshairColor, lockedColor, progress));
        if (breathTween.isAlive) breathTween.timeScale = onTarget ? Mathf.Lerp(1f, 1.6f, progress) : 1f;
        if (visible && Time.time >= nextBeep)
        {
            PlayFeedbackBeep(false, progress);
            nextBeep = Time.time + Mathf.Lerp(breathSeconds * 0.7f, 0.16f, progress);
        }
        UpdateAcquisitionShake(onTarget, progress);

        if (lockTimer < lockSeconds) return;
        lockTimer = 0f;
        confirmingLock = true;
        StartCoroutine(ConfirmLockAndLaunch());
    }

    void UpdateAcquisitionShake(bool onTarget, float progress)
    {
        if (!onTarget)
        {
            acquisitionShakesPlayed = 0;
            nextShake = 0f;
            if (shakeTween.isAlive) shakeTween.Stop();
            if (reticleMotion != null) reticleMotion.localPosition = reticleRestPosition;
            return;
        }
        if (Time.time < nextShake || acquisitionShakesPlayed >= Mathf.Max(0, acquisitionShakeCount)) return;
        acquisitionShakesPlayed++;
        ShakeCrosshair(acquisitionShake * progress, 0.14f);
        nextShake = Time.time + 0.16f;
    }

    bool IsTargetVisible()
    {
        if (scopeCamera == null || !scopeCamera.enabled || target == null ||
            !target.gameObject.activeInHierarchy || !TargetBounds(out var bounds)) return false;
        if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(scopeCamera), bounds)) return false;
        var direction = bounds.center - scopeCamera.transform.position;
        foreach (var hit in Physics.RaycastAll(scopeCamera.transform.position, direction.normalized,
            direction.magnitude, scopeCamera.cullingMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(target) || hit.collider.transform.IsChildOf(transform)) continue;
            return false;
        }
        foreach (var renderer in targetRenderers)
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                (scopeCamera.cullingMask & (1 << renderer.gameObject.layer)) != 0) return true;
        return false;
    }

    System.Collections.IEnumerator ConfirmLockAndLaunch()
    {
        if (breathTween.isAlive) breathTween.Stop();
        if (shakeTween.isAlive) shakeTween.Stop();
        if (reticleMotion != null)
        {
            reticleMotion.localPosition = reticleRestPosition;
            reticleMotion.localScale = reticleRestScale;
        }
        SetReticleState(ReticleState.FullyLocked);
        SetCrosshairColor(fullLockColor);
        int pulseCount = Mathf.Max(0, confirmationShakeCount);
        for (var pulse = 0; pulse < pulseCount; pulse++)
        {
            if (targetDown || target == null || !target.gameObject.activeInHierarchy)
            {
                ResetCrosshairFeedback();
                yield break;
            }
            PlayFeedbackBeep(true, 1f);
            float progress = pulseCount > 1 ? pulse / (pulseCount - 1f) : 1f;
            ShakeCrosshair(confirmationShake * Mathf.Lerp(0.6f, 1f, progress), confirmationShakeSeconds);
            yield return Tween.Delay(Mathf.Max(0.08f, confirmationShakeSeconds) + 0.07f).ToYieldInstruction();
        }
        yield return MissileToTarget();
        confirmingLock = false;
    }

    bool IsOnTarget()
    {
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        var from = scopeCamera.transform.position;

        // Straight down the crosshair.
        var hits = Physics.RaycastAll(from, scopeCamera.transform.forward, farClip, ~0, QueryTriggerInteraction.Collide);
        var nearestOther = float.MaxValue;
        var targetHit = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(target)) targetHit = Mathf.Min(targetHit, hit.distance);
            else if (!hit.collider.isTrigger && !hit.collider.transform.IsChildOf(transform))
                nearestOther = Mathf.Min(nearestOther, hit.distance);
        }
        if (targetHit < nearestOther) return true;

        // Or just off that line (a far Shahed is tiny), as long as nothing (ground, hills) is in the way.
        if (aimToleranceDegrees <= 0f || !TargetBounds(out var bounds)) return false;
        var toTarget = bounds.center - from;
        if (toTarget.magnitude > farClip) return false;
        if (Vector3.Angle(scopeCamera.transform.forward, toTarget) > aimToleranceDegrees) return false;

        foreach (var hit in Physics.RaycastAll(from, toTarget.normalized, toTarget.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(target) || hit.collider.transform.IsChildOf(transform)) continue;
            return false;
        }
        return true;
    }

    System.Collections.IEnumerator MissileToTarget()
    {
        missileAway = true;
        if (missileLaunchOrigin == null || target == null || !target.gameObject.activeInHierarchy)
        {
            Debug.LogWarning(name + ": cannot launch the lock-on missile because its launcher or target is missing.", this);
            missileAway = false;
            yield break;
        }

        var start = missileLaunchOrigin.TransformPoint(missileLaunchOffset);
        var aim = TargetBounds(out var targetBounds) ? targetBounds.center : target.position;
        var heading = aim - start;
        if (heading.sqrMagnitude < 0.0001f) heading = missileLaunchOrigin.forward;
        heading.Normalize();
        if (missileLaunchSound != null)
            AudioSource.PlayClipAtPoint(missileLaunchSound, start, missileLaunchVolume);

        GameObject missile;
        if (missilePrefab != null)
        {
            missile = Instantiate(missilePrefab, start, Quaternion.LookRotation(heading));
            missile.transform.localScale *= missileScale;
        }
        else
        {
            missile = BuildMissile(start, heading);
        }
        activeMissile = missile;
        var trail = LaunchSmoke.CreateTrail(0.6f);
        activeTrail = trail;
        trail.transform.SetPositionAndRotation(start, Quaternion.LookRotation(-heading));
        var position = start;
        var flightTarget = target;
        var hitTarget = false;
        missileTween = Tween.Custom(0f, 1f, Mathf.Max(0.1f, missileSeconds), progress =>
        {
            if (missile == null || target != flightTarget || flightTarget == null ||
                !flightTarget.gameObject.activeInHierarchy) return;
            aim = TargetBounds(out var bounds) ? bounds.center : flightTarget.position;
            var nextPosition = Vector3.Lerp(start, aim, progress);
            var direction = nextPosition - position;
            position = nextPosition;
            var rotation = direction.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(direction) : missile.transform.rotation;
            missile.transform.SetPositionAndRotation(position, rotation);
            if (trail != null) trail.transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, 180f, 0f));
            hitTarget = progress >= 1f;
        }, ease: Ease.InQuad);
        while (missileTween.isAlive)
        {
            if (missile == null || target != flightTarget || flightTarget == null ||
                !flightTarget.gameObject.activeInHierarchy)
            {
                missileTween.Stop();
                hitTarget = false;
                break;
            }
            yield return null;
        }
        ReleaseMissile();
        if (hitTarget && target == flightTarget) DestroyTarget(position);
        else missileAway = false;
    }

    void ReleaseMissile()
    {
        if (missileTween.isAlive) missileTween.Stop();
        if (activeTrail != null) LaunchSmoke.StopAndRelease(activeTrail);
        activeTrail = null;
        if (activeMissile != null)
        {
            activeMissile.SetActive(false);
            Destroy(activeMissile);
        }
        activeMissile = null;
    }

    void DestroyTarget(Vector3 at)
    {
        if (target == null || !target.gameObject.activeInHierarchy) { missileAway = false; return; }
        var hasBounds = TargetBounds(out var bounds);
        var size = hasBounds
            ? Mathf.Clamp(bounds.size.magnitude / 4f, 0.8f, 6f) * killExplosionScale
            : 2f * killExplosionScale;
        var center = at;

        // Blast stays in world space so it is not dragged by the drone before it disappears.
        if (hasBounds)
            ThermalExplosionHeat.Spawn(center, bounds, killExplosionScale);
        if (impactParticlePrefab != null)
        {
            var impact = Instantiate(impactParticlePrefab, center, Quaternion.identity);
            impact.transform.localScale *= killExplosionScale;
            impact.SetActive(true);
            foreach (var particles in impact.GetComponentsInChildren<ParticleSystem>(true))
                if (particles.gameObject.activeInHierarchy) particles.Play(false);
            Destroy(impact, Mathf.Max(1f, impactParticleLifetime));
        }
        else
        {
            LaunchSmoke.CreateExplosionBurst(center, size);
            LaunchSmoke.CreateFire(center, size * 0.35f, size * 0.9f, 2.5f);
        }
        if (missileImpactSound != null)
        {
            var impactAudio = new GameObject("Drone Destruction Sound").AddComponent<AudioSource>();
            impactAudio.transform.position = center;
            impactAudio.playOnAwake = false;
            impactAudio.clip = missileImpactSound;
            impactAudio.volume = missileImpactVolume;
            impactAudio.spatialBlend = 0f;
            impactAudio.dopplerLevel = 0f;
            impactAudio.Play();
            Destroy(impactAudio.gameObject, missileImpactSound.length + 0.5f);
        }

        FreezeAndHideTarget();
        targetDown = true;
        missileAway = false;
        SetReticleState(ReticleState.Idle);
        SetCrosshairColor(crosshairColor);
        TargetEliminated?.Invoke();
        StartCoroutine(ShowKillMessage());
    }

    /// <summary>Level script can point the scope at the circling drone after pickup.</summary>
    public void AssignTarget(Transform newTarget)
    {
        StopAllCoroutines();
        ReleaseMissile();
        ResetCrosshairFeedback();
        target = newTarget;
        targetDown = false;
        lockTimer = 0f;
        missileAway = false;
        targetRenderers = target != null ? target.GetComponentsInChildren<Renderer>() : null;
    }

    public void EditorTestForceTargetKill()
    {
        if (!Application.isPlaying || target == null) return;
        StopAllCoroutines();
        ReleaseMissile();
        ResetCrosshairFeedback();
        DestroyTarget(TargetBounds(out var bounds) ? bounds.center : target.position);
    }

    void FreezeAndHideTarget()
    {
        if (target == null) return;

        // Stop every mover so the wreck does not keep drifting after the hit.
        foreach (var jet in target.GetComponentsInChildren<FighterJetFlight>(true))
        {
            jet.StopFlying();
            jet.enabled = false;
        }
        foreach (var animator in target.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = false;
            animator.speed = 0f;
        }
        foreach (var rb in target.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
        foreach (var col in target.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        target.gameObject.SetActive(false);
    }

    System.Collections.IEnumerator ShowKillMessage()
    {
        if (string.IsNullOrWhiteSpace(killMessage)) yield break;

        if (origin == null || origin.Camera == null) yield break;
        var hand = rightController;
        if (hand == null) hand = origin.Camera.transform;

        var banner = HandHintBanner.Create(origin.Camera, killMessageFont, killMessage);
        banner.SetHand(hand);
        yield return new WaitForSeconds(killMessageSeconds);
        if (banner != null) banner.Hide();
    }


    static GameObject BuildMissile(Vector3 position, Vector3 heading)
    {
        var root = new GameObject("Scope Missile");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(heading));
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.15f, 0.6f, 0.15f);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
            body.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = new Color(0.8f, 0.8f, 0.78f) };
        return root;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    void ResetCrosshairFeedback()
    {
        if (breathTween.isAlive) breathTween.Stop();
        if (shakeTween.isAlive) shakeTween.Stop();
        if (reticleMotion != null)
        {
            reticleMotion.localPosition = reticleRestPosition;
            reticleMotion.localScale = reticleRestScale;
        }
        if (feedbackAudio != null) feedbackAudio.Stop();
        targetVisible = false;
        confirmingLock = false;
        lockTimer = 0f;
        nextShake = 0f;
        acquisitionShakesPlayed = 0;
        nextBeep = 0f;
        SetReticleState(ReticleState.Idle);
        SetCrosshairColor(crosshairColor);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        ReleaseMissile();
        ResetCrosshairFeedback();
        missileAway = false;
        if (scopeCamera != null) scopeCamera.enabled = false;
    }

    void OnEnable()
    {
        if (scopeReady) WakeScopeCamera();
    }

    void OnDestroy()
    {
        if (screenMaterial != null) Destroy(screenMaterial);
        if (generatedBeep != null) Destroy(generatedBeep);
        if (texture == null) return;
        texture.Release();
        Destroy(texture);
    }
}
