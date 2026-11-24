using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Put this on the Camera that renders into your monitor's RenderTexture.
/// That camera (and only that camera) will output the thermal image.
///
/// Requires <see cref="ThermalMonitorRenderFeature"/> on the Renderer used by this camera.
///
/// Replaces ThermalVisionController for the monitor use case:
///   - no static/global on-off state, no input handling, no audio / crosshair / volume hooks
///   - settings live on this component, so several monitors can look different
///   - optional auto-created RenderTexture that can be pushed straight into a RawImage
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class ThermalMonitorCamera : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Registry (the render feature asks "is this camera a thermal monitor?")
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<Camera, ThermalMonitorCamera> s_Registry =
        new Dictionary<Camera, ThermalMonitorCamera>();

    public static bool TryGet(Camera cam, out ThermalMonitorCamera monitor)
        => s_Registry.TryGetValue(cam, out monitor);

    // ──────────────────────────────────────────────────────────────────────────
    // Enums
    // ──────────────────────────────────────────────────────────────────────────

    public enum ThermalPalette
    {
        WhiteHot, BlackHot, Ironbow, Arctic, RainbowHC,
        GreenPhosphor, AmberHot, Lava, Fusion, RedHot, Recon
    }

    public enum GrainMode { Additive, Multiply, Overlay }

    // ──────────────────────────────────────────────────────────────────────────
    // Inspector
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Output (Camera -> RenderTexture -> RawImage)")]
    [Tooltip("Optional. Assign your own RenderTexture, or leave empty and tick Auto Create below.")]
    [SerializeField] private RenderTexture outputTexture;
    [Tooltip("If no Output Texture is assigned, create one at start.")]
    [SerializeField] private bool autoCreateTexture = true;
    [SerializeField] private Vector2Int autoTextureSize = new Vector2Int(512, 384);
    [Tooltip("The RawImage on your monitor canvas. Its texture is set to the output texture, " +
             "and it is used for the visibility check below.")]
    [SerializeField] private RawImage targetRawImage;

    [Header("Power")]
    [SerializeField] private bool poweredOn = true;

    [Header("Weapon scope (optional)")]
    [Tooltip("Keeps the camera enabled when powered off so the scope shows a normal (non-thermal) feed.")]
    [SerializeField] private bool scopeMode;

    [Header("Quest / Performance")]
    [Tooltip("Only render while the Target Raw Image is on screen. For a World Space canvas this is a frustum " +
             "test against the Viewer Camera (ignores occlusion). Overlay / Camera canvases always count as visible. " +
             "Needs Target Raw Image to be assigned.")]
    [SerializeField] private bool renderOnlyWhenVisible = true;
    [Tooltip("The camera the player looks through (your XR camera). Empty = Camera.main.")]
    [SerializeField] private Camera viewerCamera;
    [Tooltip("Needed only if you use thermal particles or decals (they read scene depth). " +
             "Turn off to skip the depth copy on the monitor camera.")]
    [SerializeField] private bool useSceneDepth = true;

    [Header("Camera Setup")]
    [Tooltip("Turns off HDR, MSAA, post-processing and anti-aliasing on this camera. " +
             "Strongly recommended – the thermal blit needs a plain colour target.")]
    [SerializeField] private bool autoConfigureCamera = true;

    [Header("Shaders (auto-filled by Reset in the editor)")]
    [SerializeField] private Shader postProcessShader;      // Hidden/ThermalMonitor/PostProcess
    [SerializeField] private Shader replacementShader;      // Hidden/ThermalObjectReplacement
    [SerializeField] private Shader skyHeatShader;          // Hidden/ThermalMonitor/SkyHeat

    [Header("Layers")]
    [Tooltip("Opaque objects drawn with their own ThermalVision shader pass.")]
    [SerializeField] private LayerMask nativeOpaqueLayers = -1;
    [Tooltip("Objects WITHOUT a ThermalVision pass, drawn with the flat replacement shader. " +
             "Nothing = disabled. Do not overlap with the native layers.")]
    [SerializeField] private LayerMask ambientLayers = 0;
    [Tooltip("Transparent objects / particles / decals with a ThermalVision shader pass.")]
    [SerializeField] private LayerMask nativeTransparentLayers = -1;

    [Header("Color Palette")]
    [SerializeField] private ThermalPalette palette = ThermalPalette.WhiteHot;
    [SerializeField] private bool invert = false;
    [SerializeField] private bool useManualColors = false;
    [SerializeField] private Color coldColor = new Color(0f,    0f,    0f,    1f);
    [SerializeField] private Color midColor  = new Color(0.35f, 0.35f, 0.35f, 1f);
    [SerializeField] private Color hotColor  = new Color(1f,    1f,    1f,    1f);

    [Header("Adjustments")]
    [SerializeField, Range(0f, 2f)]   private float brightness = 1f;
    [SerializeField, Range(0.5f, 2f)] private float contrast   = 1f;

    [Header("Hot blur (4-tap)")]
    [Tooltip("Strength of the 4-neighbor blur on above-threshold (hot) thermal only.")]
    [SerializeField, Range(0f, 2f)] private float bloomIntensity = 0.5f;
    [Tooltip("Processed thermal value must exceed this before a tap contributes to the blur.")]
    [SerializeField, Range(0f, 1f)] private float bloomThreshold = 0.7f;
    [Tooltip("Offset in thermal-data texels for each of the four taps.")]
    [SerializeField, Range(0.5f, 12f)] private float hotBlurRadius = 2f;

    [Header("Film Grain")]
    [SerializeField, Range(0f, 1f)]    private float grainIntensity      = 0.5f;
    [Tooltip("0 = uniform grain. 1 = grain only in dark areas.")]
    [SerializeField, Range(0f, 1f)]    private float grainBalance        = 0.8f;
    [SerializeField, Range(0.1f, 20f)] private float grainScale          = 2f;
    [Tooltip("How fast the grain pattern changes (flicker rate). 0 = frozen.")]
    [SerializeField, Range(0f, 20f)]   private float grainAnimationSpeed = 1f;
    [SerializeField] private GrainMode grainMode = GrainMode.Additive;

    [Header("Sky (thermal data has no sky geometry)")]
    [Tooltip("Maps skybox luminance into thermal heat when depth marks a sky pixel.")]
    [SerializeField, Range(0.1f, 2f)] private float skyHeatMultiplier = 0.85f;

    [Header("Edge Halo")]
    [SerializeField, Range(0f, 10f)] private float haloRadius    = 2f;
    [SerializeField] private Color haloColor = Color.white;
    [SerializeField, Range(0f, 1f)]  private float haloIntensity = 0.5f;

    [Header("Replacement Shader - Heat")]
    [SerializeField, Range(0f, 1f)]   private float defaultObjectHeat       = 0.3f;
    [SerializeField, Range(0f, 0.4f)] private float replacementHeatVariance = 0.15f;

    [Header("Replacement Shader - Normal Bias")]
    [SerializeField, Range(0f, 1f)] private float replacementNormalBias = 0.35f;
    [Tooltip("0 = World Up (floors absorb heat). 1 = Toward Camera (Fresnel-style).")]
    [SerializeField, Range(0f, 1f)] private float replacementNormalMode = 0f;

    [Header("Replacement Shader - Vertical Gradient")]
    [SerializeField, Range(0f, 1f)]    private float replacementGradientBottom = 0.25f;
    [SerializeField, Range(0f, 1f)]    private float replacementGradientTop    = 0.4f;
    [SerializeField, Range(0.1f, 20f)] private float replacementGradientRange  = 4f;
    [SerializeField]                   private float replacementGradientCenter = 0f;

    [Header("Replacement Shader - Noise")]
    [SerializeField, Range(0f, 0.5f)]  private float replacementNoiseStrength = 0.12f;
    [SerializeField, Range(0.1f, 10f)] private float replacementNoiseScale    = 2f;
    [SerializeField, Range(0.1f, 20f)] private float replacementNoiseScale2   = 7f;
    [SerializeField, Range(0f, 1f)]    private float replacementNoiseBlend    = 0.4f;

    // ──────────────────────────────────────────────────────────────────────────
    // Public read-only surface used by the render feature
    // ──────────────────────────────────────────────────────────────────────────

    public Material PostMaterial      { get; private set; }
    public Material SkyHeatMaterial   { get; private set; }
    public Shader   ReplacementShader => replacementShader;
    public bool     IncludeSkyInThermalData =>
        _cam != null && _cam.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null;
    public int NativeOpaqueLayers      => nativeOpaqueLayers.value;
    public int AmbientLayers           => ambientLayers.value;
    public int NativeTransparentLayers => nativeTransparentLayers.value;
    public RenderTexture OutputTexture => outputTexture;
    public bool IsPoweredOn => poweredOn;
    public bool UseSceneDepth => useSceneDepth;

    public bool IsRenderReady => poweredOn && PostMaterial != null;

    // ──────────────────────────────────────────────────────────────────────────
    // Private
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly int ID_ColdColor  = Shader.PropertyToID("_ColdColor");
    private static readonly int ID_MidColor   = Shader.PropertyToID("_MidColor");
    private static readonly int ID_HotColor   = Shader.PropertyToID("_HotColor");
    private static readonly int ID_Contrast   = Shader.PropertyToID("_Contrast");
    private static readonly int ID_Brightness = Shader.PropertyToID("_Brightness");
    private static readonly int ID_BloomInt   = Shader.PropertyToID("_BloomIntensity");
    private static readonly int ID_BloomThr   = Shader.PropertyToID("_BloomThreshold");
    private static readonly int ID_HotBlurRad = Shader.PropertyToID("_HotBlurRadius");
    private static readonly int ID_GrainInt   = Shader.PropertyToID("_GrainIntensity");
    private static readonly int ID_GrainBal   = Shader.PropertyToID("_GrainBalance");
    private static readonly int ID_GrainScale   = Shader.PropertyToID("_GrainScale");
    private static readonly int ID_GrainAnimSpeed = Shader.PropertyToID("_GrainAnimSpeed");
    private static readonly int ID_ThermalGrainTime = Shader.PropertyToID("_ThermalGrainTime");
    private static readonly int ID_GrainMode   = Shader.PropertyToID("_GrainMode");
    private static readonly int ID_TM_SkyCube     = Shader.PropertyToID("_TM_SkyboxCube");
    private static readonly int ID_TM_Sky2D       = Shader.PropertyToID("_TM_Skybox2D");
    private static readonly int ID_TM_SkyPanoramic = Shader.PropertyToID("_TM_SkyPanoramic");
    private static readonly int ID_TM_SkyHeatMul  = Shader.PropertyToID("_TM_SkyHeatMul");
    private static readonly int ID_TM_SkyHeatBias = Shader.PropertyToID("_TM_SkyHeatBias");
    private static readonly int ID_TM_SkyRotation = Shader.PropertyToID("_TM_SkyRotation");
    private static readonly int ID_TM_InvViewProj = Shader.PropertyToID("_TM_InvViewProj");
    private static readonly int ID_HaloRadius = Shader.PropertyToID("_InvertBlurRadius");
    private static readonly int ID_HaloColor  = Shader.PropertyToID("_HaloColor");
    private static readonly int ID_HaloInt    = Shader.PropertyToID("_HaloIntensity");
    // Replacement shader globals (declared outside the CBUFFER in ThermalObjectReplacement.shader)
    private static readonly int ID_BaseHeat       = Shader.PropertyToID("_BaseHeat");
    private static readonly int ID_HeatVariance   = Shader.PropertyToID("_HeatVariance");
    private static readonly int ID_NormalHeatBias = Shader.PropertyToID("_NormalHeatBias");
    private static readonly int ID_NormalMode     = Shader.PropertyToID("_NormalMode");
    private static readonly int ID_GradBottom     = Shader.PropertyToID("_GradientBottom");
    private static readonly int ID_GradTop        = Shader.PropertyToID("_GradientTop");
    private static readonly int ID_GradRange      = Shader.PropertyToID("_GradientRange");
    private static readonly int ID_GradCenter     = Shader.PropertyToID("_GradientCenter");
    private static readonly int ID_NoiseStrength  = Shader.PropertyToID("_NoiseStrength");
    private static readonly int ID_NoiseScale     = Shader.PropertyToID("_NoiseScale");
    private static readonly int ID_NoiseScale2    = Shader.PropertyToID("_NoiseScale2");
    private static readonly int ID_NoiseBlend     = Shader.PropertyToID("_NoiseBlend");

    private Camera _cam;
    private bool   _dirty = true;

    private bool _ownsOutputTexture;

    // ──────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void Reset()
    {
        postProcessShader = Shader.Find("Hidden/ThermalMonitor/PostProcess");
        replacementShader = Shader.Find("Hidden/ThermalObjectReplacement");
        skyHeatShader     = Shader.Find("Hidden/ThermalMonitor/SkyHeat");
    }
