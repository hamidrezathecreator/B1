using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Zero idle cost. Update() is disabled when thermal is off and re-enabled
/// only for the frames it is actually needed. Grain texture is lazy-initialised
/// on first activation so it never allocates if the player never presses the button.
///
/// SAFE SHUTDOWN: Thermal is force-deactivated on OnDisable, OnDestroy,
/// OnApplicationQuit, and scene unload so global shader state is never left dirty.
/// </summary>
// [MAP] ThermalVisionController::ThermalVisionController  L17–L649
[ExecuteAlways]
public class ThermalVisionController : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Static state & events
    // ──────────────────────────────────────────────────────────────────────────

    public static bool IsEnabled  { get; private set; } = false;
    public static bool IsInverted { get; private set; } = false;

    /// <summary>
    /// Fired whenever thermal is toggled on or off.
    /// Subscribe instead of polling IsEnabled every frame.
    /// </summary>
    public static event System.Action<bool> OnThermalStateChanged;

    // ──────────────────────────────────────────────────────────────────────────
    // Inspector fields
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Post Processing Control")]
    [SerializeField] private bool   disablePostProcessOnThermal = true;
    [Tooltip("Reference the global Volume that handles your standard game look.")]
    [SerializeField] private Volume standardPostProcessVolume;

    [Header("Thermal Control")]
    [SerializeField] private bool enableThermal = false;
    [SerializeField] private bool invertMode    = false;
    [SerializeField] private bool scopeOnlyMode;

    [Header("Color Palette")]
    [SerializeField] private ThermalPalette currentPalette  = ThermalPalette.WhiteHot;
    [SerializeField] private bool           useManualColors = false;

    [SerializeField] private Color coldColor = new Color(0.05f, 0.05f, 0.05f, 1f);
    [SerializeField] private Color midColor  = new Color(0.5f,  0.5f,  0.5f,  1f);
    [SerializeField] private Color hotColor  = new Color(0.95f, 0.95f, 0.95f, 1f);

    [Header("Normal Mode Adjustments")]
    [SerializeField][Range(0f, 2f)]   private float brightness = 1.0f;
    [SerializeField][Range(0.5f, 2f)] private float contrast   = 1.0f;

    [Header("Visual Effects")]
    [SerializeField][Range(0f, 2f)] private float bloomIntensity = 0.5f;
    [SerializeField][Range(0f, 1f)] private float bloomThreshold = 0.7f;

    [Header("Film Grain / Global Noise")]
    [Tooltip("Assign a noise texture here OR leave null to auto-generate one on first activation. " +
             "This same texture is used by ThermalObjectReplacement.shader via _GrainTex global.")]
    [SerializeField] private Texture2D grainTexture;
    [SerializeField][Range(0f, 1f)]    private float grainIntensity      = 0.5f;
    [Tooltip("0 = Uniform grain everywhere. 1 = Grain ONLY in dark areas.")]
    [SerializeField][Range(0f, 1f)]    private float grainBalance        = 0.8f;
    [SerializeField][Range(0.1f, 20f)] private float grainScale          = 2.0f;
    [SerializeField][Range(0f, 20f)]   private float grainAnimationSpeed = 1.0f;

