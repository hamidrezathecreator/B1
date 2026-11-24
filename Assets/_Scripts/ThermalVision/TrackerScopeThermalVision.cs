using UnityEngine;

/// <summary>
/// Scope FLIR driver on the scope camera. Requires a pre-assigned <see cref="ThermalMonitorCamera"/> (no runtime AddComponent).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(ThermalMonitorCamera))]
public sealed class TrackerScopeThermalVision : MonoBehaviour
{
    ThermalMonitorCamera monitor;

    public bool UserThermalEnabled => monitor != null && monitor.IsPoweredOn;

    void Awake() => monitor = GetComponent<ThermalMonitorCamera>();

    public void SetUserThermalEnabled(bool enabled) => monitor?.SetPower(enabled);

    /// <summary>Bind live scope output; settings come from the scene <see cref="ThermalMonitorCamera"/>.</summary>
    public void Prepare(RenderTexture scopeOutput, Material replacementMaterial)
    {
        if (monitor == null)
        {
            Debug.LogError("TrackerScopeThermalVision: ThermalMonitorCamera must be on this camera.", this);
            return;
        }

        if (scopeOutput != null)
            monitor.ConfigureAsScope(scopeOutput);

        if (replacementMaterial != null)
            monitor.SetReplacementShader(replacementMaterial.shader);
    }
}