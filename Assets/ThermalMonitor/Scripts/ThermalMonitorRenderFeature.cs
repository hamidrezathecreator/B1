using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Thermal Monitor Render Feature — Unity 6 (URP 17, Render Graph).
///
/// Only runs for cameras that have a <see cref="ThermalMonitorCamera"/> component.
/// Every other camera is untouched and pays nothing.
///
/// Three raster passes, between transparents and post-processing:
///   1. Thermal data   – draws heat (R channel) into a private texture:
///        a) opaques with a "ThermalVision" LightMode pass
///        b) ambient-layer objects using the flat replacement shader
///        c) transparents / particles / decals with a "ThermalVision" pass
///   2. Thermal post   – camera colour + thermal data -> palette/halo/bloom/grain -> temp texture
///   3. Copy back      – temp texture -> camera colour
///
/// Setup: add this feature to the Renderer asset used by the monitor camera
/// (best: a dedicated Renderer asset just for the monitor camera).
/// Render Graph must be enabled (default in Unity 6.x).
/// </summary>
public class ThermalMonitorRenderFeature : ScriptableRendererFeature
{
    private ThermalMonitorPass _pass;

    public override void Create()
    {
        _pass = new ThermalMonitorPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera cam = renderingData.cameraData.camera;

        if (!ThermalMonitorCamera.TryGet(cam, out ThermalMonitorCamera monitor)) return;
        if (!monitor.IsRenderReady) return;

        _pass.Setup(monitor);
        renderer.EnqueuePass(_pass);
    }

    // =========================================================================

    private class ThermalMonitorPass : ScriptableRenderPass
    {
        private ThermalMonitorCamera _monitor;

        private static readonly int ID_DataTexture   = Shader.PropertyToID("_ThermalDataTexture");
        private static readonly int ID_DataTexelSize = Shader.PropertyToID("_ThermalDataTexelSize");

        // Objects that carry their own ThermalVision pass.
        private static readonly List<ShaderTagId> s_NativeTags = new List<ShaderTagId>
        {
            new ShaderTagId("ThermalVision")
        };

        // Ambient objects have no ThermalVision pass: match their normal passes and
        // override the shader with the flat replacement shader.
        private static readonly List<ShaderTagId> s_AmbientTags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        // ── Pass data ────────────────────────────────────────────────────────

        private class DataPassData
        {
            public ThermalMonitorCamera monitor;
            public Material             skyMaterial;
            public TextureHandle        blitSource;
            public Matrix4x4            invViewProj;
            public bool                 drawSky;
            public RendererListHandle opaque;
            public RendererListHandle ambient;
            public RendererListHandle transparent;
            public bool hasOpaque, hasAmbient, hasTransparent;
        }

        private class PostPassData
        {
            public ThermalMonitorCamera monitor;
            public Material             material;
            public TextureHandle        source;
            public TextureHandle        thermal;
            public Vector4              texelSize;
        }

        private class CopyPassData
        {
            public TextureHandle source;
        }

        public ThermalMonitorPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public void Setup(ThermalMonitorCamera monitor)
        {
            _monitor = monitor;
            // Particles / decals in the thermal passes sample the scene depth texture.
            ConfigureInput(monitor.UseSceneDepth ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ThermalMonitorCamera m = _monitor;
            if (m == null || m.PostMaterial == null) return;

            UniversalResourceData  resourceData  = frameData.Get<UniversalResourceData>();
            UniversalCameraData    cameraData    = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData     lightData     = frameData.Get<UniversalLightData>();

            // Camera is rendering straight to the backbuffer – not a monitor setup.
            if (resourceData.isActiveTargetBackBuffer) return;

            RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

            int w = Mathf.Max(16, camDesc.width);
            int h = Mathf.Max(16, camDesc.height);

            // ── Thermal data textures (colour + depth) ───────────────────────
            // Single 8-bit channel: cheapest on Quest's tile memory / bandwidth.
            GraphicsFormat heatFormat =
                SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.R16_SFloat;

            var heatDesc = new RenderTextureDescriptor(w, h, heatFormat, GraphicsFormat.None)
            {
                msaaSamples = 1, useMipMap = false, autoGenerateMips = false
            };

            var depthDesc = new RenderTextureDescriptor(w, h, GraphicsFormat.None,
                SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil))
            {
                msaaSamples = 1, useMipMap = false, autoGenerateMips = false
            };

            TextureHandle thermalColor = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, heatDesc, "_ThermalMonitorData", false, FilterMode.Bilinear);
            TextureHandle thermalDepth = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, depthDesc, "_ThermalMonitorDepth", false);