// [MAP] ThermalVisionController::GrainMode  L78–L78
    public enum GrainMode { Additive, Multiply, Overlay }
    [SerializeField] private GrainMode grainMode = GrainMode.Additive;

    [Header("Replacement Shader — Heat")]
    [Tooltip("Base heat emitted by objects that have no ThermalVision pass.")]
    [SerializeField][Range(0f, 1f)]    private float defaultObjectHeat          = 0.3f;
    [Tooltip("How much the gradient/noise can swing heat up or down from the base.")]
    [SerializeField][Range(0f, 0.4f)]  private float replacementHeatVariance    = 0.15f;

    [Header("Replacement Shader — Normal Bias")]
    [Tooltip("Extra heat added to surfaces facing the hot direction.")]
    [SerializeField][Range(0f, 1f)]    private float replacementNormalBias      = 0.35f;
    [Tooltip("0 = World Up (floors absorb heat).  1 = Toward Camera (Fresnel-style).")]
    [SerializeField][Range(0f, 1f)]    private float replacementNormalMode      = 0f;

    [Header("Replacement Shader — Vertical Gradient")]
    [Tooltip("Heat value at the bottom of the gradient range.")]
    [SerializeField][Range(0f, 1f)]    private float replacementGradientBottom  = 0.25f;
    [Tooltip("Heat value at the top of the gradient range.")]
    [SerializeField][Range(0f, 1f)]    private float replacementGradientTop     = 0.4f;
    [Tooltip("World-space height over which the gradient spans (metres).")]
    [SerializeField][Range(0.1f, 20f)] private float replacementGradientRange   = 4.0f;
    [Tooltip("World Y at the centre of the gradient.")]
    [SerializeField]                   private float replacementGradientCenter   = 0.0f;

    [Header("Replacement Shader — Noise")]
    [Tooltip("How much the noise texture varies heat. 0 = completely flat color.")]
    [SerializeField][Range(0f, 0.5f)]  private float replacementNoiseStrength   = 0.12f;
    [Tooltip("UV scale for the primary (large) noise octave.")]
    [SerializeField][Range(0.1f, 10f)] private float replacementNoiseScale      = 2.0f;
    [Tooltip("UV scale for the secondary (detail) noise octave.")]
    [SerializeField][Range(0.1f, 20f)] private float replacementNoiseScale2     = 7.0f;
    [Tooltip("Blend between primary (0) and secondary (1) noise octave.")]
    [SerializeField][Range(0f, 1f)]    private float replacementNoiseBlend      = 0.4f;

    [Header("Edge Halo Effect")]
    [SerializeField][Range(0f, 10f)] private float invertBlurRadius = 2.0f;
    [SerializeField]                 private Color haloColor        = new Color(1f, 1f, 1f, 1f);
    [SerializeField][Range(0f, 1f)]  private float haloIntensity    = 0.5f;

    [Header("Material Reference")]
    [SerializeField] private Material thermalMaterial;

    [Header("Battery Optimization")]
    [SerializeField] private bool batterySaveMode = false;
    [SerializeField] private int  batterySaveFPS  = 30;

    [Header("Decal Rendering")]
    [SerializeField] private bool      renderDecalsInThermal = true;
    [SerializeField] private LayerMask thermalVisibleLayers  = -1;

    [Header("Scope thermal layers (matches render feature split)")]
    [SerializeField] private LayerMask thermalOpaqueNativeLayers = -1;
    [Tooltip("Opaques without a ThermalVision pass (terrain, props). Everything by default.")]
    [SerializeField] private LayerMask thermalAmbientLayers = -1;

    // ──────────────────────────────────────────────────────────────────────────
    // Enums
    // ──────────────────────────────────────────────────────────────────────────

// [MAP] ThermalVisionController::ThermalPalette  L109–L113
    public enum ThermalPalette
    {
        WhiteHot, BlackHot, Ironbow, Arctic, RainbowHC,
        GreenPhosphor, AmberHot, Lava, Fusion, RedHot, Recon
    }

