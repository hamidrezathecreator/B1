using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

/// <summary>Scatters scene pieces via PrimeTween. Call Explode from Timeline or a UnityEvent.</summary>
public sealed class ExplosionScatter : MonoBehaviour
{
    [Header("Pieces")]
    [SerializeField] private Transform[] pieces = new Transform[0];
    [SerializeField] private Transform launchPoint;
    [SerializeField] private Transform groundPoint;

    [Header("Random Areas")]
    [Min(0f)] [SerializeField] private float launchRadius = 1f;
    [Min(0f)] [SerializeField] private float landingRadius = 2f;
    [Min(0f)] [SerializeField] private float launchHeight = 3f;
    [Min(0f)] [SerializeField] private float launchHeightVariation = 0.75f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [Min(0f)] [SerializeField] private float raycastHeight = 20f;
    [Min(0f)] [SerializeField] private float groundClearance = 0.01f;

    [Header("Timing")]
    [Min(0.01f)] [SerializeField] private float riseDuration = 0.35f;
    [Min(0.01f)] [SerializeField] private float fallDuration = 0.9f;
    [Range(0f, 0.5f)] [SerializeField] private float durationVariation = 0.12f;
    [Tooltip("PrimeTween easing for the launch and rise: controls the start and arrival at the apex.")]
    [SerializeField] private Ease riseEase = Ease.OutQuad;
    [Tooltip("PrimeTween easing for the fall: controls departure from the apex and arrival on the ground.")]
    [SerializeField] private Ease fallEase = Ease.InQuad;

    private readonly List<Sequence> activeTweens = new List<Sequence>();
    private readonly List<Vector3> startPositions = new List<Vector3>();
    private readonly List<Quaternion> startRotations = new List<Quaternion>();

    private void Awake() => CacheStartState();

    [ContextMenu("Test Explosion")]
    public void Explode()
    {
        StopActiveTweens();
        RestoreStartState();
        if (launchPoint == null || groundPoint == null || pieces == null || pieces.Length == 0)
        {
            Debug.LogWarning("ExplosionScatter needs pieces, a Launch Point and a Ground Point.", this);
            return;
        }

        for (int i = 0; i < pieces.Length; i++)
        {
            Transform piece = pieces[i];
            if (piece == null) continue;
            Vector2 launchOffset = Random.insideUnitCircle * launchRadius;
            Vector3 apex = launchPoint.position + new Vector3(launchOffset.x,
                launchHeight + Random.Range(-launchHeightVariation, launchHeightVariation), launchOffset.y);
            Vector2 landingOffset = Random.insideUnitCircle * landingRadius;
            Vector3 landing = groundPoint.position + new Vector3(landingOffset.x, 0f, landingOffset.y);
            landing.y = FindGroundHeight(landing) + groundClearance;
            Vector3 start = piece.position;
            Quaternion startRotation = piece.rotation;
            Quaternion endRotation = Random.rotation;
            float rise = RandomizedDuration(riseDuration);
            float fall = RandomizedDuration(fallDuration);
            Quaternion apexRotation = Quaternion.Slerp(startRotation, endRotation, 0.45f);
            Sequence sequence = Sequence.Create()
                .Chain(Tween.Custom(piece, 0f, 1f, rise, (target, t) =>
                {
                    if (target == null) return;
                    target.SetPositionAndRotation(Vector3.LerpUnclamped(start, apex, t),
                        Quaternion.SlerpUnclamped(startRotation, apexRotation, t));
                }, ease: riseEase))
                .Chain(Tween.Custom(piece, 0f, 1f, fall, (target, t) =>
                {
                    if (target == null) return;
                    target.SetPositionAndRotation(Vector3.LerpUnclamped(apex, landing, t),
                        Quaternion.SlerpUnclamped(apexRotation, endRotation, t));
                }, ease: fallEase));
            activeTweens.Add(sequence);
        }
    }

    [ContextMenu("Reset Explosion")]
    public void ResetExplosion()
    {
        StopActiveTweens();
        RestoreStartState();
    }

    private float FindGroundHeight(Vector3 landing)
    {
        Ray ray = new Ray(landing + Vector3.up * raycastHeight, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, raycastHeight * 2f, groundLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return groundPoint.position.y;
    }

    private float RandomizedDuration(float value) => value * Random.Range(1f - durationVariation, 1f + durationVariation);

    private void CacheStartState()
    {
        startPositions.Clear();
        startRotations.Clear();
        if (pieces == null) return;
        foreach (Transform piece in pieces)
        {
            startPositions.Add(piece != null ? piece.position : Vector3.zero);
            startRotations.Add(piece != null ? piece.rotation : Quaternion.identity);
        }
    }

    private void RestoreStartState()
    {
        if (pieces == null) return;
        for (int i = 0; i < pieces.Length && i < startPositions.Count; i++)
        {
            if (pieces[i] == null) continue;
            pieces[i].SetPositionAndRotation(startPositions[i], startRotations[i]);
        }
    }

    private void StopActiveTweens()
    {
        for (int i = 0; i < activeTweens.Count; i++)
            if (activeTweens[i].isAlive) activeTweens[i].Stop();
        activeTweens.Clear();
    }

    private void OnDisable() => StopActiveTweens();

    private void OnDrawGizmosSelected()
    {
        if (launchPoint != null) DrawRadius(launchPoint.position, launchRadius, new Color(1f, 0.55f, 0.1f));
        if (groundPoint != null) DrawRadius(groundPoint.position, landingRadius, new Color(0.2f, 0.9f, 0.35f));
        if (launchPoint != null && groundPoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(launchPoint.position, groundPoint.position + Vector3.up * launchHeight);
        }
    }

    private static void DrawRadius(Vector3 center, float radius, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(center, 0.08f);
        const int segments = 32;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float b = (i + 1) * Mathf.PI * 2f / segments;
            Gizmos.DrawLine(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius,
                center + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius);
        }
    }
}
