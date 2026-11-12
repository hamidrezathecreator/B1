using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Quest / VR pass over a DAVFX-style explosion: turns off the modules that cost the most
/// (collision, noise, lights, heat distortion), caps particle counts, and shortens the linger.
/// Call <see cref="Optimize"/> once before the explosion is switched on.
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
    [Tooltip("Long secondary smoke that keeps filling the screen.")]
    [SerializeField] bool disableLongSmoke = true;

    [Header("Caps")]
    [Tooltip("Max live particles per child system.")]
    [Min(1)] [SerializeField] int maxParticlesPerSystem = 40;
    [Tooltip("Burst and emission rates are multiplied by this (0.35 ≈ a third of the original).")]
    [Range(0.05f, 1f)] [SerializeField] float emissionScale = 0.35f;
    [Tooltip("Particle lifetimes are multiplied by this so smoke clears sooner.")]
    [Range(0.2f, 1f)] [SerializeField] float lifetimeScale = 0.55f;
    [Tooltip("Uniform scale of the whole explosion. 1 = leave as placed. 0.6 helps a lot on Quest.")]
    [Range(0.2f, 1f)] [SerializeField] float sizeScale = 0.65f;

    [Header("Timing")]
    [Tooltip("Seconds at full particle speed before slowing (overrides ExplosionSlowAfterBurst).")]
    [Min(0.1f)] [SerializeField] float fullSpeedSeconds = 1f;
    [Min(0.05f)] [SerializeField] float slowSpeedMultiplier = 0.45f;
    [Min(0.05f)] [SerializeField] float slowdownBlendSeconds = 0.4f;
    [Tooltip("Hard stop: stop emitting and destroy after this many seconds.")]
    [Min(1f)] [SerializeField] float destroyAfterSeconds = 4.5f;

    bool done;

    /// <summary>Adds the optimizer once and runs it (works while the object is still switched off).</summary>
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
        if (done) return;
        done = true;

        if (sizeScale < 0.999f)
            transform.localScale *= sizeScale;

        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            OptimizeSystem(ps);

        foreach (var r in GetComponentsInChildren<ParticleSystemRenderer>(true))
            OptimizeRenderer(r);

        if (disableDistortion || disableLongSmoke || disableLights)
            CullHeavyChildren();

        var slow = GetComponent<ExplosionSlowAfterBurst>();
        // Prefab instance had fullSpeedSeconds = 7: that kept every system at full cost for ages.
        if (slow != null) slow.Configure(fullSpeedSeconds, slowSpeedMultiplier, slowdownBlendSeconds);

        // Hard stop so nothing keeps simulating after the player has fallen.
        Destroy(gameObject, destroyAfterSeconds);
    }

    void OptimizeSystem(ParticleSystem ps)
    {
        var main = ps.main;
        main.maxParticles = Mathf.Min(main.maxParticles, maxParticlesPerSystem);
        main.loop = false;
        // Pause when off-screen instead of always simulating.
        main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
        if (lifetimeScale < 0.999f)
        {
            var life = main.startLifetime;
            ScaleMinMaxCurve(ref life, lifetimeScale);
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
        if (emissionScale < 0.999f)
        {
            var rate = emission.rateOverTime;
            ScaleMinMaxCurve(ref rate, emissionScale);
            emission.rateOverTime = rate;
            var rateDist = emission.rateOverDistance;
            ScaleMinMaxCurve(ref rateDist, emissionScale);
            emission.rateOverDistance = rateDist;

            var burstCount = emission.burstCount;
            if (burstCount > 0)
            {
                var bursts = new ParticleSystem.Burst[burstCount];
                emission.GetBursts(bursts);
                for (var i = 0; i < bursts.Length; i++)
                {
                    var count = bursts[i].count;
                    ScaleMinMaxCurve(ref count, emissionScale);
                    // At least one particle so the flash still shows.
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
        // Soft particles sample the depth buffer per pixel — skip on Quest.
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
                (disableLongSmoke && (n.Contains("smoke longer") || n.Contains("smoke trail") ||
                                      n.Contains("raising smoke") || n.Contains("smoke front")));
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
