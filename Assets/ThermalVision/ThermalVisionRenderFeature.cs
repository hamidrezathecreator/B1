using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

/// <summary>
/// Thermal Vision Render Feature — Layer-Aware, Mobile Production Ready
///
/// ARCHITECTURE:
///   Pass 1 (ThermalOpaquePass)     — Draws opaque objects that have a "ThermalVision"
///                                    LightMode pass. No override shader. Zero cost for
///                                    objects on the wrong layer or lacking the pass.
///   Pass 2 (ThermalAmbientPass)    — Draws objects on the dedicated ambient/replacement
///                                    layer using the flat replacement shader. Only runs
///                                    when that layer mask is non-zero.
///   Pass 3 (ThermalTransparentPass)— Draws transparents that have a "ThermalVision" pass.
///   Pass 4 (ThermalPostProcessPass)— Full-screen blit applying palette/grain/bloom.
///
/// MOBILE OPTIMISATIONS:
///   - All shader property IDs cached as static readonly ints (zero string hashing).
///   - Static readonly ShaderTagId / List collections (zero per-frame GC).
///   - Single CommandBuffer per pass, pooled.
///   - Battery-save frame limiter baked into AddRenderPasses.
///   - R8 / RHalf format toggle driven by ThermalVisionController quality level.
///   - Resolution clamped between minResolution and maxResolution.
///   - Profiling scopes for GPU captures on device.
/// </summary>
public class ThermalVisionRenderFeature_Universal : ScriptableRendererFeature
{
    // ─────────────────────────────────────────────────────────────────────────
    // Settings
    // ─────────────────────────────────────────────────────────────────────────

    [System.Serializable]
    public class Settings
    {
        [Header("Materials & Shaders")]
        public Material thermalPostProcessMaterial;

        [Tooltip("Fallback shader for objects that have NO ThermalVision pass. " +
                 "Assign to the 'Ambient Layer' objects only.")]
        public Shader thermalReplacementShader;

        [Header("Layer Configuration")]
        [Tooltip("Objects on this layer will be rendered with their own ThermalVision shader pass (opaques).")]
        public LayerMask thermalOpaqueNativeLayers = -1;

        [Tooltip("Objects on this layer have NO ThermalVision pass and will be rendered " +
                 "with the flat replacement shader instead. Set to Nothing to disable.")]
        public LayerMask thermalAmbientLayers = 0;

        [Tooltip("Objects on this layer will be rendered with their own ThermalVision shader pass (transparents).")]
        public LayerMask thermalTransparentLayers = -1;

        [Header("Mobile Optimisation")]
        [Range(0.25f, 1f)]
        public float renderScale = 1f;

        [Tooltip("Use single-channel R8/RHalf for the thermal RT (saves bandwidth on mobile).")]
        public bool useR8Format = true;

        [Header("Resolution Clamping")]
        public int minResolution = 320;
        public int maxResolution = 1920;

        [Header("Battery Save")]
        [Tooltip("Cap the thermal render rate independently of game frame rate.")]
        public bool batterySaveMode = false;
        [Range(15, 60)]
        public int batterySaveFPS = 30;

        [Header("Debug")]
        public bool showDebugStats = false;
    }

    public Settings settings = new Settings();

    // ─────────────────────────────────────────────────────────────────────────
    // Passes
    // ─────────────────────────────────────────────────────────────────────────

    private ThermalOpaqueNativePass  opaqueNativePass;
    private ThermalAmbientPass       ambientPass;
    private ThermalTransparentPass   transparentPass;
    private ThermalPostProcessPass   postProcessPass;
    private Material                 _replacementMaterial;

    // Battery save state (shared across passes via feature)
    private float _frameTimeAccumulator;
    private float _targetFrameTime = 1f / 30f;

    // ─────────────────────────────────────────────────────────────────────────
    // Cached property IDs (static — set once, shared across all instances)
    // ─────────────────────────────────────────────────────────────────────────

    internal static readonly int ID_ThermalDataTexture = Shader.PropertyToID("_ThermalDataTexture");
    internal static readonly int ID_ThermalRenderScale = Shader.PropertyToID("_ThermalRenderScale");
    internal static readonly int ID_ThermalUseR8       = Shader.PropertyToID("_ThermalUseR8");

    const string ThermalDataRtName = "_ThermalDataRT";

    sealed class ThermalSharedRt
    {
        public RTHandle Handle;
        /// <summary>Per-frame thermal color target for Render Graph (set by pass 1, reused by 2–3).</summary>
        public TextureHandle GraphColor;

        public void Release()
        {
            Handle?.Release();
            Handle = null;
            GraphColor = default;
        }
    }

