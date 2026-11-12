using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// One blast on the bridge, started from a Timeline signal: explosion sound, then the explosion
/// particle, then the pieces of <see cref="scatter"/> are thrown.
/// </summary>
[AddComponentMenu("B1/Bridge Blast")]
public sealed class BridgeBlast : MonoBehaviour
{
    [Tooltip("Pieces thrown by this blast.")]
    [SerializeField] ExplosionScatter scatter;
    [Tooltip("Where the explosion appears. Empty = the middle of the scatter's pieces.")]
    [SerializeField] Transform blastPoint;

    [Header("Sound")]
    [Tooltip("Off: put the sound on the Timeline's Audio Track instead.")]
    [SerializeField] bool playSound = true;
    [Tooltip("Empty = Red_Complete.")]
    [SerializeField] AudioClip sound;
    [Range(0f, 1f)] [SerializeField] float volume = 1f;

    [Header("Particle")]
    [Tooltip("Explosion prefab, e.g. DAVFX Explosion 13. Empty = built-in fiery burst.")]
    [SerializeField] GameObject explosionPrefab;
    [Min(0.01f)] [SerializeField] float explosionScale = 3f;
    [Min(0f)] [SerializeField] float explosionLifetime = 15f;

    [Header("Order")]
    [Tooltip("Seconds from the sound to the particle.")]
    [Min(0f)] [SerializeField] float soundToParticle = 0.1f;
    [Tooltip("Seconds from the particle to the pieces being thrown.")]
    [Min(0f)] [SerializeField] float particleToDestroy = 0.15f;
    [SerializeField] bool shakeHeadset = true;

    public void Configure(ExplosionScatter target, GameObject prefab, AudioClip clip)
    {
        scatter = target;
        explosionPrefab = prefab;
        sound = clip;
    }

    /// <summary>Sound, particle, destruction, in that order. Use this from the Timeline signal.</summary>
    public void Play()
    {
        StopAllCoroutines();
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        PlaySound();
        if (soundToParticle > 0f) yield return new WaitForSeconds(soundToParticle);
        PlayParticle();
        if (particleToDestroy > 0f) yield return new WaitForSeconds(particleToDestroy);
        DestroyPieces();
    }

    public void PlaySound()
    {
        if (playSound) ExplosionCue.PlayAt(BlastPosition(), sound, volume);
    }

    public void PlayParticle()
    {
        var at = BlastPosition();
        if (explosionPrefab != null)
        {
            var boom = Instantiate(explosionPrefab, at, Quaternion.identity);
            boom.transform.localScale *= explosionScale;
            boom.SetActive(true);
            foreach (var ps in boom.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(true);
            if (explosionLifetime > 0f) Destroy(boom, explosionLifetime);
        }
        else
        {
            LaunchSmoke.CreateExplosionBurst(at, explosionScale);
        }

        if (shakeHeadset)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
            {
                var shake = origin.GetComponent<XRExplosionShake>();
                if (shake == null) shake = origin.gameObject.AddComponent<XRExplosionShake>();
                shake.ShakeFor(0.8f, 0.6f);
            }
        }
    }

    public void DestroyPieces()
    {
        if (scatter != null) scatter.Explode();
    }

    Vector3 BlastPosition()
    {
        if (blastPoint != null) return blastPoint.position;
        if (scatter != null && scatter.TryGetPiecesBounds(out var bounds)) return bounds.center;
        return transform.position;
    }
}
