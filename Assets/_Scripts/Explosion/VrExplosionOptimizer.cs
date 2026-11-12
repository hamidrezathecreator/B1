using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Quest / VR pass over a DAVFX-style explosion: turns off the modules that cost the most
/// (collision, noise, lights, heat distortion), caps particle counts, and trims linger cost.
/// Call <see cref="Optimize"/> once before the explosion is switched on.
/// Destroy is scheduled from the moment it appears, not from scene Start.
/// </summary>
public sealed class VrExplosionOptimizer : MonoBehaviour
{
    [Header("What to cut")]
    [Tooltip("Collision against the world every frame — very expensive on Quest.")]
    [SerializeField] bool disableCollision = true;
    [Tooltip("Per-particle noise. Softens motion; huge CPU cost.")]
    [SerializeField] bool disableNoise = true;
    [Tooltip("Dynamic lights from particles.")]
    [SerializeField] bool disableLights = true;
    [Tooltip("Heat / bubble distortion (fullscreen-style cost on URP).")]
    [SerializeField] bool disableDistortion = true;
    [Tooltip("Drops Smoke Trail / Smoke Front. Keeps Raising Smoke + Smoke Longer for a cheap linger.")]
    [SerializeField] bool disableHeavySmoke = true;

    [Header("Caps")]
    [Tooltip("Max live particles per child system.")]
    [Min(1)] [SerializeField] int maxParticlesPerSystem = 56;
    [Tooltip("Burst and emission rates are multiplied by this.")]
    [Range(0.05f, 1f)] [SerializeField] float emissionScale = 0.55f;
    [Tooltip("Particle lifetimes are multiplied by this. Smoke linger systems keep a longer life.")]
    [Range(0.2f, 1.5f)] [SerializeField] float lifetimeScale = 0.95f;
    [Tooltip("Uniform scale of the whole explosion. 1 = leave as placed.")]
    [Range(0.2f, 1f)] [SerializeField] float sizeScale = 0.9f;

    [Header("Timing")]
    [Tooltip("Seconds at full particle speed before slowing (overrides ExplosionSlowAfterBurst).")]
    [Min(0.1f)] [SerializeField] float fullSpeedSeconds = 2f;
    [Min(0.05f)] [SerializeField] float slowSpeedMultiplier = 0.2f;
    [Min(0.05f)] [SerializeField] float slowdownBlendSeconds = 1.25f;
    [Tooltip("Hard stop after the explosion is switched on (not from scene Start).")]
    [Min(1f)] [SerializeField] float destroyAfterSeconds = 14f;

    bool optimized;
    bool destroyScheduled;

    /// <summary>Adds the optimizer once and runs the cheap pass (safe while still inactive).</summary>
    public static VrExplosionOptimizer Apply(GameObject go)
    {
        if (go == null) return null;
        var opt = go.GetComponent<VrExplosionOptimizer>();
        if (opt == null) opt = go.AddComponent<VrExplosionOptimizer>();
        opt.Optimize();
        return opt;
    }

    [ContextMenu("Optimize Now")]
    public void Optimize()
    {
        if (optimized) return;
        optimized = true;

        if (sizeScale < 0.999f)
            transform.localScale *= sizeScale;

        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            OptimizeSystem(ps);

        foreach (var r in GetComponentsInChildren<ParticleSystemRenderer>(true))
            OptimizeRenderer(r);

        if (disableDistortion || disableHeavySmoke || disableLights)
            CullHeavyChildren();

        var slow = GetComponent<ExplosionSlowAfterBurst>();
        if (slow != null) slow.Configure(fullSpeedSeconds, slowSpeedMultiplier, slowdownBlendSeconds);

        // Do NOT Destroy here — Timeline may enable this object minutes after Start.
        if (isActiveAndEnabled)
            ScheduleDestroyFromNow();
    }