// [MAP] ThermalVisionController::ThermalQuality  L115–L115
    public enum ThermalQuality { Low, Medium, High, Auto }

    // ──────────────────────────────────────────────────────────────────────────
    // Private runtime state
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Performance")]
    [SerializeField] private ThermalQuality qualityLevel        = ThermalQuality.Auto;
    [SerializeField] private bool           overrideAutoQuality = false;
    private ThermalQuality activeQuality;

    public ThermalQuality ActiveQuality => activeQuality;

    private float frameTimeAccumulator = 0f;
    private float targetFrameTime      = 0f;

    private Color baseColdColor;
    private Color baseMidColor;
    private Color baseHotColor;

    private ThermalPalette lastPalette;
    private bool           lastInvertState;

    // Track whether we generated the grain texture so we can destroy it safely
    private bool grainTextureIsOwned = false;

    // ──────────────────────────────────────────────────────────────────────────
    // Cached shader property IDs — set once, zero string hashing per frame
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly int ID_Enabled        = Shader.PropertyToID("_ThermalVisionEnabled");
    private static readonly int ID_RenderScale    = Shader.PropertyToID("_ThermalRenderScale");
    private static readonly int ID_UseR8          = Shader.PropertyToID("_ThermalUseR8");
    private static readonly int ID_ColdColor      = Shader.PropertyToID("_ColdColor");
    private static readonly int ID_MidColor       = Shader.PropertyToID("_MidColor");
    private static readonly int ID_HotColor       = Shader.PropertyToID("_HotColor");
    private static readonly int ID_GColdColor     = Shader.PropertyToID("_GlobalThermalColdColor");
    private static readonly int ID_GMidColor      = Shader.PropertyToID("_GlobalThermalMidColor");
    private static readonly int ID_GHotColor      = Shader.PropertyToID("_GlobalThermalHotColor");
    private static readonly int ID_Contrast       = Shader.PropertyToID("_Contrast");
    private static readonly int ID_Brightness     = Shader.PropertyToID("_Brightness");
    private static readonly int ID_BloomInt       = Shader.PropertyToID("_BloomIntensity");
    private static readonly int ID_BloomThresh    = Shader.PropertyToID("_BloomThreshold");
    private static readonly int ID_GrainTex       = Shader.PropertyToID("_GrainTex");
    private static readonly int ID_GrainInt       = Shader.PropertyToID("_GrainIntensity");
    private static readonly int ID_GrainBal       = Shader.PropertyToID("_GrainBalance");
    private static readonly int ID_GrainScale     = Shader.PropertyToID("_GrainScale");
    private static readonly int ID_GrainSpeed     = Shader.PropertyToID("_GrainSpeed");
    private static readonly int ID_GrainMode      = Shader.PropertyToID("_GrainMode");
    private static readonly int ID_BlurRadius     = Shader.PropertyToID("_InvertBlurRadius");
    private static readonly int ID_HaloColor      = Shader.PropertyToID("_HaloColor");
    private static readonly int ID_HaloInt        = Shader.PropertyToID("_HaloIntensity");
    // Replacement shader globals (pushed as Shader.SetGlobal* — not per-material)
    private static readonly int ID_BaseHeat          = Shader.PropertyToID("_BaseHeat");
    private static readonly int ID_HeatVariance      = Shader.PropertyToID("_HeatVariance");
    private static readonly int ID_NormalHeatBias    = Shader.PropertyToID("_NormalHeatBias");
    private static readonly int ID_NormalMode        = Shader.PropertyToID("_NormalMode");
    private static readonly int ID_GradientBottom    = Shader.PropertyToID("_GradientBottom");
    private static readonly int ID_GradientTop       = Shader.PropertyToID("_GradientTop");
    private static readonly int ID_GradientRange     = Shader.PropertyToID("_GradientRange");
    private static readonly int ID_GradientCenter    = Shader.PropertyToID("_GradientCenter");
    private static readonly int ID_NoiseStrength     = Shader.PropertyToID("_NoiseStrength");
    private static readonly int ID_NoiseScale        = Shader.PropertyToID("_NoiseScale");
    private static readonly int ID_NoiseScale2       = Shader.PropertyToID("_NoiseScale2");
    private static readonly int ID_NoiseBlend        = Shader.PropertyToID("_NoiseBlend");

    // ──────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ──────────────────────────────────────────────────────────────────────────

// [MAP] ThermalVisionController::OnEnable()  L176–L196
    private void OnEnable()
    {
        DetermineQualityLevel();
        if (!useManualColors) ApplyPaletteColors(currentPalette);
        StorePaletteColors();
        lastPalette     = currentPalette;
        lastInvertState = invertMode;
        targetFrameTime = 1f / Mathf.Clamp(batterySaveFPS, 15, 60);

        WarmupShaderVariants();

        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
        Application.quitting += OnApplicationQuitting;

        // Do NOT set enabled = enableThermal here — that would immediately fire
        // OnDisable → ForceDeactivate if enableThermal is false (the default).
        // ActivateThermal/DeactivateThermal are the sole owners of `enabled`.

        if (enableThermal)
            EnableThermal(true);        // ← routes through EnableThermal so IsEnabled is also set
    }

// [MAP] ThermalVisionController::Start()  L198–L201
    private void Start()
    {
        targetFrameTime = 1f / Mathf.Clamp(batterySaveFPS, 15, 60);
    }

    /// <summary>
    /// Update() only runs when thermal is active (enabled = true).
    /// Zero MonoBehaviour overhead while thermal is off.
    /// </summary>
