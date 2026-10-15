using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Small exact-string event bus used to connect tutorial steps and scene interactables.</summary>
public static class TutorialEventBus
{
    static readonly Dictionary<string, Action> listeners = new Dictionary<string, Action>(StringComparer.Ordinal);

    public static void Listen(string id, Action listener)
    {
        if (string.IsNullOrEmpty(id) || listener == null) return;
        if (listeners.TryGetValue(id, out var current)) listeners[id] = current + listener;
        else listeners.Add(id, listener);
    }

    public static void StopListening(string id, Action listener)
    {
        if (string.IsNullOrEmpty(id) || listener == null || !listeners.TryGetValue(id, out var current)) return;
        current -= listener;
        if (current == null) listeners.Remove(id);
        else listeners[id] = current;
    }

    public static void Publish(string id)
    {
        if (string.IsNullOrEmpty(id) || !listeners.TryGetValue(id, out var current)) return;
        current?.Invoke();
    }
}
