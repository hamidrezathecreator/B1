using System.Collections.Generic;
using UnityEngine;

/// <summary>Fades meshes out, switches their material, then fades them back in.</summary>
[DisallowMultipleComponent]
public sealed class SpriteCrossfadeController : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private List<MeshRenderer> renderers = new List<MeshRenderer>();
    [SerializeField] private Material targetMaterial;

    [Header("Transition")]
    [Tooltip("Seconds for each half: fade out, then fade in.")]
    [Min(0f)] [SerializeField] private float fadeDuration = 0.2f;
    [Min(0f)] [SerializeField] private float randomDelayMin;
    [Min(0f)] [SerializeField] private float randomDelayMax;
    [SerializeField] private bool useUnscaledTime;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private readonly List<Transition> transitions = new List<Transition>();

    private sealed class Transition
    {
        public MeshRenderer renderer;
        public Material[] sources;
        public Material[] targets;
        public Material[] instances;
        public Color[] colors;
        public int[] properties;
        public float delay;
        public float elapsed;
        public float duration;
        public bool switched;
    }

    public IReadOnlyList<MeshRenderer> Renderers => renderers;
    public Material TargetMaterial { get => targetMaterial; set => targetMaterial = value; }

    private void Reset() => RefreshRenderers();

    [ContextMenu("Refresh Mesh Renderers")]
    public void RefreshRenderers()
    {
        renderers.Clear();
        renderers.AddRange(GetComponentsInChildren<MeshRenderer>(true));
    }

    /// <summary>Timeline/UnityEvent entry point. Each mesh receives its own random delay.</summary>
    public void BroadcastCrossfade()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || targetMaterial == null) return;
        if (ColorPropertyFor(targetMaterial) == 0)
        {
            Debug.LogWarning("SpriteCrossfadeController: target material needs a _BaseColor or _Color property with alpha transparency.", this);
            return;
        }

        CancelTransitions();
        float minDelay = Mathf.Max(0f, Mathf.Min(randomDelayMin, randomDelayMax));
        float maxDelay = Mathf.Max(minDelay, Mathf.Max(randomDelayMin, randomDelayMax));
        var seen = new HashSet<MeshRenderer>();
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer == null || !seen.Add(renderer)) continue;
            Material[] sources = renderer.sharedMaterials;
            if (sources.Length == 0) continue;
            bool supported = true;
            foreach (Material source in sources)
                if (source == null || ColorPropertyFor(source) == 0) supported = false;
            if (!supported)
            {
                Debug.LogWarning("SpriteCrossfadeController: mesh materials need a _BaseColor or _Color property with alpha transparency.", renderer);
                continue;
            }

            var state = new Transition
            {
                renderer = renderer,
                sources = sources,
                targets = new Material[sources.Length],
                delay = Random.Range(minDelay, maxDelay),
                duration = Mathf.Max(0f, fadeDuration)
            };
            for (int i = 0; i < state.targets.Length; i++) state.targets[i] = targetMaterial;
            transitions.Add(state);
        }
        // Zero duration and zero delay switch in the same call.
        AdvanceTransitions(0f);
    }

    private void Update() => AdvanceTransitions(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

    private void AdvanceTransitions(float delta)
    {
        for (int i = transitions.Count - 1; i >= 0; i--)
        {
            Transition state = transitions[i];
            state.elapsed += delta;
            float time = state.elapsed - state.delay;
            if (state.renderer == null)
            {
                Release(state);
                transitions.RemoveAt(i);
                continue;
            }
            if (time < 0f) continue;

            if (state.duration <= 0f || time >= state.duration * 2f)
            {
                state.switched = true;
                Release(state);
                transitions.RemoveAt(i);
                continue;
            }

            if (!state.switched && time >= state.duration)
            {
                DestroyInstances(state);
                state.switched = true;
            }
            if (state.instances == null) CreateInstances(state);
            float opacity = state.switched
                ? (time - state.duration) / state.duration
                : 1f - time / state.duration;
            for (int j = 0; j < state.instances.Length; j++)
            {
                Color color = state.colors[j];
                color.a *= Mathf.Clamp01(opacity);
                state.instances[j].SetColor(state.properties[j], color);
            }
        }
    }

    private static int ColorPropertyFor(Material material) =>
        material.HasProperty(BaseColor) ? BaseColor : material.HasProperty(ColorProperty) ? ColorProperty : 0;

    private static void CreateInstances(Transition state)
    {
        Material[] materials = state.switched ? state.targets : state.sources;
        state.instances = new Material[materials.Length];
        state.colors = new Color[materials.Length];
        state.properties = new int[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            state.properties[i] = ColorPropertyFor(materials[i]);
            state.colors[i] = materials[i].GetColor(state.properties[i]);
            state.instances[i] = new Material(materials[i])
            {
                name = materials[i].name + " (Sprite Transition)",
                hideFlags = HideFlags.DontSave
            };
        }
        state.renderer.sharedMaterials = state.instances;
    }

    private static void DestroyInstances(Transition state)
    {
        if (state.instances == null) return;
        foreach (Material instance in state.instances)
            if (instance != null) Destroy(instance);
        state.instances = null;
    }

    private static void Release(Transition state)
    {
        if (state.renderer != null)
            state.renderer.sharedMaterials = state.switched ? state.targets : state.sources;
        DestroyInstances(state);
    }

    private void OnDisable() => CancelTransitions();

    private void CancelTransitions()
    {
        foreach (Transition state in transitions) Release(state);
        transitions.Clear();
    }
}