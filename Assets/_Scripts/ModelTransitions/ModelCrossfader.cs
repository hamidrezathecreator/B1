using System.Collections.Generic;
using UnityEngine;

/// <summary>Crossfades the renderers under two separate model roots. Keep this on their parent.</summary>
[DisallowMultipleComponent]
public sealed class ModelCrossfader : MonoBehaviour
{
    [Header("Models")]
    [SerializeField] private GameObject first;
    [SerializeField] private GameObject second;
    [SerializeField] private bool firstStartsVisible = true;

    [Header("Transition (seconds, per pair)")]
    [Min(0f)] [SerializeField] private float fadeDuration = 0.5f;
    [Min(0f)] [SerializeField] private float randomDelayMin;
    [Min(0f)] [SerializeField] private float randomDelayMax = 0.5f;
    [SerializeField] private bool useUnscaledTime;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    private sealed class RendererState
    {
        public Renderer renderer;
        public Material[] originals;
        public Material[] instances;
        public Color[] colors;
        public int[] colorProperties;
    }

    private readonly List<RendererState> firstRenderers = new List<RendererState>();
    private readonly List<RendererState> secondRenderers = new List<RendererState>();
    private bool initialized;
    private bool targetFirst;
    private bool transitioning;
    private float blend;
    private float startBlend;
    private float delayRemaining;
    private float elapsed;

    public GameObject First => first;
    public GameObject Second => second;
    public GameObject CurrentModel => (initialized ? blend < 0.5f : firstStartsVisible) ? first : second;

    private void Awake() => Initialize();

    private bool Initialize()
    {
        if (initialized) return true;
        if (!Application.isPlaying) return false;
        // Deactivating either root must never deactivate this component or the other root.
        if (first == null || second == null || first == second ||
            transform.IsChildOf(first.transform) || transform.IsChildOf(second.transform) ||
            first.transform.IsChildOf(second.transform) || second.transform.IsChildOf(first.transform))
        {
            Debug.LogWarning("ModelCrossfader needs two separate model roots. Place the component outside both roots (usually on their parent).", this);
            return false;
        }

        CacheRenderers(first, firstRenderers);
        CacheRenderers(second, secondRenderers);
        targetFirst = firstStartsVisible;
        blend = targetFirst ? 0f : 1f;
        initialized = true;
        ApplyBlend();
        return true;
    }

    private void CacheRenderers(GameObject root, List<RendererState> states)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            Material[] originals = renderer.sharedMaterials;
            var state = new RendererState
            {
                renderer = renderer,
                originals = originals,
                instances = new Material[originals.Length],
                colors = new Color[originals.Length],
                colorProperties = new int[originals.Length]
            };
            for (int i = 0; i < originals.Length; i++)
            {
                Material source = originals[i];
                if (source == null) continue;
                int property = source.HasProperty(BaseColor) ? BaseColor : ColorProperty;
                if (!source.HasProperty(property))
                {
                    state.instances[i] = source;
                    Debug.LogWarning($"ModelCrossfader: material '{source.name}' has no _BaseColor or _Color. Use a shader with alpha transparency to fade this mesh.", renderer);
                    continue;
                }
                state.instances[i] = new Material(source) { name = source.name + " (ModelCrossfader)", hideFlags = HideFlags.DontSave };
                state.colors[i] = source.GetColor(property);
                state.colorProperties[i] = property;
                if (source.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent)
                    Debug.LogWarning($"ModelCrossfader: set material '{source.name}' to Transparent / Alpha for smooth fading. Opaque materials ignore alpha.", renderer);
            }
            renderer.sharedMaterials = state.instances;
            states.Add(state);
        }
    }

    /// <summary>Reverses the requested state, including during a delay or an unfinished fade.</summary>
    public void Crossfade()
    {
        if (Initialize()) CrossfadeTo(!targetFirst);
    }

    public void CrossfadeToFirst() => CrossfadeTo(true);
    public void CrossfadeToSecond() => CrossfadeTo(false);

    public void CrossfadeTo(GameObject model)
    {
        if (model == null) return;
        if (model == first) CrossfadeToFirst();
        else if (model == second) CrossfadeToSecond();
    }

    public bool ContainsModel(GameObject model) => model != null && (model == first || model == second);

    private void CrossfadeTo(bool showFirst)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !Initialize()) return;
        if (targetFirst == showFirst && (transitioning || blend == (showFirst ? 0f : 1f))) return;
        targetFirst = showFirst;
        startBlend = blend;
        elapsed = 0f;
        delayRemaining = Random.Range(Mathf.Max(0f, Mathf.Min(randomDelayMin, randomDelayMax)),
            Mathf.Max(0f, Mathf.Max(randomDelayMin, randomDelayMax)));
        transitioning = true;
        if (delayRemaining == 0f && fadeDuration <= 0f) Finish();
    }

    private void Update()
    {
        if (!transitioning) return;
        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (delayRemaining > 0f)
        {
            delayRemaining -= delta;
            if (delayRemaining > 0f) return;
            delta = -delayRemaining;
        }
        elapsed += delta;
        if (fadeDuration <= 0f || elapsed >= fadeDuration)
        {
            Finish();
            return;
        }
        blend = Mathf.Lerp(startBlend, targetFirst ? 0f : 1f, elapsed / fadeDuration);
        ApplyBlend();
    }

    /// <summary>Immediately returns to the configured starting model, cancelling any pending fade.</summary>
    public void ResetToInitial()
    {
        if (!Application.isPlaying || !Initialize()) return;
        targetFirst = firstStartsVisible;
        Finish();
    }

    private void Finish()
    {
        transitioning = false;
        blend = targetFirst ? 0f : 1f;
        ApplyBlend();
    }

    private void ApplyBlend()
    {
        ApplyOpacity(firstRenderers, 1f - blend);
        ApplyOpacity(secondRenderers, blend);
        if (first != null) first.SetActive(blend < 1f);
        if (second != null) second.SetActive(blend > 0f);
    }

    private static void ApplyOpacity(List<RendererState> states, float opacity)
    {
        foreach (RendererState state in states)
        {
            for (int i = 0; i < state.instances.Length; i++)
            {
                if (state.instances[i] == null || state.colorProperties[i] == 0) continue;
                Color color = state.colors[i];
                color.a *= opacity;
                state.instances[i].SetColor(state.colorProperties[i], color);
            }
        }
    }

    private void OnDisable()
    {
        // A disabled pair settles at its requested endpoint, so re-enabling cannot leave it half faded.
        if (initialized && transitioning) Finish();
    }

    private void OnDestroy()
    {
        ReleaseMaterials(firstRenderers);
        ReleaseMaterials(secondRenderers);
    }

    private static void ReleaseMaterials(List<RendererState> states)
    {
        foreach (RendererState state in states)
        {
            if (state.renderer != null) state.renderer.sharedMaterials = state.originals;
            for (int i = 0; i < state.instances.Length; i++)
                if (state.instances[i] != null && state.instances[i] != state.originals[i]) Destroy(state.instances[i]);
        }
    }
}
