using System.Collections;
using UnityEngine;

/// <summary>
/// Holds two SpriteRenderers in the same scene object and crossfades between them.
/// Add this component to the parent object that owns the two renderers.
/// </summary>
public sealed class SpriteCrossfadePair : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private SpriteRenderer first;
    [SerializeField] private SpriteRenderer second;
    [SerializeField] private bool firstStartsVisible = true;

    [Header("Transition")]
    [Min(0f)] [SerializeField] private float fadeDuration = 0.2f;
    [Min(0f)] [SerializeField] private float randomDelayMin;
    [Min(0f)] [SerializeField] private float randomDelayMax;

    private Coroutine transition;
    private bool firstVisible;

    public Sprite CurrentSprite => firstVisible ? first != null ? first.sprite : null : second != null ? second.sprite : null;
    public SpriteRenderer First => first;
    public SpriteRenderer Second => second;

    private void Awake()
    {
        if (first == null || second == null)
        {
            var renderers = GetComponentsInChildren<SpriteRenderer>(true);
            if (first == null && renderers.Length > 0) first = renderers[0];
            if (second == null && renderers.Length > 1) second = renderers[1];
        }

        firstVisible = firstStartsVisible;
        ApplyImmediate(firstVisible);
    }

    public bool ContainsSprite(Sprite sprite)
    {
        return sprite != null && (first != null && first.sprite == sprite || second != null && second.sprite == sprite);
    }

    /// <summary>Starts a delayed crossfade. The delay is randomized per pair.</summary>
    public void Crossfade()
    {
        CrossfadeTo(!firstVisible);
    }

    public void CrossfadeTo(SpriteRenderer target)
    {
        if (target == first) CrossfadeTo(true);
        else if (target == second) CrossfadeTo(false);
    }

    public void CrossfadeTo(Sprite target)
    {
        if (target == null) return;
        if (first != null && first.sprite == target) CrossfadeTo(true);
        else if (second != null && second.sprite == target) CrossfadeTo(false);
    }

    public void CrossfadeToFirst() => CrossfadeTo(true);
    public void CrossfadeToSecond() => CrossfadeTo(false);

    private void CrossfadeTo(bool showFirst)
    {
        if (first == null || second == null || first == second) return;
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(CrossfadeRoutine(showFirst));
    }

    private IEnumerator CrossfadeRoutine(bool showFirst)
    {
        float delay = Random.Range(Mathf.Min(randomDelayMin, randomDelayMax), Mathf.Max(randomDelayMin, randomDelayMax));
        if (delay > 0f) yield return new WaitForSeconds(delay);

        SpriteRenderer incoming = showFirst ? first : second;
        SpriteRenderer outgoing = showFirst ? second : first;
        float duration = Mathf.Max(0f, fadeDuration);
        float incomingStart = incoming.color.a;
        float outgoingStart = outgoing.color.a;
        incoming.gameObject.SetActive(true);

        if (duration <= 0f)
        {
            SetAlpha(incoming, 1f);
            SetAlpha(outgoing, 0f);
        }
        else
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                SetAlpha(incoming, Mathf.Lerp(incomingStart, 1f, t));
                SetAlpha(outgoing, Mathf.Lerp(outgoingStart, 0f, t));
                yield return null;
            }
            SetAlpha(incoming, 1f);
            SetAlpha(outgoing, 0f);
        }

        outgoing.gameObject.SetActive(false);
        incoming.gameObject.SetActive(true);
        firstVisible = showFirst;
        transition = null;
    }

    private void ApplyImmediate(bool showFirst)
    {
        if (first == null || second == null) return;
        SetAlpha(first, showFirst ? 1f : 0f);
        SetAlpha(second, showFirst ? 0f : 1f);
        first.gameObject.SetActive(showFirst);
        second.gameObject.SetActive(!showFirst);
    }

    private static void SetAlpha(SpriteRenderer renderer, float alpha)
    {
        if (renderer == null) return;
        Color color = renderer.color;
        color.a = alpha;
        renderer.color = color;
    }
}