    readonly ThermalSharedRt _thermalRt = new();

    // ─────────────────────────────────────────────────────────────────────────
    // ScriptableRendererFeature interface
    // ─────────────────────────────────────────────────────────────────────────

    public override void Create()
    {
        if (settings.thermalReplacementShader != null)
        {
            CoreUtils.Destroy(_replacementMaterial);
            _replacementMaterial = CoreUtils.CreateEngineMaterial(settings.thermalReplacementShader);
        }

        opaqueNativePass = new ThermalOpaqueNativePass(settings, _thermalRt);
        ambientPass      = new ThermalAmbientPass(settings, _thermalRt, _replacementMaterial);
        transparentPass  = new ThermalTransparentPass(settings, _thermalRt);
        postProcessPass  = new ThermalPostProcessPass(settings);
        postProcessPass.ConfigureInput(ScriptableRenderPassInput.Color);

        _targetFrameTime = 1f / Mathf.Clamp(settings.batterySaveFPS, 15, 60);
    }

    protected override void Dispose(bool disposing)
    {
        _thermalRt.Release();
        CoreUtils.Destroy(_replacementMaterial);
        _replacementMaterial = null;
        base.Dispose(disposing);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cam = renderingData.cameraData.camera;
        if (!TrackerScopeThermalGate.ShouldRunThermalPasses(cam) &&
            !(ThermalVisionController.IsEnabled && TrackerScopeThermalGate.ScopeCamera == cam))
            return;

        if (settings.thermalPostProcessMaterial == null) return;

        _thermalRt.GraphColor = default;

        // Battery-save frame limiter
        if (settings.batterySaveMode)
        {
            _frameTimeAccumulator += Time.unscaledDeltaTime;
            if (_frameTimeAccumulator < _targetFrameTime) return;
            _frameTimeAccumulator = 0f;
        }

        // Pass 1 — opaques with native ThermalVision pass
        if (settings.thermalOpaqueNativeLayers != 0)
            renderer.EnqueuePass(opaqueNativePass);

        // Pass 2 — ambient/replacement layer (objects without a ThermalVision pass)
        if (settings.thermalAmbientLayers != 0 && settings.thermalReplacementShader != null)
            renderer.EnqueuePass(ambientPass);

        // Pass 3 — transparents with native ThermalVision pass
        if (settings.thermalTransparentLayers != 0)
            renderer.EnqueuePass(transparentPass);

        // Pass 4 — full-screen post process
        renderer.EnqueuePass(postProcessPass);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: build the thermal RT descriptor
    // ─────────────────────────────────────────────────────────────────────────

    internal static RenderTextureDescriptor BuildThermalDescriptor(
        RenderTextureDescriptor cameraDesc, Settings s)
    {
        float scale = Shader.GetGlobalFloat(ID_ThermalRenderScale);
        if (scale <= 0f) scale = s.renderScale;

        bool r8 = (Shader.GetGlobalInt(ID_ThermalUseR8) == 1) || s.useR8Format;

        int w = Mathf.RoundToInt(cameraDesc.width  * scale);
        int h = Mathf.RoundToInt(cameraDesc.height * scale);

        w = Mathf.Clamp(w, s.minResolution, s.maxResolution > 0 ? s.maxResolution : w);
        h = Mathf.Clamp(h, s.minResolution, s.maxResolution > 0 ? s.maxResolution : h);

        var desc = cameraDesc;
        desc.width            = w;
        desc.height           = h;
        desc.colorFormat      = r8 ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
        desc.graphicsFormat   = r8 ? GraphicsFormat.R16_SFloat : GraphicsFormat.R16G16B16A16_SFloat;
        desc.depthBufferBits  = 16;
        desc.depthStencilFormat = GraphicsFormat.None;
        desc.msaaSamples      = 1;

        return desc;
    }

    /// <summary>Color-only RT for Render Graph — ReAllocateHandleIfNeeded rejects combined color+depth descriptors.</summary>
    static RenderTextureDescriptor BuildThermalColorDescriptor(RenderTextureDescriptor cameraDesc, Settings s)
    {
        var desc = BuildThermalDescriptor(cameraDesc, s);
        desc.depthBufferBits = 0;
        desc.depthStencilFormat = GraphicsFormat.None;
        return desc;
    }

    static TextureDesc BuildThermalGraphTextureDesc(in RenderTextureDescriptor cameraDesc, Settings s)
    {
        var rtDesc = BuildThermalColorDescriptor(cameraDesc, s);
        var texDesc = new TextureDesc(rtDesc.width, rtDesc.height);
        texDesc.format = rtDesc.graphicsFormat;
        texDesc.msaaSamples = (MSAASamples)rtDesc.msaaSamples;
        texDesc.filterMode = FilterMode.Bilinear;
        texDesc.wrapMode = TextureWrapMode.Clamp;
        texDesc.name = ThermalDataRtName;
        texDesc.clearBuffer = false;
        return texDesc;
    }

    static TextureHandle GetOrCreateThermalGraphColor(
        RenderGraph renderGraph, ThermalSharedRt thermalRt, in RenderTextureDescriptor cameraDesc, Settings s, bool createIfMissing)
    {
        if (thermalRt.GraphColor.IsValid()) return thermalRt.GraphColor;
        if (!createIfMissing) return default;

        thermalRt.GraphColor = renderGraph.CreateTexture(BuildThermalGraphTextureDesc(cameraDesc, s));
        return thermalRt.GraphColor;
    }

    // =========================================================================
    // PASS 1 — Opaque objects with a native ThermalVision LightMode pass
    // =========================================================================

    class ThermalOpaqueNativePass : ScriptableRenderPass
    {
        private readonly Settings         _s;
        private readonly ThermalSharedRt  _thermalRt;
        private readonly ProfilingSampler _sampler = new ProfilingSampler("Thermal – Opaque Native");

        // Only look for the custom LightMode tag — materials without it are skipped automatically.
        private static readonly List<ShaderTagId> _tags = new List<ShaderTagId>
        {
            new ShaderTagId("ThermalVision")
        };

        public ThermalOpaqueNativePass(Settings s, ThermalSharedRt thermalRt)
        {
            _s          = s;
            _thermalRt  = thermalRt;
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            var desc = BuildThermalDescriptor(data.cameraData.cameraTargetDescriptor, _s);
            RenderingUtils.ReAllocateIfNeeded(
                ref _thermalRt.Handle, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ThermalDataRtName);
            ConfigureTarget(_thermalRt.Handle);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, _sampler))
            {
                // Flush so the RT is cleared before drawing
                ctx.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var filter = new FilteringSettings(RenderQueueRange.opaque, _s.thermalOpaqueNativeLayers);
                var draw   = CreateDrawingSettings(_tags, ref data, SortingCriteria.CommonOpaque);
                // No override — each object's own ThermalVision pass runs.
                draw.overrideMaterial = null;
                draw.overrideShader   = null;
                draw.perObjectData    = PerObjectData.None;

                ctx.DrawRenderers(data.cullResults, ref draw, ref filter);

                // Make the RT available to subsequent passes as a global texture.
                cmd.SetGlobalTexture(ID_ThermalDataTexture, _thermalRt.Handle);
            }

            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        class PassData
        {
            internal RendererListHandle rendererList;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            TextureHandle thermalTarget = GetOrCreateThermalGraphColor(
                renderGraph, _thermalRt, cameraData.cameraTargetDescriptor, _s, createIfMissing: true);
            if (!thermalTarget.IsValid()) return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("ThermalOpaqueNative", out var passData, _sampler))
            {
                var filter = new FilteringSettings(RenderQueueRange.opaque, _s.thermalOpaqueNativeLayers);
                var draw = RenderingUtils.CreateDrawingSettings(
                    _tags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                draw.overrideMaterial = null;
                draw.overrideShader = null;
                draw.perObjectData = PerObjectData.None;

                passData.rendererList = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, draw, filter));

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(thermalTarget, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(thermalTarget, ID_ThermalDataTexture);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(true, false, Color.black);
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }
        }
    }

    // =========================================================================
    // PASS 2 — Ambient layer: objects WITHOUT a ThermalVision pass
    //          Rendered with the flat replacement shader.
    // =========================================================================

    class ThermalAmbientPass : ScriptableRenderPass
    {
        private readonly Settings         _s;
        private readonly ThermalSharedRt  _thermalRt;
        private readonly Material         _replacementMaterial;
        private readonly ProfilingSampler _sampler = new ProfilingSampler("Thermal – Ambient (Replacement)");

        // Standard opaque tags — these objects will NOT have a ThermalVision pass,
        // so we override with the replacement shader.
        private static readonly List<ShaderTagId> _tags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("LightweightForward"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        public ThermalAmbientPass(Settings s, ThermalSharedRt thermalRt, Material replacementMaterial)
        {
            _s                   = s;
            _thermalRt           = thermalRt;
            _replacementMaterial = replacementMaterial;
            renderPassEvent      = RenderPassEvent.AfterRenderingSkybox + 1;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            if (_thermalRt.Handle == null) return;
            ConfigureTarget(_thermalRt.Handle);
            ConfigureClear(ClearFlag.None, Color.black);
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, _sampler))
            {
                ctx.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var filter = new FilteringSettings(RenderQueueRange.opaque, _s.thermalAmbientLayers);
                var draw   = CreateDrawingSettings(_tags, ref data, SortingCriteria.CommonOpaque);

                draw.overrideMaterial = _replacementMaterial;
                draw.overrideShader   = null;
                draw.perObjectData    = PerObjectData.None;

                ctx.DrawRenderers(data.cullResults, ref draw, ref filter);
            }

            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        class PassData
        {
            internal RendererListHandle rendererList;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_replacementMaterial == null) return;

            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            TextureHandle thermalTarget = GetOrCreateThermalGraphColor(
                renderGraph, _thermalRt, cameraData.cameraTargetDescriptor, _s, createIfMissing: true);
            if (!thermalTarget.IsValid()) return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("ThermalAmbient", out var passData, _sampler))
            {
                var filter = new FilteringSettings(RenderQueueRange.opaque, _s.thermalAmbientLayers);
                var draw = RenderingUtils.CreateDrawingSettings(
                    _tags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                draw.overrideMaterial = _replacementMaterial;
                draw.overrideShader = null;
                draw.perObjectData = PerObjectData.None;

                passData.rendererList = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, draw, filter));

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(thermalTarget, 0, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(thermalTarget, ID_ThermalDataTexture);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }
        }

    }

    // =========================================================================
    // PASS 3 — Transparent objects with a native ThermalVision LightMode pass
    // =========================================================================

    class ThermalTransparentPass : ScriptableRenderPass
    {
        private readonly Settings         _s;
        private readonly ThermalSharedRt  _thermalRt;
        private readonly ProfilingSampler _sampler = new ProfilingSampler("Thermal – Transparent Native");

        private static readonly List<ShaderTagId> _tags = new List<ShaderTagId>
        {
            new ShaderTagId("ThermalVision")
        };

        public ThermalTransparentPass(Settings s, ThermalSharedRt thermalRt)
        {
            _s          = s;
            _thermalRt  = thermalRt;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            if (_thermalRt.Handle == null) return;
            ConfigureTarget(_thermalRt.Handle);
            ConfigureClear(ClearFlag.None, Color.black);
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, _sampler))
            {
                ctx.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var filter = new FilteringSettings(RenderQueueRange.transparent, _s.thermalTransparentLayers);
                var draw   = CreateDrawingSettings(_tags, ref data, SortingCriteria.CommonTransparent);
                draw.overrideMaterial = null;
                draw.overrideShader   = null;
                draw.perObjectData    = PerObjectData.None;

                ctx.DrawRenderers(data.cullResults, ref draw, ref filter);
            }

            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        class PassData
        {
            internal RendererListHandle rendererList;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            TextureHandle thermalTarget = GetOrCreateThermalGraphColor(
                renderGraph, _thermalRt, cameraData.cameraTargetDescriptor, _s, createIfMissing: true);
            if (!thermalTarget.IsValid()) return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("ThermalTransparent", out var passData, _sampler))
            {
                var filter = new FilteringSettings(RenderQueueRange.transparent, _s.thermalTransparentLayers);
                var draw = RenderingUtils.CreateDrawingSettings(
                    _tags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
                draw.overrideMaterial = null;
                draw.overrideShader = null;
                draw.perObjectData = PerObjectData.None;

                passData.rendererList = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, draw, filter));

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(thermalTarget, 0, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(thermalTarget, ID_ThermalDataTexture);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }
        }

    }

    // =========================================================================
    // PASS 4 — Full-screen post-process blit
    // =========================================================================

    class ThermalPostProcessPass : ScriptableRenderPass
    {
        private readonly Settings         _s;
        private readonly ProfilingSampler _sampler = new ProfilingSampler("Thermal – Post Process");

        public ThermalPostProcessPass(Settings s)
        {
            _s              = s;
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public override void Execute(ScriptableRenderContext ctx, ref RenderingData data)
        {
            if (_s.thermalPostProcessMaterial == null) return;

            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, _sampler))
            {
#pragma warning disable CS0618
                Blit(cmd, ref data, _s.thermalPostProcessMaterial, 0);
#pragma warning restore CS0618
            }

            ctx.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_s.thermalPostProcessMaterial == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()) return;

            var destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "ThermalScopeColor";
            destinationDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            renderGraph.AddBlitPass(
                new RenderGraphUtils.BlitMaterialParameters(source, destination, _s.thermalPostProcessMaterial, 0),
                passName: "ThermalPostProcess");

            resourceData.cameraColor = destination;
        }
    }
}