    void OnEnable()
    {
        // Timeline / cue just turned the blast on: restart particles and start the lifetime clock.
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (!ps.gameObject.activeInHierarchy) continue;
            ps.Clear(true);
            ps.Play(true);
        }
        ScheduleDestroyFromNow();
    }

    void ScheduleDestroyFromNow()
    {
        if (destroyScheduled) return;
        destroyScheduled = true;
        Destroy(gameObject, destroyAfterSeconds);
    }

    void OptimizeSystem(ParticleSystem ps)
    {
        var n = ps.gameObject.name.ToLowerInvariant();
        var isLingerSmoke = n.Contains("raising smoke") || n.Contains("smoke longer");
        var isFlash = n.Contains("flash") || n.Contains("shockwave") || n == "particles";

        var main = ps.main;
        var particleCap = isLingerSmoke
            ? Mathf.Min(maxParticlesPerSystem, 32)
            : maxParticlesPerSystem;
        main.maxParticles = Mathf.Min(main.maxParticles, particleCap);
        main.loop = false;
        // Flash must keep simulating while the player falls; linger smoke can pause off-screen.
        main.cullingMode = isFlash
            ? ParticleSystemCullingMode.AlwaysSimulate
            : ParticleSystemCullingMode.PauseAndCatchup;

        var lifeMul = isLingerSmoke ? Mathf.Max(lifetimeScale, 1.15f) : lifetimeScale;
        if (Mathf.Abs(lifeMul - 1f) > 0.001f)
        {
            var life = main.startLifetime;
            ScaleMinMaxCurve(ref life, lifeMul);
            main.startLifetime = life;
        }

        if (disableCollision)
        {
            var col = ps.collision;
            col.enabled = false;
        }
        if (disableNoise)
        {
            var noise = ps.noise;
            noise.enabled = false;
        }
        if (disableLights)
        {
            var lights = ps.lights;
            lights.enabled = false;
        }

        var trails = ps.trails;
        trails.enabled = false;

        var emission = ps.emission;
        var emitMul = isLingerSmoke ? Mathf.Min(emissionScale, 0.45f) : emissionScale;
        if (emitMul < 0.999f)
        {
            var rate = emission.rateOverTime;
            ScaleMinMaxCurve(ref rate, emitMul);
            emission.rateOverTime = rate;
            var rateDist = emission.rateOverDistance;
            ScaleMinMaxCurve(ref rateDist, emitMul);
            emission.rateOverDistance = rateDist;

            var burstCount = emission.burstCount;
            if (burstCount > 0)
            {
                var bursts = new ParticleSystem.Burst[burstCount];
                emission.GetBursts(bursts);
                for (var i = 0; i < bursts.Length; i++)
                {
                    var count = bursts[i].count;
                    ScaleMinMaxCurve(ref count, emitMul);
                    if (count.mode == ParticleSystemCurveMode.Constant)
                        count.constant = Mathf.Max(1f, Mathf.Round(count.constant));
                    bursts[i].count = count;
                }
                emission.SetBursts(bursts);
            }
        }
    }

    static void OptimizeRenderer(ParticleSystemRenderer r)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        r.allowRoll = false;
        r.forceRenderingOff = false;
        if (r.renderMode == ParticleSystemRenderMode.Mesh)
            r.enableGPUInstancing = true;
    }

    void CullHeavyChildren()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            var n = t.name.ToLowerInvariant();
            var drop =
                (disableDistortion && (n.Contains("distort") || n.Contains("distorsion") || n.Contains("bubble"))) ||
                (disableLights && (n == "light" || n.EndsWith(" light"))) ||
                // Keep Raising Smoke + Smoke Longer; drop the denser side plumes.
                (disableHeavySmoke && (n.Contains("smoke trail") || n.Contains("smoke front")));
            if (drop) t.gameObject.SetActive(false);
        }
    }

    static void ScaleMinMaxCurve(ref ParticleSystem.MinMaxCurve curve, float scale)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                curve.constant *= scale;
                break;
            case ParticleSystemCurveMode.TwoConstants:
                curve.constantMin *= scale;
                curve.constantMax *= scale;
                break;
            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                curve.curveMultiplier *= scale;
                break;
        }
    }
}