            TextureHandle cameraColor = resourceData.activeColorTexture;

            Matrix4x4 view = cameraData.GetViewMatrix();
            Matrix4x4 proj = cameraData.GetGPUProjectionMatrix();
            Matrix4x4 invViewProj = Matrix4x4.Inverse(proj * view);

            // ── Pass 1: thermal data ─────────────────────────────────────────
            using (var builder = renderGraph.AddRasterRenderPass<DataPassData>("Thermal Monitor - Data", out var pd))
            {
                pd.monitor      = m;
                pd.drawSky      = m.IncludeSkyInThermalData && m.SkyHeatMaterial != null;
                pd.skyMaterial  = m.SkyHeatMaterial;
                pd.blitSource   = cameraColor;
                pd.invViewProj  = invViewProj;

                if (pd.drawSky)
                    builder.UseTexture(cameraColor, AccessFlags.Read);

                if (m.NativeOpaqueLayers != 0)
                {
                    var filter = new FilteringSettings(RenderQueueRange.opaque, m.NativeOpaqueLayers);
                    var draw   = CreateDrawingSettings(s_NativeTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                    draw.perObjectData = PerObjectData.None;
                    pd.opaque = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, draw, filter));
                    builder.UseRendererList(pd.opaque);
                    pd.hasOpaque = true;
                }

                if (m.AmbientLayers != 0 && m.ReplacementShader != null)
                {
                    var filter = new FilteringSettings(RenderQueueRange.opaque, m.AmbientLayers);
                    var draw   = CreateDrawingSettings(s_AmbientTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                    draw.overrideShader = m.ReplacementShader;
                    draw.perObjectData  = PerObjectData.None;
                    pd.ambient = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, draw, filter));
                    builder.UseRendererList(pd.ambient);
                    pd.hasAmbient = true;
                }

                if (m.NativeTransparentLayers != 0)
                {
                    var filter = new FilteringSettings(RenderQueueRange.transparent, m.NativeTransparentLayers);
                    var draw   = CreateDrawingSettings(s_NativeTags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
                    draw.perObjectData = PerObjectData.None;
                    pd.transparent = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, draw, filter));
                    builder.UseRendererList(pd.transparent);
                    pd.hasTransparent = true;
                }

                // Particles/decals read the scene depth texture.
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                builder.SetRenderAttachment(thermalColor, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(thermalDepth, AccessFlags.Write);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);   // we push per-camera globals

                builder.SetRenderFunc(static (DataPassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.ClearRenderTarget(true, true, Color.black);
                    d.monitor.PushPassGlobals(ctx.cmd);

                    if (d.drawSky)
                    {
                        d.monitor.PushSkyHeatPass(ctx.cmd, d.invViewProj);
                        Blitter.BlitTexture(ctx.cmd, d.blitSource, new Vector4(1, 1, 0, 0), d.skyMaterial, 0);
                    }

                    if (d.hasOpaque)      ctx.cmd.DrawRendererList(d.opaque);
                    if (d.hasAmbient)     ctx.cmd.DrawRendererList(d.ambient);
                    if (d.hasTransparent) ctx.cmd.DrawRendererList(d.transparent);
                });
            }

            // ── Pass 2: thermal post-process into a temp texture ─────────────
            RenderTextureDescriptor tmpDesc = camDesc;
            tmpDesc.depthBufferBits = 0;
            tmpDesc.msaaSamples     = 1;

            TextureHandle tempColor = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, tmpDesc, "_ThermalMonitorColor", false, FilterMode.Bilinear);

            using (var builder = renderGraph.AddRasterRenderPass<PostPassData>("Thermal Monitor - Post", out var pd))
            {
                pd.monitor     = m;
                pd.material    = m.PostMaterial;
                pd.source      = cameraColor;
                pd.thermal     = thermalColor;
                pd.texelSize   = new Vector4(1f / w, 1f / h, w, h);

                builder.UseTexture(pd.source,  AccessFlags.Read);
                builder.UseTexture(pd.thermal, AccessFlags.Read);
                builder.SetRenderAttachment(tempColor, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PostPassData d, RasterGraphContext ctx) =>
                {
                    d.monitor.PushPostPassMaterial();
                    d.material = d.monitor.PostMaterial;
                    if (d.material == null) return;

                    d.material.SetTexture(ID_DataTexture, d.thermal);
                    d.material.SetVector(ID_DataTexelSize, d.texelSize);
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }

            // ── Pass 3: copy the thermal image back into the camera colour ───
            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Thermal Monitor - Copy Back", out var pd))
            {
                pd.source = tempColor;

                builder.UseTexture(pd.source, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColor, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (CopyPassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }
    }
}
