// ========================================================
// ExplosionHeatManager.cs - Production-Ready Pooled Decal Manager
// ========================================================
// Optimized for high-frequency spawning with:
// - Preallocated pools to avoid runtime instantiation
// - Spatial batching to reduce overdraw
// - Automatic pool growth with configurable limits
// - Memory-efficient data structures
// - Built-in performance monitoring

using UnityEngine;
using System.Collections.Generic;

public class ExplosionHeatManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════
    // SINGLETON
    // ════════════════════════════════════════════════════════════
    public static ExplosionHeatManager Instance { get; private set; }
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        InitializePool();
    }
    
    // ════════════════════════════════════════════════════════════
    // POOL SETTINGS
    // ════════════════════════════════════════════════════════════
    [Header("Object Pool Configuration")]
    [Tooltip("Prefab with ExplosionHeatDecal component")]
    public GameObject decalPrefab;
    
    [Tooltip("Initial pool size (preallocated on start)")]
    [Range(10, 1000)]
    public int initialPoolSize = 100;
    
    [Tooltip("Maximum pool size (prevents runaway growth)")]
    [Range(50, 2000)]
    public int maxPoolSize = 500;
    
    [Tooltip("Grow pool by this amount when exhausted")]
    [Range(5, 100)]
    public int poolGrowthIncrement = 20;
    
    [Tooltip("Parent transform for pooled objects")]
    public Transform poolContainer;
    
    [Tooltip("Auto-grow pool when running low")]
    public bool autoGrowPool = true;
    
    // ════════════════════════════════════════════════════════════
    // ANIMATION SETTINGS
    // ════════════════════════════════════════════════════════════
    [Header("Heat Buildup Animation")]
    [Tooltip("Time for heat to reach peak (quick ease-in)")]
    [Range(0.05f, 3f)]
    public float defaultBuildupDuration = 0.3f;
    
    [Tooltip("Buildup curve (quick ease-in)")]
    public AnimationCurve defaultBuildupCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    [Header("Heat Decay Animation")]
    [Tooltip("Time for heat to fully decay")]
    [Range(1f, 30f)]
    public float defaultDecayDuration = 5f;
    
    [Tooltip("Decay curve (slower ease-out)")]
    public AnimationCurve defaultDecayCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    
    // ════════════════════════════════════════════════════════════
    // EXPLOSION SETTINGS
    // ════════════════════════════════════════════════════════════
    [Header("Default Explosion Properties")]
    [Range(0.1f, 20f)]
    public float defaultSize = 5f;
    
    [Range(0f, 1f)]
    public float defaultHeat = 0.8f;
    
    [Header("Ground Detection")]
    [Tooltip("Auto-detect ground surface")]
    public bool autoDetectGround = true;
    
    [Tooltip("Raycast distance for ground detection")]
    [Range(0.1f, 50f)]
    public float raycastDistance = 10f;
    
    [Tooltip("Layers to consider as ground")]
    public LayerMask groundLayers = ~0;
    
    [Header("Visual Settings")]
    [Tooltip("Optional heat pattern texture")]
    public Texture2D defaultHeatTexture;
    
    // ════════════════════════════════════════════════════════════
    // PERFORMANCE OPTIMIZATION
    // ════════════════════════════════════════════════════════════
    [Header("Performance Settings")]
    [Tooltip("Limit spawns per frame to prevent lag spikes")]
    public bool limitSpawnsPerFrame = true;
    
    [Tooltip("Maximum decals to spawn in a single frame")]
    [Range(1, 100)]
    public int maxSpawnsPerFrame = 20;
    
    [Tooltip("Maximum active decals (oldest auto-removed)")]
    [Range(10, 1000)]
    public int maxActiveDecals = 300;
    
    [Tooltip("Merge decals closer than this distance")]
    [Range(0f, 5f)]
    public float mergingDistance = 0.5f;
    
    [Tooltip("Enable spatial merging to reduce overdraw")]
    public bool enableSpatialMerging = true;
    
    // ════════════════════════════════════════════════════════════
    // DEBUG & MONITORING
    // ════════════════════════════════════════════════════════════
    [Header("Debug")]
    public bool showDebugStats = true;
    public bool showWarnings = true;
    
    // ════════════════════════════════════════════════════════════
    // RUNTIME STATE
    // ════════════════════════════════════════════════════════════
    private Stack<ExplosionHeatDecal> pooledDecals = new Stack<ExplosionHeatDecal>();
    private LinkedList<ExplosionHeatDecal> activeDecals = new LinkedList<ExplosionHeatDecal>();
    private Dictionary<ExplosionHeatDecal, LinkedListNode<ExplosionHeatDecal>> activeDecalNodes = 
        new Dictionary<ExplosionHeatDecal, LinkedListNode<ExplosionHeatDecal>>();
    
    // Frame limiting
    private int spawnsThisFrame = 0;
    private Queue<SpawnRequest> pendingSpawns = new Queue<SpawnRequest>();
    
    // Stats
    private int totalSpawned = 0;
    private int poolHits = 0;
    private int poolMisses = 0;
    private int totalGrowths = 0;
    private int mergedDecals = 0;
    private int forcedRemovals = 0;
    
    // Cached values
    private float mergingDistanceSqr;
    
    // ════════════════════════════════════════════════════════════
    // SPAWN REQUEST STRUCT (prevents GC allocations)
    // ════════════════════════════════════════════════════════════
    private struct SpawnRequest
    {
        public Vector3 position;
        public Vector3 normal;
        public float size;
        public float heat;
        public float buildupDuration;
        public float decayDuration;
        public bool useNormal;
    }
    
    // ════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ════════════════════════════════════════════════════════════
    
    void Start()
    {
        // Cache squared distance for faster comparisons
        mergingDistanceSqr = mergingDistance * mergingDistance;
    }
    
    void InitializePool()
    {
        if (decalPrefab == null)
        {
            Debug.LogError("[ExplosionHeatManager] No decal prefab assigned! System disabled.");
            enabled = false;
            return;
        }
        
        // Validate prefab
        if (decalPrefab.GetComponent<ExplosionHeatDecal>() == null)
        {
            Debug.LogError("[ExplosionHeatManager] Decal prefab missing ExplosionHeatDecal component!");
            enabled = false;
            return;
        }
        
        // Create container if needed
        if (poolContainer == null)
        {
            GameObject container = new GameObject("_DecalPool");
            container.transform.SetParent(transform);
            poolContainer = container.transform;
        }
        
        // Pre-warm pool
        int prewarmed = 0;
        for (int i = 0; i < initialPoolSize; i++)
        {
            if (CreateNewDecal())
                prewarmed++;
        }
        
        Debug.Log($"[ExplosionHeatManager] Initialized pool with {prewarmed}/{initialPoolSize} decals");
        
        if (prewarmed < initialPoolSize)
        {
            Debug.LogWarning($"[ExplosionHeatManager] Only created {prewarmed} of {initialPoolSize} requested decals");
        }
    }
    
    // ════════════════════════════════════════════════════════════
    // POOL MANAGEMENT
    // ════════════════════════════════════════════════════════════
    
    private bool CreateNewDecal()
    {
        // Check pool size limit
        if (pooledDecals.Count + activeDecals.Count >= maxPoolSize)
        {
            if (showWarnings)
            {
                Debug.LogWarning($"[ExplosionHeatManager] Pool at maximum size ({maxPoolSize})");
            }
            return false;
        }
        
        GameObject obj = Instantiate(decalPrefab, poolContainer);
        obj.SetActive(false);
        
        ExplosionHeatDecal decal = obj.GetComponent<ExplosionHeatDecal>();
        if (decal == null)
        {
            Debug.LogError("[ExplosionHeatManager] Decal prefab missing ExplosionHeatDecal component!");
            Destroy(obj);
            return false;
        }
        
        pooledDecals.Push(decal);
        return true;
    }
    
    private void GrowPool()
    {
        if (!autoGrowPool) return;
        
        int created = 0;
        for (int i = 0; i < poolGrowthIncrement; i++)
        {
            if (CreateNewDecal())
                created++;
            else
                break; // Hit max pool size
        }
        
        if (created > 0)
        {
            totalGrowths++;
            if (showDebugStats)
            {
                Debug.Log($"[ExplosionHeatManager] Pool grown by {created} decals (growth #{totalGrowths})");
            }
        }
    }
    
    private ExplosionHeatDecal GetDecalFromPool()
    {
        ExplosionHeatDecal decal;
        
        if (pooledDecals.Count > 0)
        {
            decal = pooledDecals.Pop();
            poolHits++;
        }
        else
        {
            // Pool exhausted - try to grow
            GrowPool();
            
            if (pooledDecals.Count > 0)
            {
                decal = pooledDecals.Pop();
            }
            else
            {
                // Still no decals - force remove oldest active decal
                if (activeDecals.Count > 0)
                {
                    var oldest = activeDecals.First.Value;
                    if (showWarnings)
                    {
                        Debug.LogWarning("[ExplosionHeatManager] Pool exhausted - recycling oldest decal");
                    }
                    oldest.ForceReturnToPool();
                    forcedRemovals++;
                    
                    if (pooledDecals.Count > 0)
                    {
                        decal = pooledDecals.Pop();
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            
            poolMisses++;
        }
        
        return decal;
    }
    
    public void ReturnDecalToPool(ExplosionHeatDecal decal)
    {
        if (decal == null) return;
        
        // Remove from active tracking
        if (activeDecalNodes.TryGetValue(decal, out var node))
        {
            activeDecals.Remove(node);
            activeDecalNodes.Remove(decal);
        }
        
        // Parent back to pool container
        decal.transform.SetParent(poolContainer);
        
        // Return to pool (using Stack for LIFO - better cache locality)
        pooledDecals.Push(decal);
    }
    
    // ════════════════════════════════════════════════════════════
    // PUBLIC API - SPAWN EXPLOSIONS
    // ════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Spawn explosion with default settings
    /// </summary>
    public void SpawnExplosion(Vector3 position)
    {
        SpawnExplosion(position, defaultSize, defaultHeat, defaultBuildupDuration, defaultDecayDuration);
    }
    
    /// <summary>
    /// Spawn explosion with custom parameters
    /// </summary>
    public void SpawnExplosion(Vector3 position, float size, float heat = -1f, 
                              float buildupDuration = -1f, float decayDuration = -1f)
    {
        if (heat < 0f) heat = defaultHeat;
        if (buildupDuration < 0f) buildupDuration = defaultBuildupDuration;
        if (decayDuration < 0f) decayDuration = defaultDecayDuration;
        
        SpawnRequest request = new SpawnRequest
        {
            position = position,
            normal = Vector3.up,
            size = size,
            heat = heat,
            buildupDuration = buildupDuration,
            decayDuration = decayDuration,
            useNormal = false
        };
        
        QueueSpawn(request);
    }
    
    /// <summary>
    /// Spawn explosion with explicit normal (no raycast)
    /// </summary>
    public void SpawnExplosionWithNormal(Vector3 position, Vector3 normal, float size, 
                                        float heat = -1f, float buildupDuration = -1f, float decayDuration = -1f)
    {
        if (heat < 0f) heat = defaultHeat;
        if (buildupDuration < 0f) buildupDuration = defaultBuildupDuration;
        if (decayDuration < 0f) decayDuration = defaultDecayDuration;
        
        SpawnRequest request = new SpawnRequest
        {
            position = position,
            normal = normal,
            size = size,
            heat = heat,
            buildupDuration = buildupDuration,
            decayDuration = decayDuration,
            useNormal = true
        };
        
        QueueSpawn(request);
    }
    
    // ════════════════════════════════════════════════════════════
    // SPAWN QUEUE SYSTEM (prevents frame spikes)
    // ════════════════════════════════════════════════════════════
    
    private void QueueSpawn(SpawnRequest request)
    {
        if (limitSpawnsPerFrame && spawnsThisFrame >= maxSpawnsPerFrame)
        {
            // Queue for next frame
            pendingSpawns.Enqueue(request);
        }
        else
        {
            // Spawn immediately
            ProcessSpawnRequest(request);
        }
    }
    
    void LateUpdate()
    {
        // Reset frame counter
        spawnsThisFrame = 0;
        
        // Process pending spawns
        while (pendingSpawns.Count > 0 && spawnsThisFrame < maxSpawnsPerFrame)
        {
            var request = pendingSpawns.Dequeue();
            ProcessSpawnRequest(request);
        }
    }
    
    private void ProcessSpawnRequest(SpawnRequest request)
    {
        Vector3 spawnPos = request.position;
        Vector3 normal = request.normal;
        
        // Auto-detect ground if needed
        if (!request.useNormal && autoDetectGround)
        {
            RaycastHit hit;
            if (Physics.Raycast(request.position + Vector3.up * 0.5f, Vector3.down, 
                               out hit, raycastDistance, groundLayers))
            {
                spawnPos = hit.point + hit.normal * 0.01f;
                normal = hit.normal;
            }
        }
        
        // Check for spatial merging
        if (enableSpatialMerging && ShouldMergeWithExisting(spawnPos, request.size))
        {
            mergedDecals++;
            return; // Skip spawning - too close to existing decal
        }
        
        // Enforce active decal limit
        if (activeDecals.Count >= maxActiveDecals)
        {
            // Remove oldest decal
            var oldest = activeDecals.First.Value;
            oldest.ForceReturnToPool();
            forcedRemovals++;
        }
        
        SpawnDecalInternal(spawnPos, normal, request.size, request.heat, 
                          request.buildupDuration, request.decayDuration);
        
        spawnsThisFrame++;
    }
    
    // ════════════════════════════════════════════════════════════
    // SPATIAL MERGING (reduces overdraw)
    // ════════════════════════════════════════════════════════════
    
    private bool ShouldMergeWithExisting(Vector3 position, float size)
    {
        if (!enableSpatialMerging || mergingDistance <= 0f)
            return false;
        
        // Check against recently spawned decals
        int checkCount = Mathf.Min(activeDecals.Count, 10); // Only check last 10 for performance
        var node = activeDecals.Last;
        
        for (int i = 0; i < checkCount && node != null; i++)
        {
            var decal = node.Value;
            float distSqr = (decal.transform.position - position).sqrMagnitude;
            
            if (distSqr < mergingDistanceSqr)
            {
                return true; // Too close - merge by skipping spawn
            }
            
            node = node.Previous;
        }
        
        return false;
    }
    
    // ════════════════════════════════════════════════════════════
    // INTERNAL SPAWN LOGIC
    // ════════════════════════════════════════════════════════════
    
    private void SpawnDecalInternal(Vector3 position, Vector3 normal, float size, 
                                   float heat, float buildupDuration, float decayDuration)
    {
        // Get decal from pool
        ExplosionHeatDecal decal = GetDecalFromPool();
        if (decal == null)
        {
            if (showWarnings)
            {
                Debug.LogWarning("[ExplosionHeatManager] Failed to get decal from pool!");
            }
            return;
        }
        
        // Move to world
        decal.transform.SetParent(null);
        
        // Calculate rotation (cube faces normal)
        Quaternion rotation = Quaternion.LookRotation(normal);
        
        // Initialize decal with buildup and decay settings
        decal.Initialize(position, rotation, size, heat, 
                        buildupDuration, decayDuration,
                        defaultBuildupCurve, defaultDecayCurve, 
                        defaultHeatTexture);
        
        // Track as active (add to end of list)
        var node = activeDecals.AddLast(decal);
        activeDecalNodes[decal] = node;
        
        totalSpawned++;
    }
    
    // ════════════════════════════════════════════════════════════
    // UTILITY & CLEANUP
    // ════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Clear all active decals
    /// </summary>
    public void ClearAllDecals()
    {
        // Create temporary list to avoid modification during iteration
        List<ExplosionHeatDecal> toReturn = new List<ExplosionHeatDecal>(activeDecals);
        
        foreach (var decal in toReturn)
        {
            if (decal != null)
            {
                decal.ForceReturnToPool();
            }
        }
        
        activeDecals.Clear();
        activeDecalNodes.Clear();
        pendingSpawns.Clear();
        
        if (showDebugStats)
        {
            Debug.Log("[ExplosionHeatManager] All decals cleared");
        }
    }
    
    /// <summary>
    /// Get current pool statistics
    /// </summary>
    public PoolStats GetPoolStats()
    {
        return new PoolStats
        {
            activeDecals = activeDecals.Count,
            pooledDecals = pooledDecals.Count,
            totalDecals = activeDecals.Count + pooledDecals.Count,
            totalSpawned = totalSpawned,
            poolHits = poolHits,
            poolMisses = poolMisses,
            poolGrowths = totalGrowths,
            mergedDecals = mergedDecals,
            forcedRemovals = forcedRemovals,
            pendingSpawns = pendingSpawns.Count,
            hitRate = poolHits > 0 ? (float)poolHits / (poolHits + poolMisses) : 0f
        };
    }
    
    public struct PoolStats
    {
        public int activeDecals;
        public int pooledDecals;
        public int totalDecals;
        public int totalSpawned;
        public int poolHits;
        public int poolMisses;
        public int poolGrowths;
        public int mergedDecals;
        public int forcedRemovals;
        public int pendingSpawns;
        public float hitRate;
    }
    
    // ════════════════════════════════════════════════════════════
    // DEBUG VISUALIZATION
    // ════════════════════════════════════════════════════════════
    
    void OnGUI()
    {
        if (!showDebugStats) return;
        
        var stats = GetPoolStats();
        
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 12;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.UpperLeft;
        style.padding = new RectOffset(10, 10, 10, 10);
        
        string debugText = 
            "<b>EXPLOSION HEAT MANAGER - POOL STATS</b>\n" +
            $"Active Decals: {stats.activeDecals} / {maxActiveDecals}\n" +
            $"Pooled Decals: {stats.pooledDecals}\n" +
            $"Total Decals: {stats.totalDecals} / {maxPoolSize}\n" +
            $"Pending Spawns: {stats.pendingSpawns}\n" +
            $"\n" +
            $"<b>Lifetime Stats:</b>\n" +
            $"Total Spawned: {stats.totalSpawned}\n" +
            $"Pool Hit Rate: {stats.hitRate:P1}\n" +
            $"Pool Growths: {stats.poolGrowths}\n" +
            $"Merged Decals: {stats.mergedDecals}\n" +
            $"Forced Removals: {stats.forcedRemovals}\n";
        
        GUI.Box(new Rect(10, Screen.height - 240, 300, 230), debugText, style);
    }
    
    void OnDestroy()
    {
        if (showDebugStats)
        {
            var stats = GetPoolStats();
            Debug.Log($"[ExplosionHeatManager] Final Stats - Total Spawned: {stats.totalSpawned}, " +
                     $"Hit Rate: {stats.hitRate:P1}, Growths: {stats.poolGrowths}");
        }
    }
}