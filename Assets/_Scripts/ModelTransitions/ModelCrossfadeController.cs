using System.Collections.Generic;
using UnityEngine;

/// <summary>Timeline Signal / UnityEvent entry points for a collection of model pairs.</summary>
[DisallowMultipleComponent]
public sealed class ModelCrossfadeController : MonoBehaviour
{
    [SerializeField] private List<ModelCrossfader> pairs = new List<ModelCrossfader>();
    public IReadOnlyList<ModelCrossfader> Pairs => pairs;

    private void Reset() => RefreshPairs();

    [ContextMenu("Refresh Pairs")]
    public void RefreshPairs()
    {
        pairs.Clear();
        pairs.AddRange(GetComponentsInChildren<ModelCrossfader>(true));
    }

    public void BroadcastCrossfade()
    {
        foreach (ModelCrossfader pair in UniqueActivePairs()) pair.Crossfade();
    }

    public void BroadcastToFirst()
    {
        foreach (ModelCrossfader pair in UniqueActivePairs()) pair.CrossfadeToFirst();
    }

    public void BroadcastToSecond()
    {
        foreach (ModelCrossfader pair in UniqueActivePairs()) pair.CrossfadeToSecond();
    }

    public void BroadcastTo(GameObject model)
    {
        foreach (ModelCrossfader pair in UniqueActivePairs())
            if (pair.ContainsModel(model)) pair.CrossfadeTo(model);
    }

    public void BroadcastFrom(GameObject visibleModel)
    {
        if (visibleModel == null) return;
        foreach (ModelCrossfader pair in UniqueActivePairs())
            if (pair.CurrentModel == visibleModel) pair.Crossfade();
    }

    public void ResetAll()
    {
        foreach (ModelCrossfader pair in UniqueActivePairs()) pair.ResetToInitial();
    }

    private IEnumerable<ModelCrossfader> UniqueActivePairs()
    {
        var visited = new HashSet<ModelCrossfader>();
        foreach (ModelCrossfader pair in pairs)
            if (pair != null && pair.isActiveAndEnabled && visited.Add(pair)) yield return pair;
    }
}
