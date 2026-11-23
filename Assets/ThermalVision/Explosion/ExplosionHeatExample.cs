// ========================================================
// ExplosionHeatExample.cs - Production Test Script
// ========================================================
// Demonstrates optimal usage patterns for high-frequency spawning
// Uses New Input System

using UnityEngine;
using UnityEngine.InputSystem;

public class ExplosionHeatExample : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════
    // INPUT CONFIGURATION
    // ════════════════════════════════════════════════════════════
    [Header("Input Keys (New Input System)")]
    [Tooltip("Keys to spawn different explosion types")]
    public Key smallExplosionKey = Key.Digit1;
    public Key mediumExplosionKey = Key.Digit2;
    public Key largeExplosionKey = Key.Digit3;
    public Key rapidFireKey = Key.Digit4;
    public Key linePatternKey = Key.Digit5;
    public Key circlePatternKey = Key.Digit6;
    public Key stressTestKey = Key.Digit7;
    public Key clearAllKey = Key.C;
    public Key toggleStatsKey = Key.Tab;
    
    [Header("Settings")]
    [Tooltip("Show instructions on screen")]
    public bool showInstructions = true;
    
    [Header("Rapid Fire Settings")]
    [Tooltip("Delay between rapid fire spawns (seconds)")]
    [Range(0.01f, 1f)]
    public float rapidFireDelay = 0.05f;
    
    [Tooltip("Spread radius for rapid fire")]
    [Range(1f, 20f)]
    public float rapidFireSpread = 8f;
    
    // ════════════════════════════════════════════════════════════
    // RUNTIME STATE
    // ════════════════════════════════════════════════════════════
    private float rapidFireTimer = 0f;
    private bool statsVisible = true;
    
    // ════════════════════════════════════════════════════════════
    // UPDATE
    // ════════════════════════════════════════════════════════════
    
    void Update()
    {
        HandleInput();
        
        // Update rapid fire timer
        if (rapidFireTimer > 0f)
        {
            rapidFireTimer -= Time.deltaTime;
        }
    }
    
    void HandleInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        
        // Toggle stats display
        if (keyboard[toggleStatsKey].wasPressedThisFrame)
        {
            statsVisible = !statsVisible;
            if (ExplosionHeatManager.Instance != null)
            {
                ExplosionHeatManager.Instance.showDebugStats = statsVisible;
            }
        }
        
        // ════════════════════════════════════════════════════════
        // SINGLE EXPLOSIONS
        // ════════════════════════════════════════════════════════
        
        // EXAMPLE 1: Small explosion
        if (keyboard[smallExplosionKey].wasPressedThisFrame)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * 5f;
            ExplosionHeatManager.Instance.SpawnExplosion(
                pos, 
                size: 3f, 
                heat: 0.6f, 
                buildupDuration: 0.2f,
                decayDuration: 3f
            );
            Debug.Log($"Spawned SMALL explosion at {pos}");
        }
        
        // EXAMPLE 2: Medium explosion
        if (keyboard[mediumExplosionKey].wasPressedThisFrame)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * 5f;
            ExplosionHeatManager.Instance.SpawnExplosion(
                pos, 
                size: 6f, 
                heat: 0.8f, 
                buildupDuration: 0.3f,
                decayDuration: 5f
            );
            Debug.Log($"Spawned MEDIUM explosion at {pos}");
        }
        
        // EXAMPLE 3: Large explosion
        if (keyboard[largeExplosionKey].wasPressedThisFrame)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * 5f;
            ExplosionHeatManager.Instance.SpawnExplosion(
                pos, 
                size: 10f, 
                heat: 1.0f, 
                buildupDuration: 0.5f,
                decayDuration: 8f
            );
            Debug.Log($"Spawned LARGE explosion at {pos}");
        }
        
        // ════════════════════════════════════════════════════════
        // RAPID FIRE (HIGH FREQUENCY TEST)
        // ════════════════════════════════════════════════════════
        
        // EXAMPLE 4: Rapid fire explosions (respects frame limiting)
        if (keyboard[rapidFireKey].isPressed && rapidFireTimer <= 0f)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * rapidFireSpread;
            ExplosionHeatManager.Instance.SpawnExplosion(
                pos, 
                size: 4f, 
                heat: 0.7f,
                buildupDuration: 0.15f,
                decayDuration: 2f
            );
            rapidFireTimer = rapidFireDelay;
        }
        
        // ════════════════════════════════════════════════════════
        // PATTERN SPAWNS
        // ════════════════════════════════════════════════════════
        
        // EXAMPLE 5: Line of explosions
        if (keyboard[linePatternKey].wasPressedThisFrame)
        {
            SpawnLinePattern();
        }
        
        // EXAMPLE 6: Circle of explosions
        if (keyboard[circlePatternKey].wasPressedThisFrame)
        {
            SpawnCirclePattern();
        }
        
        // EXAMPLE 7: Stress test (many simultaneous spawns)
        if (keyboard[stressTestKey].wasPressedThisFrame)
        {
            SpawnStressTest();
        }
        
        // ════════════════════════════════════════════════════════
        // CLEANUP
        // ════════════════════════════════════════════════════════
        
        // Clear all active decals
        if (keyboard[clearAllKey].wasPressedThisFrame)
        {
            ExplosionHeatManager.Instance.ClearAllDecals();
            Debug.Log("Cleared all heat decals");
        }
    }
    
    // ════════════════════════════════════════════════════════════
    // PATTERN SPAWNING METHODS
    // ════════════════════════════════════════════════════════════
    
    private void SpawnLinePattern()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 pos = transform.position + Vector3.right * i * 2f;
            ExplosionHeatManager.Instance.SpawnExplosion(
                pos, 
                size: 4f, 
                heat: 0.8f,
                buildupDuration: 0.2f + i * 0.05f, // Cascading effect
                decayDuration: 5f
            );
        }
        Debug.Log("Spawned LINE pattern (10 explosions)");
    }
    
    private void SpawnCirclePattern()
    {
        int count = 12;
        float radius = 8f;
        
        for (int i = 0; i < count; i++)
        {
            float angle = (i / (float)count) * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            Vector3 pos = transform.position + offset;
            
            ExplosionHeatManager.Instance.SpawnExplosion(
                pos, 
                size: 5f, 
                heat: 0.9f,
                buildupDuration: 0.3f,
                decayDuration: 6f
            );
        }
        Debug.Log("Spawned CIRCLE pattern (12 explosions)");
    }
    
    private void SpawnStressTest()
    {
        int count = 50;
        Debug.Log($"<color=yellow>STRESS TEST: Spawning {count} explosions...</color>");
        
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * 15f;
            float size = Random.Range(2f, 8f);
            float heat = Random.Range(0.5f, 1f);
            
            ExplosionHeatManager.Instance.SpawnExplosion(
                pos, 
                size: size, 
                heat: heat,
                buildupDuration: Random.Range(0.1f, 0.4f),
                decayDuration: Random.Range(3f, 7f)
            );
        }
        
        Debug.Log("<color=yellow>Stress test complete - check pool stats!</color>");
    }
    
    // ════════════════════════════════════════════════════════════
    // UI
    // ════════════════════════════════════════════════════════════
    
    void OnGUI()
    {
        if (!showInstructions) return;
        
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.UpperLeft;
        style.padding = new RectOffset(10, 10, 5, 5);
        
        string instructions = 
            "<b>EXPLOSION HEAT DECAL TEST - PRODUCTION VERSION</b>\n" +
            "\n" +
            "<b>Single Explosions:</b>\n" +
            $"  [{smallExplosionKey}] Small (3m, 0.6 heat, 3s decay)\n" +
            $"  [{mediumExplosionKey}] Medium (6m, 0.8 heat, 5s decay)\n" +
            $"  [{largeExplosionKey}] Large (10m, 1.0 heat, 8s decay)\n" +
            "\n" +
            "<b>High-Frequency Tests:</b>\n" +
            $"  [{rapidFireKey}] Rapid Fire (HOLD - {1f/rapidFireDelay:F0} per second)\n" +
            $"  [{stressTestKey}] Stress Test (50 simultaneous spawns)\n" +
            "\n" +
            "<b>Patterns:</b>\n" +
            $"  [{linePatternKey}] Line Pattern (10 explosions)\n" +
            $"  [{circlePatternKey}] Circle Pattern (12 explosions)\n" +
            "\n" +
            "<b>Utility:</b>\n" +
            $"  [{clearAllKey}] Clear All Decals\n" +
            $"  [{toggleStatsKey}] Toggle Pool Stats\n" +
            "\n" +
            "<color=yellow>Switch to THERMAL VISION to see heat!</color>";
        
        GUI.Label(new Rect(10, 10, 450, 320), instructions, style);
    }
    
    // ════════════════════════════════════════════════════════════
    // INTEGRATION EXAMPLES FOR YOUR PROJECT
    // ════════════════════════════════════════════════════════════
    
    #region Integration Examples
    
    /// <summary>
    /// Example: Call from your grenade explosion code
    /// PERFORMANCE: Single call, no frame impact
    /// </summary>
    void OnGrenadeExplode(Vector3 position)
    {
        ExplosionHeatManager.Instance.SpawnExplosion(
            position, 
            size: 8f,              // 8 meter radius
            heat: 0.9f,            // Very hot
            buildupDuration: 0.3f, // Quick flash
            decayDuration: 6f      // Lasts 6 seconds
        );
    }
    
    /// <summary>
    /// Example: Call from your bullet impact code
    /// PERFORMANCE: Safe for rapid fire weapons (pooling handles it)
    /// </summary>
    void OnBulletImpact(Vector3 position, Vector3 normal)
    {
        ExplosionHeatManager.Instance.SpawnExplosionWithNormal(
            position, 
            normal,                // Use surface normal
            size: 1f,              // Small decal
            heat: 0.3f,            // Low heat
            buildupDuration: 0.1f, // Instant
            decayDuration: 2f      // Quick fade
        );
    }
    
    /// <summary>
    /// Example: Call from your fire damage code
    /// PERFORMANCE: Can be called every frame for ongoing fire
    /// </summary>
    void OnFireDamageOverTime(Vector3 position)
    {
        // Only spawn every 0.5 seconds to avoid spam
        if (Time.frameCount % 30 == 0) // ~0.5s at 60fps
        {
            ExplosionHeatManager.Instance.SpawnExplosion(
                position,
                size: 5f,
                heat: 0.7f,
                buildupDuration: 0.2f,
                decayDuration: 15f  // Burns for 15 seconds
            );
        }
    }
    
    /// <summary>
    /// Example: Call from vehicle explosion
    /// PERFORMANCE: Multiple overlapping decals for realism
    /// </summary>
    void OnVehicleDestroyed(Vector3 position)
    {
        // Main explosion
        ExplosionHeatManager.Instance.SpawnExplosion(
            position,
            size: 15f,             // Huge radius
            heat: 1.0f,            // Maximum heat
            buildupDuration: 0.4f,
            decayDuration: 10f     // Long-lasting
        );
        
        // Secondary explosions for effect (pooling handles multiple spawns)
        for (int i = 0; i < 3; i++)
        {
            Vector3 offset = Random.insideUnitSphere * 3f;
            ExplosionHeatManager.Instance.SpawnExplosion(
                position + offset,
                size: 8f,
                heat: 0.8f,
                buildupDuration: 0.2f + i * 0.1f,
                decayDuration: 8f
            );
        }
    }
    
    /// <summary>
    /// Example: Artillery barrage (many simultaneous explosions)
    /// PERFORMANCE: Frame limiting prevents lag spikes
    /// </summary>
    void OnArtilleryBarrage(Vector3 center, int shellCount)
    {
        for (int i = 0; i < shellCount; i++)
        {
            Vector3 pos = center + Random.insideUnitSphere * 20f;
            
            // Manager will automatically queue if needed
            ExplosionHeatManager.Instance.SpawnExplosion(
                pos,
                size: Random.Range(6f, 12f),
                heat: Random.Range(0.7f, 1.0f),
                buildupDuration: 0.3f,
                decayDuration: Random.Range(5f, 8f)
            );
        }
        
        Debug.Log($"Spawned {shellCount} artillery shells (queued if needed)");
    }
    
    #endregion
}