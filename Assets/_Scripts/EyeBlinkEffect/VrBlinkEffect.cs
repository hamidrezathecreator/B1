using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

/// <summary>XR-safe curved eyelid blink effect ported from the legacy shader implementation.</summary>
[ExecuteAlways]
public sealed class VrBlinkEffect : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private Color eyelidColor = Color.black;
    [Range(0f, 1f)] [SerializeField] private float smallBlinkAmount = 0.1f;
    [Range(0f, 1f)] [SerializeField] private float halfBlinkAmount = 0.5f;
    [Range(0f, 5f)] [SerializeField] private float curvature = 1f;
    [Range(0.001f, 0.25f)] [SerializeField] private float edgeSoftness = 0.04f;

    [Header("Timing")]
    [Tooltip("Total small blink duration in seconds: 40% closing, 60% reopening, with no hold.")]
    [Min(0f)] [SerializeField] private float smallBlinkDuration = 0.3f;
    [Min(0f)] [SerializeField] private float closeDuration = 0.12f;
    [Tooltip("Time in seconds for CloseSlowly to fully close the eyelids and keep them closed.")]
    [Min(0f)] [SerializeField] private float slowCloseDuration = 2f;
    [Min(0f)] [SerializeField] private float openDuration = 0.18f;
    [Min(0f)] [SerializeField] private float fullBlinkHold = 0.04f;

    [Header("Gameplay Events")]
    [Tooltip("React to small explosion shake pulses in this scene. Existing blinks and closed eyelids take priority.")]
    [SerializeField] private bool blinkOnSmallShake = true;

    [Header("Camera")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("Head-relative world-space distance. Kept beyond the camera near clip plane.")]
    [Min(0.01f)] [SerializeField] private float canvasDistance = 0.05f;
    [SerializeField] private int sortingOrder = 32760;

    private Canvas overlayCanvas;
    private Image overlayImage;
    private Material overlayMaterial;
    private bool animating;
    private bool blinkCycle;
    private bool pendingStart;
    private double animationStart;
    private float animationFrom;
    private float animationTarget;
    private float animationCloseDuration;
    private float animationOpenDuration;
    private float animationHold;
    public float Amount => amount;
    public bool IsAnimating => animating;
    private float amount;
    private const float OpenThreshold = 0.0001f;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int CurvatureId = Shader.PropertyToID("_Curvature");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    private void OnEnable()
    {
        ResetBlink();
        XRExplosionShake.SmallShakeStarted -= OnSmallShakeStarted;
        XRExplosionShake.SmallShakeStarted += OnSmallShakeStarted;
        Application.onBeforeRender += PositionOverlay;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

    private void Update()
    {
        if (Application.isPlaying) TickAnimation(CurrentTime);
    }

    private static double CurrentTime
    {
        get
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return Time.realtimeSinceStartupAsDouble;
        }
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || !animating) return;
        TickAnimation(CurrentTime);
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
#endif

    private void LateUpdate() => PositionOverlay();

    private void PositionOverlay()
    {
        if (overlayCanvas == null) return;
        if (targetCamera == null || !targetCamera.isActiveAndEnabled) targetCamera = FindSceneCamera();
        if (targetCamera == null) return;
        overlayCanvas.worldCamera = targetCamera;
        float distance = Mathf.Max(targetCamera.nearClipPlane + 0.03f, Mathf.Max(0.1f, canvasDistance));
        Transform head = targetCamera.transform;
        overlayCanvas.transform.SetPositionAndRotation(head.position + head.forward * distance, head.rotation);
        // Overscan both eyes; the shader computes the eyelid shape in each eye's screen coordinates.
        overlayCanvas.transform.localScale = Vector3.one * (distance * 0.005f);
    }

    private void OnSmallShakeStarted(XRExplosionShake source)
    {
        if (Application.isPlaying && blinkOnSmallShake &&
            source != null && source.gameObject.scene == gameObject.scene)
            SmallBlink();
    }

    /// <summary>Small reflex blink for gameplay events, UnityEvents, or Timeline signals. Does not interrupt existing eye animations.</summary>
    [ContextMenu("Small Blink (10% Default)")]
    public void SmallBlink()
    {
        if (animating || amount > OpenThreshold) return;
        float duration = Mathf.Max(0f, smallBlinkDuration);
        StartBlink(Mathf.Clamp01(smallBlinkAmount), duration * 0.4f, duration * 0.6f, 0f);
    }

    [ContextMenu("Half Blink")]
    public void HalfBlink() => StartBlink(Mathf.Clamp01(halfBlinkAmount));

    [ContextMenu("Full Blink")]
    public void FullBlink() => StartBlink(1f);

    /// <summary>Closes from the current amount and stays closed. Callable from a Timeline Signal Receiver.</summary>
    [ContextMenu("Close Slowly")]
    public void CloseSlowly() => StartTo(1f, slowCloseDuration);

    public void FadeIn() => StartTo(1f, closeDuration);
    public void FadeOut() => StartTo(0f, openDuration);

    [ContextMenu("Open")]
    public void Open() => StartTo(0f, openDuration);

    /// <summary>Sets the eyelid amount immediately for editor previewing.</summary>
    public void PreviewAmount(float value)
    {
        EnsureOverlay();
        StopAnimation();
        SetAmount(value);
    }

    [ContextMenu("Stop & Reset (Fully Open)")]
    public void ResetBlink()
    {
        StopAnimation();
        SetAmount(0f);
    }

    private void StartBlink(float targetAmount)
        => StartBlink(targetAmount, closeDuration, openDuration, fullBlinkHold);

    private void StartBlink(float targetAmount, float closing, float opening, float hold)
    {
        if (!isActiveAndEnabled) return;
        ResetBlink();
        EnsureOverlay();
        blinkCycle = true;
        animationFrom = 0f;
        animationTarget = Mathf.Clamp01(targetAmount);
        animationCloseDuration = Mathf.Max(0f, closing);
        animationOpenDuration = Mathf.Max(0f, opening);
        animationHold = Mathf.Max(0f, hold);
        pendingStart = true;
        animating = true;
    }

    private void StartTo(float targetAmount, float duration)
    {
        if (!isActiveAndEnabled) return;
        EnsureOverlay();
        StopAnimation();
        blinkCycle = false;
        animationFrom = amount;
        animationTarget = Mathf.Clamp01(targetAmount);
        animationCloseDuration = Mathf.Max(0f, duration);
        animationStart = CurrentTime;
        animating = true;
    }

    private void TickAnimation(double now)
    {
        if (!animating) return;
        if (pendingStart)
        {
            // Render a zero update before starting the clock, even on retrigger.
            pendingStart = false;
            animationStart = now;
            SetAmount(0f);
            return;
        }

        double elapsed = System.Math.Max(0d, now - animationStart);
        if (elapsed < animationCloseDuration)
        {
            SetAmount(Mathf.Lerp(animationFrom, animationTarget,
                Mathf.SmoothStep(0f, 1f, (float)(elapsed / animationCloseDuration))));
            return;
        }
        if (!blinkCycle)
        {
            SetAmount(animationTarget);
            StopAnimation();
            return;
        }
        elapsed -= animationCloseDuration;
        if (elapsed < animationHold)
        {
            SetAmount(animationTarget);
            return;
        }
        elapsed -= animationHold;
        if (elapsed < animationOpenDuration)
        {
            SetAmount(Mathf.Lerp(animationTarget, 0f,
                Mathf.SmoothStep(0f, 1f, (float)(elapsed / animationOpenDuration))));
            return;
        }
        ResetBlink();
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas != null) return;
        if (targetCamera == null) targetCamera = FindSceneCamera();
        if (targetCamera == null) return;

        GameObject canvasObject = new GameObject("VR Blink Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.hideFlags = HideFlags.HideAndDontSave;
        overlayCanvas = canvasObject.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.WorldSpace;
        overlayCanvas.worldCamera = targetCamera;
        canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(2000f, 2000f);
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        GameObject imageObject = new GameObject("Curved Eyelids", typeof(RectTransform), typeof(Image));
        imageObject.hideFlags = HideFlags.HideAndDontSave;
        imageObject.transform.SetParent(canvasObject.transform, false);
        overlayImage = imageObject.GetComponent<Image>();
        overlayImage.enabled = false;
        RectTransform rect = overlayImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        overlayImage.raycastTarget = false;

        Material template = Resources.Load<Material>("VRBlinkOverlay");
        if (template == null)
        {
            Debug.LogError("VrBlinkEffect could not find the VRBlinkOverlay material resource.", this);
            ReleaseOverlay();
            return;
        }
        overlayMaterial = new Material(template) { name = "VR Blink Material (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        overlayImage.material = overlayMaterial;
        PositionOverlay();
        SetAmount(amount);
    }

    private void SetAmount(float value)
    {
        amount = Mathf.Clamp01(value);
        if (overlayMaterial != null)
        {
            overlayMaterial.SetColor(ColorId, eyelidColor);
            overlayMaterial.SetFloat(ProgressId, amount);
            overlayMaterial.SetFloat(CurvatureId, curvature);
            overlayMaterial.SetFloat(SmoothnessId, edgeSoftness);
        }

        // Fully open means no overlay is drawn.
        if (overlayImage != null)
            overlayImage.enabled = overlayMaterial != null && amount > OpenThreshold;
    }

    private Camera FindSceneCamera()
    {
        XROrigin origin = FindFirstObjectByType<XROrigin>();
        if (origin != null && origin.Camera != null && origin.Camera.isActiveAndEnabled) return origin.Camera;
        return Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
    }

    private void StopAnimation()
    {
        animating = false;
        pendingStart = false;
    }

    private void OnDisable()
    {
        XRExplosionShake.SmallShakeStarted -= OnSmallShakeStarted;
        Application.onBeforeRender -= PositionOverlay;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        ResetBlink();
        ReleaseOverlay();
    }

    private void OnDestroy() => ReleaseOverlay();

    private void ReleaseOverlay()
    {
        if (overlayCanvas != null)
        {
            if (Application.isPlaying) Destroy(overlayCanvas.gameObject);
            else DestroyImmediate(overlayCanvas.gameObject);
        }
        if (overlayMaterial != null)
        {
            if (Application.isPlaying) Destroy(overlayMaterial);
            else DestroyImmediate(overlayMaterial);
        }
        overlayCanvas = null;
        overlayImage = null;
        overlayMaterial = null;
    }
}