// [MAP] ThermalVisionController::Update()  L207–L232
    private void Update()
    {
        if (scopeOnlyMode && scopeThermalLive)
        {
            ApplyShaderProperties();
            return;
        }

        HandleInputs();

        if (batterySaveMode)
        {
            frameTimeAccumulator += Time.unscaledDeltaTime;
            if (frameTimeAccumulator < targetFrameTime) return;
            frameTimeAccumulator = 0f;
        }

        if (invertMode != lastInvertState)
        {
            IsInverted      = invertMode;
            lastInvertState = invertMode;
            UpdateThermalState();
        }

        if (!useManualColors && currentPalette != lastPalette)
        {
            SetThermalPalette(currentPalette);
            lastPalette = currentPalette;
        }

        ApplyShaderProperties();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Safe shutdown — covers every exit path
    // ──────────────────────────────────────────────────────────────────────────

// [MAP] ThermalVisionController::OnDisable()  L238–L243
    private void OnDisable()
    {
        if (scopeOnlyMode) return;
        // Covers: component disabled in Inspector, GameObject deactivated,
        // scene unload while [ExecuteAlways] is active in editor, etc.
        ForceDeactivate();
    }

// [MAP] ThermalVisionController::OnDestroy()  L245–L253
    private void OnDestroy()
    {
        if (!scopeOnlyMode)
            ForceDeactivate();
        DestroyOwnedGrainTexture();

        UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
        Application.quitting -= OnApplicationQuitting;
    }

// [MAP] ThermalVisionController::OnApplicationQuit()  L255–L259
    private void OnApplicationQuit()
    {
        // Unity fires this before OnDestroy — clear state as early as possible
        ForceDeactivate();
    }

// [MAP] ThermalVisionController::OnApplicationQuitting()  L261–L265
    private void OnApplicationQuitting()
    {
        // Application.quitting delegate — same job, catches editor stop-play too
        ForceDeactivate();
    }

// [MAP] ThermalVisionController::OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)  L267–L271
    private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
    {
        if (scene == gameObject.scene)
            ForceDeactivate();
    }

    /// <summary>
    /// Hard-resets all global thermal shader state to off.
    /// Safe to call multiple times — idempotent.
    /// </summary>
// [MAP] ThermalVisionController::ForceDeactivate()  L277–L307
    private void ForceDeactivate()
    {
        // Do NOT early-return based on IsEnabled/enableThermal — both are static and
        // survive scene reloads. After a reload IsEnabled is already false, so the
        // old guard returned immediately without clearing global shader state, leaving
        // thermal visually stuck-on at the shader level in the next scene.
        bool wasEnabled = IsEnabled || enableThermal;

        IsEnabled     = false;
        enableThermal = false;

        Shader.SetGlobalFloat(ID_Enabled, 0f);

        if (standardPostProcessVolume != null)
            standardPostProcessVolume.enabled = true;

        if (thermalMaterial != null)
        {
            thermalMaterial.DisableKeyword("QUALITY_LOW");
            thermalMaterial.DisableKeyword("QUALITY_MEDIUM");
            thermalMaterial.DisableKeyword("QUALITY_HIGH");
            thermalMaterial.DisableKeyword("_INVERT_ON");
        }
        Shader.DisableKeyword("QUALITY_LOW");
        Shader.DisableKeyword("QUALITY_MEDIUM");
        Shader.DisableKeyword("QUALITY_HIGH");

        frameTimeAccumulator = 0f;

        // Only fire the event if there was actually something to turn off,
        // to avoid spamming UI/audio with redundant false events.
        if (wasEnabled)
            OnThermalStateChanged?.Invoke(false);
    }
    // ──────────────────────────────────────────────────────────────────────────
    // Enable / Disable thermal
    // ──────────────────────────────────────────────────────────────────────────

    public void AssignPostMaterial(Material material)
    {
        if (material != null) thermalMaterial = material;
    }

    public void ScopeOnlyMode()
    {
        scopeOnlyMode = true;
        disablePostProcessOnThermal = false;
        IsEnabled = false;
        enableThermal = false;
        enabled = true;
        ApplyScopeMonitorPreset();
    }

    public int ThermalLayerMask => thermalVisibleLayers.value;
    public int ThermalOpaqueLayerMask => thermalOpaqueNativeLayers.value;
    public int ThermalAmbientLayerMask => thermalAmbientLayers.value;

    bool scopeThermalLive;

    public void SetScopeThermalLive(bool live)
    {
        scopeThermalLive = live;
        if (live) ApplyScopeMonitorPreset();
    }

    /// <summary>Scope monitor: white-hot, full-frame grain ("screen snow"), stronger object heat.</summary>
    public void ApplyScopeMonitorPreset()
    {
        currentPalette = ThermalPalette.WhiteHot;
        useManualColors = false;
        ApplyPaletteColors(currentPalette);
        brightness = 1.05f;
        contrast = 1.15f;
        bloomIntensity = 0.55f;
        bloomThreshold = 0.65f;
        grainIntensity = 0.42f;
        grainBalance = 0.08f;
        grainScale = 5.5f;
        grainAnimationSpeed = 2.8f;
        grainMode = GrainMode.Additive;
        defaultObjectHeat = 0.38f;
        replacementHeatVariance = 0.18f;
        replacementNoiseStrength = 0.16f;
        replacementGradientBottom = 0.22f;
        replacementGradientTop = 0.48f;
        replacementGradientRange = 8f;
        invertBlurRadius = 2.5f;
        haloIntensity = 0.35f;
        activeQuality = ThermalQuality.High;
    }

    /// <summary>Scope camera frame: turn on shader globals for URP thermal passes.</summary>
    public void BeginScopeRender() => ActivateThermal();

    /// <summary>Scope camera frame end: idle globals; keep this component enabled in scope mode.</summary>
    public void EndScopeRender() => DeactivateThermal();

// [MAP] ThermalVisionController::EnableThermal(bool enable)  L313–L336
    public void EnableThermal(bool enable)
    {
        if (!scopeOnlyMode)
            enableThermal = enable;
        IsEnabled = enable;

        if (enable)
            ActivateThermal();
        else
            DeactivateThermal();

        HandlePostProcessing(enable);

        OnThermalStateChanged?.Invoke(IsEnabled);
    }

    /// <summary>
    /// Called once when thermal turns ON.
    /// Lazy-inits the grain texture and re-enables Update().
    /// </summary>
// [MAP] ThermalVisionController::ActivateThermal()  L342–L354
    private void ActivateThermal()
    {
        if (grainTexture == null)
        {
            grainTexture        = GenerateGrainTexture(512, 512);
            grainTextureIsOwned = true;
        }

        IsEnabled = !scopeOnlyMode;
        Shader.SetGlobalFloat(ID_Enabled, scopeOnlyMode ? 0f : 1f);
        ApplyShaderProperties();

        enabled = true;
    }

    /// <summary>
    /// Called once when thermal turns OFF.
    /// Disables Update() — zero MonoBehaviour overhead until next activation.
    /// </summary>
// [MAP] ThermalVisionController::DeactivateThermal()  L360–L365
    private void DeactivateThermal()
    {
        IsEnabled = false;
        Shader.SetGlobalFloat(ID_Enabled, 0f);
        if (!scopeOnlyMode)
            enabled = false;
        frameTimeAccumulator = 0f;
    }

// [MAP] ThermalVisionController::ToggleThermal()  L367–L367
    public void ToggleThermal()              => EnableThermal(!enableThermal);
// [MAP] ThermalVisionController::SetInvertMode(bool invert)  L368–L368
    public void SetInvertMode(bool invert)   { invertMode = invert; IsInverted = invert; UpdateThermalState(); }
// [MAP] ThermalVisionController::ToggleInvert()  L369–L369
    public void ToggleInvert()               => SetInvertMode(!invertMode);

    // ──────────────────────────────────────────────────────────────────────────
    // Inputs — only polled while thermal is active
    // ──────────────────────────────────────────────────────────────────────────

// [MAP] ThermalVisionController::HandleInputs()  L375–L384
    private void HandleInputs()
    {
        if (scopeOnlyMode || !Application.isPlaying) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.tKey.wasPressedThisFrame) ToggleThermal();
        if (kb.iKey.wasPressedThisFrame) ToggleInvert();
        if (kb.pKey.wasPressedThisFrame) CyclePalette();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Shader property application (runs only while thermal is on)
    // ──────────────────────────────────────────────────────────────────────────

// [MAP] ThermalVisionController::ApplyShaderProperties()  L390–L461
    public void ApplyShaderProperties()
    {
        if (thermalMaterial == null)
        {
            Debug.LogWarning("[Thermal] Material is null!");
            return;
        }

        if (grainTexture == null)
        {
            grainTexture = GenerateGrainTexture(512, 512);
            grainTextureIsOwned = true;
        }

        GetQualitySettings(out float renderScale, out bool useR8, out bool enableBloom);

        // Quality keywords
        Shader.DisableKeyword("QUALITY_LOW");
        Shader.DisableKeyword("QUALITY_MEDIUM");
        Shader.DisableKeyword("QUALITY_HIGH");
        thermalMaterial.DisableKeyword("QUALITY_LOW");
        thermalMaterial.DisableKeyword("QUALITY_MEDIUM");
        thermalMaterial.DisableKeyword("QUALITY_HIGH");

        string kw = activeQuality switch
        {
            ThermalQuality.Low    => "QUALITY_LOW",
            ThermalQuality.Medium => "QUALITY_MEDIUM",
            _                     => "QUALITY_HIGH"
        };
        Shader.EnableKeyword(kw);
        thermalMaterial.EnableKeyword(kw);

        // Global shader properties
        Shader.SetGlobalFloat(ID_RenderScale, renderScale);
        Shader.SetGlobalInt(ID_UseR8, useR8 ? 1 : 0);
        Shader.SetGlobalColor(ID_GColdColor, coldColor);
        Shader.SetGlobalColor(ID_GMidColor,  midColor);
        Shader.SetGlobalColor(ID_GHotColor,  hotColor);

        // Material properties
        thermalMaterial.SetColor(ID_ColdColor,   coldColor);
        thermalMaterial.SetColor(ID_MidColor,    midColor);
        thermalMaterial.SetColor(ID_HotColor,    hotColor);
        thermalMaterial.SetFloat(ID_Contrast,    contrast);
        thermalMaterial.SetFloat(ID_Brightness,  brightness);
        thermalMaterial.SetFloat(ID_BloomInt,    enableBloom ? bloomIntensity : 0f);
        thermalMaterial.SetFloat(ID_BloomThresh, bloomThreshold);

        // Grain texture — set on both material and global so replacement shader sees it
        if (grainTexture != null)
        {
            thermalMaterial.SetTexture(ID_GrainTex, grainTexture);
            Shader.SetGlobalTexture(ID_GrainTex, grainTexture);
        }

        thermalMaterial.SetFloat(ID_GrainInt,   grainIntensity);
        thermalMaterial.SetFloat(ID_GrainBal,   grainBalance);
        thermalMaterial.SetFloat(ID_GrainScale, grainScale);
        thermalMaterial.SetFloat(ID_GrainSpeed, grainAnimationSpeed);
        thermalMaterial.SetInt(ID_GrainMode,    (int)grainMode);

        // Also set grain scale/speed globally so replacement shader animates identically
        Shader.SetGlobalFloat(ID_GrainScale, grainScale);
        Shader.SetGlobalFloat(ID_GrainSpeed, grainAnimationSpeed);

        thermalMaterial.SetFloat(ID_BlurRadius, invertBlurRadius);
        thermalMaterial.SetColor(ID_HaloColor,  haloColor);
        thermalMaterial.SetFloat(ID_HaloInt,    haloIntensity);

        if (invertMode) thermalMaterial.EnableKeyword("_INVERT_ON");
        else            thermalMaterial.DisableKeyword("_INVERT_ON");

        // ── Replacement shader globals ───────────────────────────────────────────
        // Must be Shader.SetGlobal*: the shader declares these as plain globals,
        // NOT inside CBUFFER_START(UnityPerMaterial). material.SetFloat would
        // go into the per-material CBuffer which SetGlobal* never touches —
        // that's the original cause of the flat-color bug.
        Shader.SetGlobalFloat(ID_BaseHeat,       defaultObjectHeat);
        Shader.SetGlobalFloat(ID_HeatVariance,   replacementHeatVariance);
        Shader.SetGlobalFloat(ID_NormalHeatBias, replacementNormalBias);
        Shader.SetGlobalFloat(ID_NormalMode,     replacementNormalMode);
        Shader.SetGlobalFloat(ID_GradientBottom, replacementGradientBottom);
        Shader.SetGlobalFloat(ID_GradientTop,    replacementGradientTop);
        Shader.SetGlobalFloat(ID_GradientRange,  replacementGradientRange);
        Shader.SetGlobalFloat(ID_GradientCenter, replacementGradientCenter);
        Shader.SetGlobalFloat(ID_NoiseStrength,  replacementNoiseStrength);
        Shader.SetGlobalFloat(ID_NoiseScale,     replacementNoiseScale);
        Shader.SetGlobalFloat(ID_NoiseScale2,    replacementNoiseScale2);
        Shader.SetGlobalFloat(ID_NoiseBlend,     replacementNoiseBlend);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Palette & quality helpers
    // ──────────────────────────────────────────────────────────────────────────

// [MAP] ThermalVisionController::CyclePalette()  L467–L471
    private void CyclePalette()
    {
        int next = ((int)currentPalette + 1) % System.Enum.GetValues(typeof(ThermalPalette)).Length;
        SetThermalPalette((ThermalPalette)next);
    }

// [MAP] ThermalVisionController::SetBatterySaveMode(bool enable)  L473–L477
    public void SetBatterySaveMode(bool enable)
    {
        batterySaveMode = enable;
        Debug.Log($"[Thermal] Battery save mode: {(enable ? "ON" : "OFF")} ({batterySaveFPS} fps)");
    }

// [MAP] ThermalVisionController::StorePaletteColors()  L479–L484
    private void StorePaletteColors()
    {
        baseColdColor = coldColor;
        baseMidColor  = midColor;
        baseHotColor  = hotColor;
    }

// [MAP] ThermalVisionController::UpdateThermalState()  L486–L486
    private void UpdateThermalState() => ApplyShaderProperties();

// [MAP] ThermalVisionController::HandlePostProcessing(bool thermalActive)  L494–L498
    private void HandlePostProcessing(bool thermalActive)
    {
        if (standardPostProcessVolume == null) return;
        standardPostProcessVolume.enabled = disablePostProcessOnThermal ? !thermalActive : true;
    }

// [MAP] ThermalVisionController::SetThermalPalette(ThermalPalette palette)  L500–L507
    public void SetThermalPalette(ThermalPalette palette)
    {
        currentPalette  = palette;
        useManualColors = false;
        ApplyPaletteColors(palette);
        StorePaletteColors();
        UpdateThermalState();
    }

// [MAP] ThermalVisionController::SetQualityLevel(ThermalQuality quality)  L509–L516
    public void SetQualityLevel(ThermalQuality quality)
    {
        if (quality == ThermalQuality.Auto) DetermineQualityLevel();
        else activeQuality = quality;
        qualityLevel = quality;
        if (IsEnabled) ApplyShaderProperties();
        Debug.Log($"[Thermal] Quality set to: {activeQuality}");
    }

// [MAP] ThermalVisionController::DetermineQualityLevel()  L518–L552
    private void DetermineQualityLevel()
    {
        if (qualityLevel != ThermalQuality.Auto || overrideAutoQuality)
        {
            activeQuality = qualityLevel;
            return;
        }

        int    vram  = SystemInfo.graphicsMemorySize;
        string gpu   = SystemInfo.graphicsDeviceName.ToLower();
        int    cores = SystemInfo.processorCount;

        if (vram >= 4096 ||
            gpu.Contains("apple a1") || gpu.Contains("apple m")  ||
            gpu.Contains("adreno 7") || gpu.Contains("adreno 8") ||
            gpu.Contains("adreno 660") || gpu.Contains("adreno 650") ||
            gpu.Contains("mali-g7")  || gpu.Contains("mali-g610") ||
            (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal && vram >= 3072))
        {
            activeQuality = ThermalQuality.High;
        }
        else if (vram < 2048 || cores <= 4 ||
                 gpu.Contains("adreno 5")  || gpu.Contains("adreno 610") ||
                 gpu.Contains("mali-g5")   || gpu.Contains("mali-g31")   ||
                 gpu.Contains("powervr")   || gpu.Contains("img"))
        {
            activeQuality = ThermalQuality.Low;
        }
        else
        {
            activeQuality = ThermalQuality.Medium;
        }

        Debug.Log($"[Thermal] Auto-detected quality: {activeQuality} (VRAM: {vram}MB, GPU: {gpu}, Cores: {cores})");
    }

// [MAP] ThermalVisionController::GetQualitySettings(out float renderScale, out bool useR8, out bool enableBloom)  L554–L565
    private void GetQualitySettings(out float renderScale, out bool useR8, out bool enableBloom)
    {
        switch (activeQuality)
        {
            case ThermalQuality.Low:
                renderScale = 0.6f; useR8 = false; enableBloom = false; break;
            case ThermalQuality.Medium:
                renderScale = 0.8f; useR8 = false; enableBloom = true;  break;
            default: // High
                renderScale = 1.0f; useR8 = false; enableBloom = true;  break;
        }
    }

// [MAP] ThermalVisionController::ApplyPaletteColors(ThermalPalette palette)  L567–L583
    private void ApplyPaletteColors(ThermalPalette palette)
    {
        switch (palette)
        {
            case ThermalPalette.WhiteHot:      coldColor = new Color(0.0f,  0.0f,  0.0f,  1f); midColor = new Color(0.35f, 0.35f, 0.35f, 1f); hotColor = new Color(1.0f,  1.0f,  1.0f,  1f); break;
            case ThermalPalette.BlackHot:      coldColor = new Color(0.9f,  0.9f,  0.9f,  1f); midColor = new Color(0.45f, 0.45f, 0.45f, 1f); hotColor = new Color(0.0f,  0.0f,  0.0f,  1f); break;
            case ThermalPalette.Ironbow:       coldColor = new Color(0.0f,  0.0f,  0.15f, 1f); midColor = new Color(0.6f,  0.2f,  0.5f,  1f); hotColor = new Color(1.0f,  0.95f, 0.7f,  1f); break;
            case ThermalPalette.Arctic:        coldColor = new Color(0.0f,  0.05f, 0.2f,  1f); midColor = new Color(0.2f,  0.4f,  0.6f,  1f); hotColor = new Color(1.0f,  0.85f, 0.3f,  1f); break;
            case ThermalPalette.RainbowHC:     coldColor = new Color(0.0f,  0.0f,  0.0f,  1f); midColor = new Color(0.0f,  0.8f,  0.0f,  1f); hotColor = new Color(1.0f,  0.0f,  0.0f,  1f); break;
            case ThermalPalette.GreenPhosphor: coldColor = new Color(0.0f,  0.03f, 0.0f,  1f); midColor = new Color(0.0f,  0.3f,  0.05f, 1f); hotColor = new Color(0.3f,  1.0f,  0.3f,  1f); break;
            case ThermalPalette.AmberHot:      coldColor = new Color(0.02f, 0.01f, 0.0f,  1f); midColor = new Color(0.5f,  0.25f, 0.0f,  1f); hotColor = new Color(1.0f,  0.75f, 0.1f,  1f); break;
            case ThermalPalette.Lava:          coldColor = new Color(0.0f,  0.0f,  0.1f,  1f); midColor = new Color(0.7f,  0.0f,  0.0f,  1f); hotColor = new Color(1.0f,  1.0f,  0.4f,  1f); break;
            case ThermalPalette.Fusion:        coldColor = new Color(0.0f,  0.12f, 0.02f, 1f); midColor = new Color(0.05f, 0.35f, 0.08f, 1f); hotColor = new Color(1.0f,  0.95f, 0.6f,  1f); break;
            case ThermalPalette.RedHot:        coldColor = new Color(0.03f, 0.03f, 0.03f, 1f); midColor = new Color(0.25f, 0.25f, 0.25f, 1f); hotColor = new Color(1.0f,  0.0f,  0.0f,  1f); break;
            case ThermalPalette.Recon:         coldColor = new Color(0.08f, 0.08f, 0.12f, 1f); midColor = new Color(0.45f, 0.45f, 0.5f,  1f); hotColor = new Color(0.85f, 0.85f, 0.95f, 1f); break;
        }
    }

// [MAP] ThermalVisionController::OnValidate()  L585–L599
    private void OnValidate()
    {
        contrast            = Mathf.Clamp(contrast,            0.5f,  2.0f);
        brightness          = Mathf.Clamp(brightness,          0f,    2f);
        batterySaveFPS      = Mathf.Clamp(batterySaveFPS,      15,    60);
        grainIntensity      = Mathf.Clamp(grainIntensity,      0f,    1f);
        grainBalance        = Mathf.Clamp(grainBalance,        0f,    1f);
        grainScale          = Mathf.Clamp(grainScale,          0.1f,  20f);
        grainAnimationSpeed = Mathf.Clamp(grainAnimationSpeed, 0f,    20f);
        invertBlurRadius    = Mathf.Clamp(invertBlurRadius,    0f,    10f);
        haloIntensity       = Mathf.Clamp(haloIntensity,       0f,    1f);
        defaultObjectHeat         = Mathf.Clamp(defaultObjectHeat,          0f,   1f);
        replacementHeatVariance   = Mathf.Clamp(replacementHeatVariance,    0f,   0.4f);
        replacementNormalBias     = Mathf.Clamp(replacementNormalBias,      0f,   1f);
        replacementNormalMode     = Mathf.Clamp(replacementNormalMode,      0f,   1f);
        replacementGradientBottom = Mathf.Clamp(replacementGradientBottom,  0f,   1f);
        replacementGradientTop    = Mathf.Clamp(replacementGradientTop,     0f,   1f);
        replacementGradientRange  = Mathf.Clamp(replacementGradientRange,   0.1f, 20f);
        replacementNoiseStrength  = Mathf.Clamp(replacementNoiseStrength,   0f,   0.5f);
        replacementNoiseScale     = Mathf.Clamp(replacementNoiseScale,      0.1f, 10f);
        replacementNoiseScale2    = Mathf.Clamp(replacementNoiseScale2,     0.1f, 20f);
        replacementNoiseBlend     = Mathf.Clamp(replacementNoiseBlend,      0f,   1f);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Grain texture
    // ──────────────────────────────────────────────────────────────────────────

// [MAP] ThermalVisionController::Texture2D(int width, int height)  L605–L621
    private Texture2D GenerateGrainTexture(int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.R8, false);
        tex.wrapMode   = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            float n = Random.value;
            pixels[i] = new Color(n, n, n, 1f);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        Debug.Log("[Thermal] Grain texture generated on first activation");
        return tex;
    }

// [MAP] ThermalVisionController::DestroyOwnedGrainTexture()  L623–L635
    private void DestroyOwnedGrainTexture()
    {
        if (grainTextureIsOwned && grainTexture != null)
        {
            if (Application.isPlaying)
                Destroy(grainTexture);
            else
                DestroyImmediate(grainTexture);

            grainTexture        = null;
            grainTextureIsOwned = false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Shader warmup
    // ──────────────────────────────────────────────────────────────────────────

// [MAP] ThermalVisionController::WarmupShaderVariants()  L641–L648
    private void WarmupShaderVariants()
    {
        if (thermalMaterial == null) return;
        thermalMaterial.EnableKeyword("QUALITY_LOW");    thermalMaterial.DisableKeyword("QUALITY_LOW");
        thermalMaterial.EnableKeyword("QUALITY_MEDIUM"); thermalMaterial.DisableKeyword("QUALITY_MEDIUM");
        thermalMaterial.EnableKeyword("QUALITY_HIGH");   thermalMaterial.DisableKeyword("QUALITY_HIGH");
        Debug.Log("[Thermal] Shader variants warmed up");
    }
}
