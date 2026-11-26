using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class XrLensFlareRenderFeature : ScriptableRendererFeature
{
    [SerializeField] Shader flareShader;
    Material material;
    Camera eyeCamera;
    FlarePass pass;

    public override void Create()
    {
        CoreUtils.Destroy(material);
        material = flareShader != null ? CoreUtils.CreateEngineMaterial(flareShader) : null;
        if (eyeCamera == null)
        {
            var cameraObject = new GameObject("Lens Flare Eye Projection") { hideFlags = HideFlags.HideAndDontSave };
            eyeCamera = cameraObject.AddComponent<Camera>();
            eyeCamera.enabled = false;
        }
        pass = new FlarePass(material, eyeCamera);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null || LensFlareCommonSRP.Instance.IsEmpty()) return;
        if (!renderingData.cameraData.postProcessEnabled) return;
        if (renderingData.cameraData.renderType != CameraRenderType.Base) return;
        if (renderingData.cameraData.camera.cameraType == CameraType.Preview) return;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        if (eyeCamera != null) CoreUtils.Destroy(eyeCamera.gameObject);
    }

    public static void RenderEye(CommandBuffer command, Material flareMaterial, Camera camera, XRPass xr,
        int eyeIndex, Matrix4x4 viewProjection, RenderTargetIdentifier destination, int width, int height,
        Camera projectionCamera)
    {
        if (xr.enabled)
        {
            projectionCamera.CopyFrom(camera);
            projectionCamera.enabled = false;
            var inverseView = xr.GetViewMatrix(xr.singlePassEnabled ? eyeIndex : 0).inverse;
            projectionCamera.transform.SetPositionAndRotation(inverseView.GetColumn(3),
                Quaternion.LookRotation(-inverseView.GetColumn(2), inverseView.GetColumn(1)));
            camera = projectionCamera;
        }
        LensFlareCommonSRP.ComputeOcclusion(flareMaterial, camera, xr, eyeIndex, width, height,
            false, 0f, 1f, true, camera.transform.position, viewProjection, command,
            false, false, null, null);
        LensFlareCommonSRP.DoLensFlareDataDrivenCommon(flareMaterial, camera, new Rect(0f, 0f, width, height),
            xr, eyeIndex, width, height, false, 0f, 1f, true, camera.transform.position,
            viewProjection, command, false, false, null, null, destination, LightAttenuation, false);
    }

    static float LightAttenuation(Light light, Camera camera, Vector3 direction)
    {
        if (light.type == LightType.Directional)
            return LensFlareCommonSRP.ShapeAttenuationDirLight(light.transform.forward, camera.transform.forward);
        if (light.type == LightType.Spot)
            return LensFlareCommonSRP.ShapeAttenuationSpotConeLight(light.transform.forward, direction,
                light.spotAngle, light.innerSpotAngle / 180f);
        return 1f;
    }

    sealed class FlarePass : ScriptableRenderPass
    {
        readonly Material material;
        readonly Camera eyeCamera;
        static readonly int DepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        static readonly int DepthTexelSize = Shader.PropertyToID("_CameraDepthTexture_TexelSize");

        sealed class PassData
        {
            public Material material;
            public UniversalCameraData camera;
            public Camera eyeCamera;
            public TextureHandle color;
            public TextureHandle depth;
            public int width;
            public int height;
        }

        public FlarePass(Material flareMaterial, Camera projectionCamera)
        {
            material = flareMaterial;
            eyeCamera = projectionCamera;
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
            var camera = frameData.Get<UniversalCameraData>();
            var descriptor = graph.GetTextureDesc(resources.activeColorTexture);
            using (var builder = graph.AddUnsafePass<PassData>("XR Lens Flares", out var data))
            {
                data.material = material;
                data.camera = camera;
                data.eyeCamera = eyeCamera;
                data.color = resources.activeColorTexture;
                data.depth = resources.cameraDepthTexture;
                data.width = descriptor.width;
                data.height = descriptor.height;
                builder.UseTexture(data.color, AccessFlags.ReadWrite);
                builder.UseTexture(data.depth, AccessFlags.Read);
                if (LensFlareCommonSRP.IsOcclusionRTCompatible())
                    builder.UseTexture(graph.ImportTexture(LensFlareCommonSRP.occlusionRT), AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData state, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    RTHandle depth = state.depth;
                    RTHandle color = state.color;
                    CoreUtils.SetKeyword(state.material, "B1_FLARE_DEPTH_ARRAY", depth.rt.dimension == TextureDimension.Tex2DArray);
                    command.SetGlobalTexture(DepthTexture, depth);
                    command.SetGlobalVector(DepthTexelSize, new Vector4(1f / depth.rt.width, 1f / depth.rt.height, depth.rt.width, depth.rt.height));
                    var xr = state.camera.xr;
                    var viewCount = xr.enabled && xr.singlePassEnabled ? xr.viewCount : 1;
                    for (var viewIndex = 0; viewIndex < viewCount; viewIndex++)
                    {
                        var projectionMatrix = xr.enabled ? xr.GetProjMatrix(viewIndex) : state.camera.camera.nonJitteredProjectionMatrix;
                        var projection = GL.GetGPUProjectionMatrix(projectionMatrix, true);
                        var viewProjection = projection * state.camera.GetViewMatrix(viewIndex);
                        RenderEye(command, state.material, state.camera.camera, xr,
                            xr.singlePassEnabled ? viewIndex : xr.multipassId, viewProjection,
                            color, state.width, state.height, state.eyeCamera);
                    }
                });
            }
        }
    }
}
