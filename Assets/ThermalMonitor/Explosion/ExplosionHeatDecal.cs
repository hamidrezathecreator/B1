// ========================================================
// ExplosionHeatDecal.cs - Production-Ready Pooled Thermal Decal
// ========================================================
// Optimized for high-frequency spawning with proper pooling
// Handles heat buildup with ease-in, then decay with ease-out

using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class ExplosionHeatDecal : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════
    // RUNTIME STATE
    // ════════════════════════════════════════════════════════════
    private Material decalMaterial;
    private MeshRenderer meshRenderer;
    private float elapsedTime;
    private bool isActive;
    
    // Current settings (set by pool when spawned)
    private float peakHeat;
    private float buildupDuration;
    private float decayDuration;
    private AnimationCurve buildupCurve;
    private AnimationCurve decayCurve;
    
    // Animation phase tracking
    public enum HeatPhase { BuildingUp, Decaying, Complete }
    private HeatPhase currentPhase;
    
    // Material property IDs (cached for performance)
    private static readonly int _ThermalHeatID = Shader.PropertyToID("_ThermalHeat");
    private static readonly int _MainTexID = Shader.PropertyToID("_MainTex");
    private static readonly int _ColorID = Shader.PropertyToID("_Color");
    
    // Pool reference
    private bool isInitialized;
    
    // ════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ════════════════════════════════════════════════════════════
    
    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        
        // Create material instance only once
        if (meshRenderer.sharedMaterial != null)
        {
            decalMaterial = meshRenderer.material; // Creates instance
        }
        
        isInitialized = true;
    }
    
    // ════════════════════════════════════════════════════════════
    // UPDATE LOOP
    // ════════════════════════════════════════════════════════════
    
    void Update()
    {
        if (!isActive || decalMaterial == null) return;
        
        elapsedTime += Time.deltaTime;
        
        switch (currentPhase)
        {
            case HeatPhase.BuildingUp:
                UpdateBuildupPhase();
                break;
                
            case HeatPhase.Decaying:
                UpdateDecayPhase();
                break;
        }
    }
    
    /// <summary>
    /// Handle heat buildup phase (ease-in)
    /// </summary>
    private void UpdateBuildupPhase()
    {
        float progress = Mathf.Clamp01(elapsedTime / Mathf.Max(0.01f, buildupDuration));
        
        // Evaluate buildup curve (quick ease-in)
        float currentHeat = peakHeat * buildupCurve.Evaluate(progress);
        decalMaterial.SetFloat(_ThermalHeatID, currentHeat);
        
        // Transition to decay phase when buildup complete
        if (progress >= 1.0f)
        {
            currentPhase = HeatPhase.Decaying;
            elapsedTime = 0f;  // Reset timer for decay phase
        }
    }
    
    /// <summary>
    /// Handle heat decay phase (ease-out)
    /// </summary>
    private void UpdateDecayPhase()
    {
        float progress = Mathf.Clamp01(elapsedTime / Mathf.Max(0.01f, decayDuration));
        
        // Evaluate decay curve (slower ease-out)
        float currentHeat = peakHeat * decayCurve.Evaluate(progress);
        decalMaterial.SetFloat(_ThermalHeatID, currentHeat);
        
        // Return to pool when complete
        if (progress >= 1.0f)
        {
            ReturnToPool();
        }
    }
    
    // ════════════════════════════════════════════════════════════
    // POOL INTERFACE
    // ════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Initialize decal from pool (called by ExplosionHeatManager)
    /// </summary>
    public void Initialize(Vector3 position, Quaternion rotation, float size, float heat, 
                          float buildupTime, float decayTime, 
                          AnimationCurve buildupCrv, AnimationCurve decayCrv, 
                          Texture2D texture = null)
    {
        // Ensure material is ready
        if (!isInitialized || decalMaterial == null)
        {
            Debug.LogError("ExplosionHeatDecal: Not properly initialized!");
            return;
        }
        
        // Position and scale the cube
        transform.position = position;
        transform.rotation = rotation;
        transform.localScale = new Vector3(size, size, size);
        if (ThermalMonitorLayers.Index >= 0)
            gameObject.layer = ThermalMonitorLayers.Index;
        
        // Store settings (use references to avoid GC)
        peakHeat = heat;
        buildupDuration = buildupTime;
        decayDuration = decayTime;
        buildupCurve = buildupCrv;
        decayCurve = decayCrv;
        
        // Reset animation state
        elapsedTime = 0f;
        currentPhase = HeatPhase.BuildingUp;
        isActive = true;
        
        // Start at zero heat
        decalMaterial.SetFloat(_ThermalHeatID, 0f);
        
        // Update texture if provided
        if (texture != null)
        {
            decalMaterial.SetTexture(_MainTexID, texture);
        }
        
        // Enable rendering
        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;
        }
        
        gameObject.SetActive(true);
    }
    
    /// <summary>
    /// Return this decal to the pool
    /// </summary>
    public void ReturnToPool()
    {
        isActive = false;
        currentPhase = HeatPhase.Complete;
        
        // Disable rendering (keep material instance)
        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }
        
        gameObject.SetActive(false);
        
        // Notify manager to return to pool
        ExplosionHeatManager.Instance?.ReturnDecalToPool(this);
    }
    
    /// <summary>
    /// Manually set heat value (stops automatic animation)
    /// </summary>
    public void SetHeat(float heat)
    {
        if (decalMaterial != null)
        {
            decalMaterial.SetFloat(_ThermalHeatID, Mathf.Clamp01(heat));
        }
        isActive = false;
        currentPhase = HeatPhase.Complete;
    }
    
    /// <summary>
    /// Force immediate return to pool (for cleanup)
    /// </summary>
    public void ForceReturnToPool()
    {
        ReturnToPool();
    }
    
    // ════════════════════════════════════════════════════════════
    // CLEANUP
    // ════════════════════════════════════════════════════════════
    
    void OnDestroy()
    {
        // Cleanup material instance
        if (decalMaterial != null)
        {
            Destroy(decalMaterial);
            decalMaterial = null;
        }
    }
    
    // ════════════════════════════════════════════════════════════
    // DEBUG INFO
    // ════════════════════════════════════════════════════════════
    
    public bool IsActive => isActive;
    public HeatPhase CurrentPhase => currentPhase;
    public float CurrentProgress => currentPhase == HeatPhase.BuildingUp 
        ? (elapsedTime / buildupDuration) 
        : (elapsedTime / decayDuration);
}