#endif

    private void OnEnable()
    {
        _cam = GetComponent<Camera>();

        if (postProcessShader == null) postProcessShader = Shader.Find("Hidden/ThermalMonitor/PostProcess");
        if (replacementShader == null) replacementShader = Shader.Find("Hidden/ThermalObjectReplacement");
        if (skyHeatShader == null)     skyHeatShader     = Shader.Find("Hidden/ThermalMonitor/SkyHeat");

        if (postProcessShader == null)
        {
            Debug.LogError("[ThermalMonitor] Post-process shader missing. Assign 'Hidden/ThermalMonitor/PostProcess'.", this);
            return;
        }

        PostMaterial = new Material(postProcessShader) { hideFlags = HideFlags.HideAndDontSave };

        if (skyHeatShader != null)
            SkyHeatMaterial = new Material(skyHeatShader) { hideFlags = HideFlags.HideAndDontSave };

        EnsureOutputTexture();
        if (autoConfigureCamera) ConfigureCamera();

        s_Registry[_cam] = this;
        _dirty           = true;

        ApplyPower();
    }

    private void OnDisable()
    {
        if (_cam != null)
        {
            s_Registry.Remove(_cam);
            _cam.enabled = false;   // a disabled monitor must not render a normal (non-thermal) image
        }

        if (PostMaterial != null)
        {
            if (Application.isPlaying) Destroy(PostMaterial); else DestroyImmediate(PostMaterial);
            PostMaterial = null;
        }

        if (SkyHeatMaterial != null)
        {
            if (Application.isPlaying) Destroy(SkyHeatMaterial); else DestroyImmediate(SkyHeatMaterial);
            SkyHeatMaterial = null;
        }
    }

    private void OnDestroy()
    {
        if (_ownsOutputTexture && outputTexture != null)
        {
            if (_cam != null && _cam.targetTexture == outputTexture) _cam.targetTexture = null;
            outputTexture.Release();
            if (Application.isPlaying) Destroy(outputTexture); else DestroyImmediate(outputTexture);
            outputTexture = null;
        }
    }

    private void OnValidate()
    {
        autoTextureSize.x = Mathf.Clamp(autoTextureSize.x, 16, 4096);
        autoTextureSize.y = Mathf.Clamp(autoTextureSize.y, 16, 4096);

        if (!useManualColors)
            ApplyPaletteColors(palette);

        _dirty = true;
        PushInspectorChanges();
    }

    /// <summary>Inspector tweaks during Play Mode apply on the next frame (or immediately from OnValidate).</summary>
    void PushInspectorChanges()
    {
        if (!isActiveAndEnabled || PostMaterial == null) return;
        ApplyMaterialProperties();
        ApplyPower();
        _dirty = false;
    }

    private void Update()
    {
        if (PostMaterial == null) return;

        if (_dirty)
            PushInspectorChanges();

        if (!poweredOn) return;

        // Only render while the monitor's RawImage is actually in view.
        bool shouldRender = IsScreenVisible();
        if (_cam.enabled != shouldRender) _cam.enabled = shouldRender;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    public void SetPower(bool on)
    {
        poweredOn = on;
        ApplyPower();
    }

    public void TogglePower() => SetPower(!poweredOn);

    public void SetPalette(ThermalPalette p)
    {
        palette         = p;
        useManualColors = false;
        _dirty          = true;
    }

    public void CyclePalette()
        => SetPalette((ThermalPalette)(((int)palette + 1) % System.Enum.GetValues(typeof(ThermalPalette)).Length));

    public void SetInverted(bool value) { invert = value; _dirty = true; }
    public void ToggleInvert()          => SetInverted(!invert);

    public void SetManualColors(Color cold, Color mid, Color hot)
    {
        coldColor = cold; midColor = mid; hotColor = hot;
        useManualColors = true;
        _dirty = true;
    }

    /// <summary>
    /// Tracker weapon scope: existing RenderTexture output, no RawImage visibility gate.
    /// </summary>
    public void ConfigureAsScope(RenderTexture rt)
    {
        scopeMode = true;
        autoCreateTexture = false;
        renderOnlyWhenVisible = false;
        ApplyScopeLayerMasks();
        _ownsOutputTexture = false;
        outputTexture = rt;
        if (_cam != null) _cam.targetTexture = rt;
    }

    /// <summary>
    /// ThermalSignature = native heat pass; Default + Ground = replacement pass.
    /// </summary>
    public void ApplyScopeLayerMasks()
    {
        ThermalMonitorLayers.GetScopePassMasks(
            out int native, out int ambient, out int transparent);
        nativeOpaqueLayers = native;
        ambientLayers = ambient;
        nativeTransparentLayers = transparent;
    }

    public void SetReplacementShader(Shader shader)
    {
        if (shader == null) return;
        replacementShader = shader;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internals
    // ──────────────────────────────────────────────────────────────────────────

    private readonly Vector3[] _corners = new Vector3[4];
    private readonly Plane[]   _planes  = new Plane[6];

    private bool IsScreenVisible()
    {
        if (!renderOnlyWhenVisible || targetRawImage == null) return true;
        if (!targetRawImage.isActiveAndEnabled) return false;

        Canvas canvas = targetRawImage.canvas;
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) return true;

        Camera viewer = viewerCamera != null ? viewerCamera : Camera.main;
        if (viewer == null) return true;

        targetRawImage.rectTransform.GetWorldCorners(_corners);
        var bounds = new Bounds(_corners[0], Vector3.zero);
        for (int i = 1; i < 4; i++) bounds.Encapsulate(_corners[i]);
        bounds.Expand(0.05f);   // small margin so it never pops at the edge of view

        GeometryUtility.CalculateFrustumPlanes(viewer, _planes);
        return GeometryUtility.TestPlanesAABB(_planes, bounds);
    }

    private void ApplyPower()
    {
        if (_cam == null) return;

        if (poweredOn)
        {
            _cam.enabled = true;
        }
        else if (scopeMode)
        {
            // Normal scope feed when FLIR is off — thermal passes skipped via IsRenderReady.
        }
        else
        {
            _cam.enabled = false;
            ClearOutputToBlack();
        }
    }

    private void EnsureOutputTexture()
    {
        if (outputTexture == null && autoCreateTexture)
        {
            outputTexture = new RenderTexture(autoTextureSize.x, autoTextureSize.y, 24, RenderTextureFormat.ARGB32)
            {
                name       = "ThermalMonitorRT",
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1
            };
            outputTexture.Create();
            _ownsOutputTexture = true;
        }

        if (outputTexture != null)
        {
            _cam.targetTexture = outputTexture;
            if (targetRawImage != null) targetRawImage.texture = outputTexture;
        }
        else if (_cam.targetTexture == null)
        {
            Debug.LogWarning("[ThermalMonitor] Camera has no target RenderTexture. " +
                             "Assign one on the camera / this component, or enable Auto Create Texture.", this);
        }
    }

    private void ConfigureCamera()
    {
        _cam.allowHDR  = false;
        _cam.allowMSAA = false;
        _cam.allowDynamicResolution = false;
        _cam.stereoTargetEye = StereoTargetEyeMask.None;   // plain mono camera, never an XR eye

        UniversalAdditionalCameraData data = _cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = false;
            data.antialiasing         = AntialiasingMode.None;
            data.renderShadows        = false;   // monitor doesn't need shadow maps
            data.requiresColorTexture = false;
            data.requiresDepthOption  = useSceneDepth ? CameraOverrideOption.On : CameraOverrideOption.Off;
            data.requiresDepthTexture = useSceneDepth;
        }
    }

    private void ClearOutputToBlack()
    {
        RenderTexture rt = _cam != null ? _cam.targetTexture : outputTexture;
        if (rt == null) return;

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
    }

    private void ApplyMaterialProperties()
    {
        if (!useManualColors) ApplyPaletteColors(palette);

        Material m = PostMaterial;

        if (invert) m.EnableKeyword("_INVERT_ON");
        else        m.DisableKeyword("_INVERT_ON");

        m.SetColor(ID_ColdColor, coldColor);
        m.SetColor(ID_MidColor,  midColor);
        m.SetColor(ID_HotColor,  hotColor);
        m.SetFloat(ID_Contrast,   contrast);
        m.SetFloat(ID_Brightness, brightness);
        m.SetFloat(ID_BloomInt,   bloomIntensity);
        m.SetFloat(ID_BloomThr,   bloomThreshold);
        m.SetFloat(ID_HotBlurRad, hotBlurRadius);

        m.SetFloat(ID_GrainInt,   grainIntensity);
        m.SetFloat(ID_GrainBal,   grainBalance);
        m.SetFloat(ID_GrainScale, grainScale);
        m.SetFloat(ID_GrainAnimSpeed, grainAnimationSpeed);
        m.SetFloat(ID_GrainMode,  (float)(int)grainMode);

        m.SetFloat(ID_HaloRadius, haloRadius);
        m.SetColor(ID_HaloColor,  haloColor);
        m.SetFloat(ID_HaloInt,    haloIntensity);
    }

    float GrainTimeNow => Time.realtimeSinceStartup;

    /// <summary>Push all post material fields at blit time (Render Graph does not pick up Update/OnValidate).</summary>
    public void PushPostPassMaterial()
    {
        if (PostMaterial == null) return;
        ApplyMaterialProperties();
        PostMaterial.SetFloat(ID_ThermalGrainTime, GrainTimeNow);
    }

    public void PushSkyHeatPass(RasterCommandBuffer cmd, Matrix4x4 invViewProj)
    {
        if (SkyHeatMaterial == null) return;

        Material sky = RenderSettings.skybox;
        if (sky == null) return;

        bool panoramic = false;
        if (sky.HasProperty("_MainTex"))
        {
            var tex = sky.GetTexture("_MainTex");
            if (tex is Texture2D tex2d)
            {
                Shader.SetGlobalTexture(ID_TM_Sky2D, tex2d);
                panoramic = true;
            }
        }

        if (!panoramic && sky.HasProperty("_Tex"))
        {
            var tex = sky.GetTexture("_Tex");
            if (tex is Cubemap cube)
                Shader.SetGlobalTexture(ID_TM_SkyCube, cube);
            else if (tex is Texture2D tex2d)
            {
                Shader.SetGlobalTexture(ID_TM_Sky2D, tex2d);
                panoramic = true;
            }
        }

        float rotation = 0f;
        if (sky.HasProperty("_Rotation")) rotation = sky.GetFloat("_Rotation") / 360f;

        cmd.SetGlobalFloat(ID_TM_SkyPanoramic, panoramic ? 1f : 0f);
        cmd.SetGlobalFloat(ID_TM_SkyHeatMul, skyHeatMultiplier);
        cmd.SetGlobalFloat(ID_TM_SkyHeatBias, 0.06f);
        cmd.SetGlobalFloat(ID_TM_SkyRotation, rotation);
        cmd.SetGlobalMatrix(ID_TM_InvViewProj, invViewProj);
    }

    public void PushPassGlobals(RasterCommandBuffer cmd)
    {
        cmd.SetGlobalFloat(ID_BaseHeat,       defaultObjectHeat);
        cmd.SetGlobalFloat(ID_HeatVariance,   replacementHeatVariance);
        cmd.SetGlobalFloat(ID_NormalHeatBias, replacementNormalBias);
        cmd.SetGlobalFloat(ID_NormalMode,     replacementNormalMode);
        cmd.SetGlobalFloat(ID_GradBottom,     replacementGradientBottom);
        cmd.SetGlobalFloat(ID_GradTop,        replacementGradientTop);
        cmd.SetGlobalFloat(ID_GradRange,      replacementGradientRange);
        cmd.SetGlobalFloat(ID_GradCenter,     replacementGradientCenter);
        cmd.SetGlobalFloat(ID_NoiseStrength,  replacementNoiseStrength);
        cmd.SetGlobalFloat(ID_NoiseScale,     replacementNoiseScale);
        cmd.SetGlobalFloat(ID_NoiseScale2,    replacementNoiseScale2);
        cmd.SetGlobalFloat(ID_NoiseBlend,     replacementNoiseBlend);
    }

    private void ApplyPaletteColors(ThermalPalette p)
    {
        switch (p)
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
}
