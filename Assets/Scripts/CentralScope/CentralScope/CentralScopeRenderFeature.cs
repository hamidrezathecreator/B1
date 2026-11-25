using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public sealed class CentralScopeRenderFeature : ScriptableRendererFeature
{
    [SerializeField] private Material material;
    [SerializeField] private CentralScopeSettings settings;
    private CentralScopePass pass;

    public override void Create()
    {
        pass = new CentralScopePass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing,
            requiresIntermediateTexture = true
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null || settings == null || pass == null) return;
        var camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game ||
            !camera.TryGetComponent<CentralScopeCamera>(out var scope) ||
            !scope.isActiveAndEnabled || scope.Intensity <= 0f) return;
        pass.Setup(material, settings, scope);
        renderer.EnqueuePass(pass);
    }

    private sealed class CentralScopePass : ScriptableRenderPass
    {
        private Material material;
        private CentralScopeSettings settings;
        private CentralScopeCamera scope;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private static readonly int Amount = Shader.PropertyToID("_RadialBlurAmount");
        private static readonly int Radius = Shader.PropertyToID("_RadialBlurRadius");
        private static readonly int Vignette = Shader.PropertyToID("_VignetteAmount");
        private static readonly int Inner = Shader.PropertyToID("_FeatherInnerRadius");
        private static readonly int Outer = Shader.PropertyToID("_FeatherOuterRadius");
        private static readonly int Curve = Shader.PropertyToID("_FeatherCurve");

        public void Setup(Material sourceMaterial, CentralScopeSettings configuration, CentralScopeCamera cameraScope)
        {
            material = sourceMaterial;
            settings = configuration;
            scope = cameraScope;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || scope == null || scope.Intensity <= 0f) return;
            var profile = scope.IsFalling ? settings.fall : settings.shake;
            float intensity = scope.Intensity;
            properties.SetFloat(Amount, profile.radialBlurAmount * intensity);
            properties.SetFloat(Radius, profile.radialBlurRadius);
            properties.SetFloat(Vignette, profile.vignetteAmount * intensity);
            properties.SetFloat(Inner, profile.featherInnerRadius);
            properties.SetFloat(Outer, Mathf.Max(profile.featherOuterRadius, profile.featherInnerRadius + 0.01f));
            properties.SetFloat(Curve, profile.featherCurve);
            var source = resources.activeColorTexture;
            var descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "_CentralScopeColor";
            descriptor.clearBuffer = false;
            var destination = renderGraph.CreateTexture(descriptor);
            var parameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0)
            {
                propertyBlock = properties
            };
            renderGraph.AddBlitPass(parameters, passName: "CentralScope");
            resources.cameraColor = destination;
        }
    }
}
