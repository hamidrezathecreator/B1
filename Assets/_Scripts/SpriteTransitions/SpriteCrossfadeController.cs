using System.Collections.Generic;
using UnityEngine;

/// <summary>Broadcasts crossfade commands to a collection of SpriteCrossfadePair components.</summary>
public sealed class SpriteCrossfadeController : MonoBehaviour
{
    [SerializeField] private List<SpriteCrossfadePair> pairs = new List<SpriteCrossfadePair>();

    public IReadOnlyList<SpriteCrossfadePair> Pairs => pairs;

    private void Reset() => RefreshPairs();

    [ContextMenu("Refresh Pairs")]
    public void RefreshPairs()
    {
        pairs.Clear();
        pairs.AddRange(GetComponentsInChildren<SpriteCrossfadePair>(true));
    }

    /// <summary>Timeline/UnityEvent entry point: every pair switches to its other sprite.</summary>
    public void BroadcastCrossfade()
    {
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i] != null) pairs[i].Crossfade();
    }

    /// <summary>Switches every pair that contains the requested sprite.</summary>
    public void BroadcastTo(Sprite target)
    {
        if (target == null) return;
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i] != null && pairs[i].ContainsSprite(target)) pairs[i].CrossfadeTo(target);
    }

    /// <summary>Crossfades only pairs whose currently visible sprite is the supplied sprite.</summary>
    public void BroadcastFrom(Sprite visibleSprite)
    {
        if (visibleSprite == null) return;
        for (int i = 0; i < pairs.Count; i++)
        {
            if (pairs[i] != null && pairs[i].CurrentSprite == visibleSprite)
                pairs[i].Crossfade();
        }
    }
}
