using System;
using UnityEngine;

/// <summary>
/// Sits on an explosion object that the Timeline switches on. Each time it appears it plays the
/// explosion sound at its position and raises <see cref="Shown"/>.
/// </summary>
public sealed class ExplosionCue : MonoBehaviour
{
    const string DefaultClipPath = "Audio/RedComplete";

    [Tooltip("Off: the sound is placed on the Timeline's Audio Track instead.")]
    [SerializeField] bool playSound;
    [SerializeField] AudioClip sound;
    [Range(0f, 1f)] [SerializeField] float volume = 1f;

    public bool PlaysSound { get => playSound; set => playSound = value; }

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
        if (playSound) PlayAt(transform.position, sound, volume);
        Shown?.Invoke(this);
    }

    /// <summary>Plays an explosion sound at a point. Empty clip = Red_Complete.</summary>
    public static void PlayAt(Vector3 position, AudioClip clip = null, float volume = 1f)
    {
        if (clip == null) clip = Resources.Load<AudioClip>(DefaultClipPath);
        if (clip == null) return;
        var go = new GameObject("Explosion Sound");
        go.transform.position = position;
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
