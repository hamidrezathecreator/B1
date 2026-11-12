using System;
using UnityEngine;

/// <summary>
/// Sits on an explosion object that the Timeline switches on. Each time it appears it plays the
/// explosion sound at its position and raises <see cref="Shown"/>.
/// </summary>
public sealed class ExplosionCue : MonoBehaviour
{
    const string DefaultClipPath = "Audio/RedComplete";

    [SerializeField] AudioClip sound;
    [Range(0f, 1f)] [SerializeField] float volume = 1f;

    public event Action<ExplosionCue> Shown;

    /// <summary>Adds the cue once to an object (works while it is still switched off).</summary>
    public static ExplosionCue AddTo(GameObject go)
    {
        if (go == null) return null;
        var cue = go.GetComponent<ExplosionCue>();
        return cue != null ? cue : go.AddComponent<ExplosionCue>();
    }

    void OnEnable()
    {
        PlaySound();
        Shown?.Invoke(this);
    }

    void PlaySound()
    {
        var clip = sound != null ? sound : Resources.Load<AudioClip>(DefaultClipPath);
        if (clip == null) return;
        var go = new GameObject("Explosion Sound");
        go.transform.position = transform.position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        // Mostly 3D so it comes from the blast, but loud from far across the bridge.
        source.spatialBlend = 0.6f;
        source.minDistance = 40f;
        source.maxDistance = 1500f;
        source.Play();
        Destroy(go, clip.length + 0.5f);
    }
}
