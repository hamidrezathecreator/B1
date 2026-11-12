using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

/// <summary>Scatters scene pieces via PrimeTween. Call Explode or ExplodePartly from Timeline or a UnityEvent.</summary>
public sealed class ExplosionScatter : MonoBehaviour
{
    [Header("Pieces")]
    [SerializeField] private Transform[] pieces = new Transform[0];
    [SerializeField] private Transform launchPoint;
    [SerializeField] private Transform groundPoint;
    [Tooltip("Pieces with these names (or under an object with these names) never move, even if listed above.")]
    [SerializeField] private string[] stayStillNames = { "Topology" };

    [Header("Random Areas")]
    [Min(0f)] [SerializeField] private float launchRadius = 1f;
    [Tooltip("How far each piece can land from where it started (or from the Ground Point when gathering).")]
    [Min(0f)] [SerializeField] private float landingRadius = 2f;
    [Tooltip("Off: every piece falls down near its own spot, pushed outward from the Launch Point. " +
             "On: all pieces land together around the Ground Point.")]
    [SerializeField] private bool gatherAtGroundPoint;
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
    private int partialExplosionStep;
    private int nextPieceIndex;

    private void Awake() => CacheStartState();

    /// <summary>Sets the pieces from code (for a scatter added at runtime) and remembers where they start.</summary>
    public void Configure(Transform[] newPieces, Transform blastCenter, float throwRadius, float landRadius,
        float height, LayerMask ground)
    {
        StopActiveTweens();
        pieces = newPieces ?? new Transform[0];
        launchPoint = blastCenter;
        launchRadius = throwRadius;
        landingRadius = landRadius;
        launchHeight = height;
        groundLayers = ground;
        raycastHeight = Mathf.Max(raycastHeight, 500f);
        gatherAtGroundPoint = false;
        partialExplosionStep = 0;
        nextPieceIndex = 0;
        CacheStartState();
    }

    [ContextMenu("Test Explosion")]
    public void Explode()
    {
        StopActiveTweens();
        RestoreStartState();
        if (!HasSetup()) return;

        partialExplosionStep = 4;
        nextPieceIndex = pieces.Length;
        for (int i = 0; i < pieces.Length; i++)
            ExplodePiece(pieces[i]);
    }

    /// <summary>Throws every piece not yet thrown, all at once. Later calls do nothing until ResetExplosion.</summary>
    [ContextMenu("Test Explode Once")]
    public void ExplodeOnce()
    {
        if (!HasSetup()) return;
        if (partialExplosionStep >= 4) return;

        partialExplosionStep = 4;
        while (nextPieceIndex < pieces.Length)
            ExplodePiece(pieces[nextPieceIndex++]);
    }

    [ContextMenu("Test Partial Explosion")]
    public void ExplodePartly()
    {
        if (!HasSetup()) return;

        if (partialExplosionStep >= 4) return;

        // Use cumulative quarters so rounding still releases every piece in four calls.
        partialExplosionStep++;
        int endIndex = Mathf.CeilToInt(pieces.Length * (partialExplosionStep / 4f));
        while (nextPieceIndex < endIndex)
            ExplodePiece(pieces[nextPieceIndex++]);
    }

    private void ExplodePiece(Transform piece)
    {
        if (piece == null || StaysStill(piece)) return;
        Vector3 start = piece.position;
        Vector2 launchOffset = Random.insideUnitCircle * launchRadius;
        // Rise from each piece's own position instead of converging on the launch point.
        Vector3 apex = start + new Vector3(launchOffset.x,
            launchHeight + Random.Range(-launchHeightVariation, launchHeightVariation), launchOffset.y);
        Vector3 landing = LandingSpot(start);
        landing.y = FindGroundHeight(landing, start.y) + groundClearance;
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

    private bool StaysStill(Transform piece)
    {
        if (stayStillNames == null) return false;
        for (Transform t = piece; t != null; t = t.parent)
            foreach (string name in stayStillNames)
                if (!string.IsNullOrEmpty(name) && t.name == name) return true;
        return false;
    }

    [ContextMenu("Reset Explosion")]
    public void ResetExplosion()
    {
        StopActiveTweens();
        RestoreStartState();
        partialExplosionStep = 0;
        nextPieceIndex = 0;
    }

    private bool HasSetup()
    {
        if (pieces != null && pieces.Length > 0 && (!gatherAtGroundPoint || groundPoint != null)) return true;
        Debug.LogWarning(gatherAtGroundPoint
            ? "ExplosionScatter needs pieces and a Ground Point when gathering."
            : "ExplosionScatter needs pieces.", this);
        return false;
    }

    /// <summary>Where a piece comes down, before snapping to the ground.</summary>
    private Vector3 LandingSpot(Vector3 start)
    {
        Vector2 random = Random.insideUnitCircle * landingRadius;
        if (gatherAtGroundPoint)
            return groundPoint.position + new Vector3(random.x, 0f, random.y);

        // Thrown outward from the blast, landing below and around where the piece was.
        Vector3 outward = Vector3.zero;
        if (launchPoint != null)
        {
            outward = start - launchPoint.position;
            outward.y = 0f;
            if (outward.sqrMagnitude > 0.0001f) outward = outward.normalized * Random.Range(0.3f, 1f) * landingRadius;
        }
        return start + outward + new Vector3(random.x, 0f, random.y) * 0.5f;
    }

    private float FindGroundHeight(Vector3 landing, float startHeight)
    {
        Ray ray = new Ray(landing + Vector3.up * raycastHeight, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, raycastHeight * 2f, groundLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return groundPoint != null ? groundPoint.position.y : startHeight;
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
