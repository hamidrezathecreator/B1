using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Scope-only FLIR: builds _ThermalDataTexture with DrawRenderers + your ThermalPostProcess shader blit.
/// No ScriptableRendererFeature / Render Graph.
/// </summary>
public static class ScopeThermalComposite
{
    static readonly List<ShaderTagId> ThermalTags = new() { new ShaderTagId("ThermalVision") };
    static readonly List<ShaderTagId> ForwardTags = new()
    {
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly"),
        new ShaderTagId("ForwardOnly"),
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("LightweightForward")
    };

    static readonly int CameraOpaqueId = Shader.PropertyToID("_CameraOpaqueTexture");
    static readonly int ThermalDataId = Shader.PropertyToID("_ThermalDataTexture");
    static readonly int ThermalDataTexelSizeId = Shader.PropertyToID("_ThermalDataTexture_TexelSize");

    static RenderTexture thermalData;
    static Material luminanceFallback;
    static readonly ProfilingSampler Sampler = new("ScopeThermalComposite");

    public static void Composite(
        ScriptableRenderContext context,
        Camera scopeCamera,
        RenderTexture scopeColor,
        Material postMaterial,
        Material replacementMaterial,
        ThermalVisionController palette)
    {
        if (scopeCamera == null || scopeColor == null || postMaterial == null) return;

        EnsureThermalDataRt(scopeColor);
        palette?.ApplyShaderProperties();

        var nativeMask = palette != null ? palette.ThermalOpaqueLayerMask : -1;
        var ambientMask = palette != null ? palette.ThermalAmbientLayerMask : -1;
        if (!RenderThermalData(context, scopeCamera, thermalData, replacementMaterial, nativeMask, ambientMask))
            BlitLuminanceFallback(scopeColor, thermalData);

        Shader.SetGlobalTexture(CameraOpaqueId, scopeColor);
        Shader.SetGlobalTexture(ThermalDataId, thermalData);
        Shader.SetGlobalVector(ThermalDataTexelSizeId, new Vector4(
            1f / thermalData.width, 1f / thermalData.height, thermalData.width, thermalData.height));

        var desc = scopeColor.descriptor;
        desc.depthBufferBits = 0;
        var tmp = RenderTexture.GetTemporary(desc);
        postMaterial.EnableKeyword("QUALITY_HIGH");
        postMaterial.DisableKeyword("QUALITY_LOW");
        postMaterial.DisableKeyword("QUALITY_MEDIUM");

        Graphics.Blit(scopeColor, tmp, postMaterial, 0);
        Graphics.Blit(tmp, scopeColor);
        RenderTexture.ReleaseTemporary(tmp);
    }

    static void EnsureThermalDataRt(RenderTexture scopeColor)
    {
        var desc = scopeColor.descriptor;
        desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat;
        desc.depthBufferBits = 24;
        desc.msaaSamples = 1;
        if (thermalData != null && thermalData.width == desc.width && thermalData.height == desc.height &&
            thermalData.graphicsFormat == desc.graphicsFormat)
            return;
        if (thermalData != null) thermalData.Release();
        thermalData = new RenderTexture(desc) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "_ScopeThermalData" };
        thermalData.Create();
    }

    static bool RenderThermalData(
        ScriptableRenderContext context,
        Camera cam,
        RenderTexture dest,
        Material replacement,
        int nativeLayerMask,
        int ambientLayerMask)
    {
        if (!cam.TryGetCullingParameters(false, out var cullingParameters)) return false;
        var cull = context.Cull(ref cullingParameters);

        var cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, Sampler))
        {
            cmd.SetRenderTarget(dest.colorBuffer, dest.depthBuffer);
            cmd.ClearRenderTarget(true, true, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var sortOpaque = cam.opaqueSortMode == OpaqueSortMode.Default
                ? SortingCriteria.CommonOpaque
                : SortingCriteria.SortingLayer | SortingCriteria.RenderQueue;

            if (nativeLayerMask != 0)
            {
                var thermalDraw = CreateDrawingSettings(ThermalTags, sortOpaque, cam);
                var nativeFilter = new FilteringSettings(RenderQueueRange.opaque, nativeLayerMask);
                context.DrawRenderers(cull, ref thermalDraw, ref nativeFilter);
            }

            if (replacement != null && ambientLayerMask != 0)
            {
                var replaceDraw = CreateDrawingSettings(ForwardTags, sortOpaque, cam);
                replaceDraw.overrideMaterial = replacement;
                replaceDraw.overrideShader = null;
                replaceDraw.perObjectData = PerObjectData.None;
                var ambientFilter = new FilteringSettings(RenderQueueRange.opaque, ambientLayerMask);
                context.DrawRenderers(cull, ref replaceDraw, ref ambientFilter);
            }

            if (nativeLayerMask != 0)
            {
                var transDraw = CreateDrawingSettings(ThermalTags, SortingCriteria.CommonTransparent, cam);
                var transFilter = new FilteringSettings(RenderQueueRange.transparent, nativeLayerMask);
                context.DrawRenderers(cull, ref transDraw, ref transFilter);
            }

            context.Submit();
        }

        CommandBufferPool.Release(cmd);
        return true;
    }

    static DrawingSettings CreateDrawingSettings(List<ShaderTagId> tags, SortingCriteria sort, Camera cam)
    {
        var sortingSettings = new SortingSettings(cam) { criteria = sort };
        var draw = new DrawingSettings(tags[0], sortingSettings);
        for (var i = 1; i < tags.Count; i++)
            draw.SetShaderPassName(i, tags[i]);
        draw.perObjectData = PerObjectData.None;
        return draw;
    }

    static void BlitLuminanceFallback(RenderTexture source, RenderTexture dest)
    {
        if (luminanceFallback == null)
            luminanceFallback = new Material(Shader.Find("Hidden/ScopeThermalLuminance"));
        if (luminanceFallback == null) return;
        Graphics.Blit(source, dest, luminanceFallback);
    }

    public static void Release()
    {
        if (thermalData != null) { thermalData.Release(); thermalData = null; }
    }
}