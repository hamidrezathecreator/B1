using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Scope FLIR via CommandBuffer DrawRenderers + your ThermalPostProcess shader (no URP render feature).
/// </summary>
[DisallowMultipleComponent]
public sealed class TrackerScopeThermalVision : MonoBehaviour
{
    [SerializeField] Material thermalPostMaterial;
    [SerializeField] Material thermalReplacementMaterial;

    Camera scopeCamera;
    ThermalVisionController palette;
    bool userThermalEnabled;

    public bool UserThermalEnabled => userThermalEnabled;

    public void SetUserThermalEnabled(bool enabled)
    {
        userThermalEnabled = enabled;
        palette?.SetScopeThermalLive(enabled);
    }

    public void SetReplacementMaterial(Material mat) => thermalReplacementMaterial = mat;

    public static TrackerScopeThermalVision Bind(
        Camera scopeCam,
        Material postMaterial,
        Material replacementMaterial)
    {
        if (scopeCam == null) return null;
        var driver = scopeCam.GetComponent<TrackerScopeThermalVision>();
        if (driver == null) driver = scopeCam.gameObject.AddComponent<TrackerScopeThermalVision>();
        driver.scopeCamera = scopeCam;
        driver.thermalPostMaterial = postMaterial;
        driver.thermalReplacementMaterial = replacementMaterial;
        driver.EnsurePalette();
        return driver;
    }

    void EnsurePalette()
    {
        if (scopeCamera == null) return;
        palette = scopeCamera.GetComponent<ThermalVisionController>();
        if (palette == null) palette = scopeCamera.gameObject.AddComponent<ThermalVisionController>();
        if (thermalPostMaterial != null)
            palette.AssignPostMaterial(thermalPostMaterial);
        palette.ScopeOnlyMode();
        enabled = true;
    }

    void OnEnable()
    {
        EnsurePalette();
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        ScopeThermalComposite.Release();
        palette?.EndScopeRender();
    }

    void OnEndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!userThermalEnabled || thermalPostMaterial == null) return;
        if (scopeCamera == null || camera != scopeCamera) return;
        var rt = scopeCamera.targetTexture as RenderTexture;
        if (rt == null || !rt.IsCreated()) return;

        palette?.BeginScopeRender();
        ScopeThermalComposite.Composite(context, scopeCamera, rt, thermalPostMaterial, thermalReplacementMaterial, palette);
        palette?.EndScopeRender();
    }
}