using UnityEngine;

/// <summary>
/// Built-in launch smoke: a trail that follows the tail of a moving object and a ground
/// cloud at the launch point. Uses Resources/Effects/LaunchSmoke (URP Particles Unlit + the
/// Realistic Smoke flipbook) so it renders in the Quest build.
/// </summary>
public static class LaunchSmoke
{
    const string MaterialPath = "Effects/LaunchSmoke";
    static Material cachedMaterial;

    /// <summary>Looping trail. Simulates in world space so the smoke stays behind as the object moves.</summary>
    public static ParticleSystem CreateTrail(float size)
    {
        var ps = NewSystem("LaunchSmoke Trail");

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * size, 2f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(1.5f * size, 3f * size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.88f, 0.86f, 0.84f, 0.6f), new Color(0.62f, 0.6f, 0.58f, 0.5f));
        main.gravityModifier = -0.03f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = 30f;
        emission.rateOverDistance = 3f / Mathf.Max(0.2f, size);

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.4f * size;

        AddSharedLook(ps, 0.6f, 2.4f, 0.7f);
        ps.Play();
        return ps;
    }

    /// <summary>One-shot cloud spreading over the ground. Destroys itself when done.</summary>
    public static ParticleSystem CreateGroundBurst(Vector3 position, float size)
    {
        var ps = NewSystem("LaunchSmoke Ground");
        ps.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));

        var main = ps.main;
        main.duration = 1.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f * size, 8f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(3f * size, 6f * size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.86f, 0.84f, 0.82f, 0.55f), new Color(0.6f, 0.58f, 0.56f, 0.45f));
        main.gravityModifier = -0.04f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 45),
            new ParticleSystem.Burst(0.35f, 25),
        });

        // Flat ring that throws smoke outward along the ground.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1.2f * size;
        shape.radiusThickness = 1f;

        // Rapid spread that slows down, then drifts up.
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.6f * size;
        limit.dampen = 0.12f;

        AddSharedLook(ps, 0.7f, 2.2f, 0.65f);
        ps.Play();
        return ps;
    }

    /// <summary>Stop emitting and remove the system once the last particle has faded.</summary>
    public static void StopAndRelease(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.transform.SetParent(null, true);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        var life = ps.main.startLifetime;
        Object.Destroy(ps.gameObject, Mathf.Max(life.constantMax, life.constant) + 1f);
    }

    static ParticleSystem NewSystem(string name)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        // AddComponent starts playing with defaults; settings like duration need it stopped.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = SmokeMaterial();
        return ps;
    }

    static void AddSharedLook(ParticleSystem ps, float sizeStart, float sizeEnd, float peakAlpha)
    {
        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, sizeStart), new Keyframe(1f, sizeEnd)));

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.12f), new GradientAlphaKey(peakAlpha * 0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.25f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        // Realistic Smoke texture is a 6 x 6 flipbook.
        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 6;
        sheet.numTilesY = 6;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        sheet.cycleCount = 1;
    }

    static Material SmokeMaterial()
    {
        if (cachedMaterial != null) return cachedMaterial;

        cachedMaterial = Resources.Load<Material>(MaterialPath);
        if (cachedMaterial != null) return cachedMaterial;

        // Fallback without the asset: plain soft particle so nothing renders pink.
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        cachedMaterial = new Material(shader) { name = "LaunchSmokeRuntime" };
        if (cachedMaterial.HasProperty("_BaseColor"))
            cachedMaterial.SetColor("_BaseColor", new Color(0.82f, 0.8f, 0.78f, 1f));
        return cachedMaterial;
    }
}
