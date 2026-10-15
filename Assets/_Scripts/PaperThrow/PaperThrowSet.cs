using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Animates an assigned set of papers from their scene positions up into the air
/// and back down to a flat, scattered landing. Call ThrowPapers from a UnityEvent
/// (for example, a Timeline Signal Receiver).
/// </summary>
public class PaperThrowSet : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform[] papers = new Transform[0];
    [Tooltip("Center of the papers' highest positions, in world space.")]
    [SerializeField] private Transform highPoint;
    [Tooltip("Center of the landing area; its Y position is the fallback floor height.")]
    [SerializeField] private Transform groundPoint;

    [Header("Flight")]
    [Min(0.01f)] [SerializeField] private float riseDuration = 0.8f;
    [Min(0.01f)] [SerializeField] private float fallDuration = 1.6f;
    [Min(0f)] [SerializeField] private float startDelayPerPaper = 0.07f;
    [Range(0f, 0.5f)] [SerializeField] private float durationVariation = 0.15f;
    [Min(0f)] [SerializeField] private float highPointScatter = 0.6f;
    [Min(0f)] [SerializeField] private float landingScatter = 1.1f;
    [Min(0f)] [SerializeField] private float heightVariation = 0.3f;
    [Min(0f)] [SerializeField] private float flutterDistance = 0.12f;
    [Tooltip("Side-to-side travel while falling; fades out at the ground.")]
    [Min(0f)] [SerializeField] private float fallSwayDistance = 0.18f;
    [Range(0f, 40f)] [SerializeField] private float fallSwayAngle = 15f;
    [Min(0f)] [SerializeField] private float fallSwayCycles = 2f;

    [Header("Landing")]
    [Tooltip("Local direction of the visible front face of each paper. Use (0, 0, 1) if the mesh faces local Z.")]
    [SerializeField] private Vector3 localFaceNormal = Vector3.up;
    [Range(0f, 25f)] [SerializeField] private float landingTilt = 5f;
    [Tooltip("Extra height above the floor to prevent z-fighting (meters).")]
    [Min(0f)] [SerializeField] private float groundClearance = 0.003f;
    [Tooltip("Use the nearest floor collider under each landing spot, falling back to the Ground Point's Y.")]
    [SerializeField] private bool useGroundColliders = true;
    [SerializeField] private LayerMask groundLayers = ~0;
    [Min(0f)] [SerializeField] private float paperSeparation = 0.001f;
    [Range(0f, 3f)] [SerializeField] private float tumbleTurns = 1.5f;

    private readonly List<PaperFlight> flights = new List<PaperFlight>();
    private Coroutine running;

    private struct PaperFlight
    {
        public Transform paper;
        public Vector3 start;
        public Quaternion startRotation;
        public Vector3 apex;
        public Vector3 landing;
        public Quaternion landingRotation;
        public Vector3 tumbleAxis;
        public float turns;
        public float flutterPhase;
        public float swayPhase;
        public float swayCycles;
        public Vector3 swayAxis;
        public float delay;
        public float rise;
        public float fall;
    }

    /// <summary>Timeline Signal Receiver / UnityEvent entry point. Replays from the original scene positions.</summary>
    public void ThrowPapers()
    {
        if (highPoint == null || groundPoint == null || papers == null || papers.Length == 0)
        {
            Debug.LogWarning("PaperThrowSet needs papers, a High Point and a Ground Point.", this);
            return;
        }

        ResetPapers();
        Vector3 face = localFaceNormal.sqrMagnitude > 0.0001f ? localFaceNormal.normalized : Vector3.up;

        for (int i = 0; i < papers.Length; i++)
        {
            Transform paper = papers[i];
            if (paper == null) continue;

            Vector2 apexOffset = Random.insideUnitCircle * highPointScatter;
            Vector2 landOffset = Random.insideUnitCircle * landingScatter;
            Vector3 apex = highPoint.position + new Vector3(apexOffset.x, Random.Range(-heightVariation, heightVariation), apexOffset.y);
            Vector3 landing = groundPoint.position + new Vector3(landOffset.x, 0f, landOffset.y);
            Vector3 sideways = Vector3.Cross(Vector3.up, landing - apex);
            sideways = sideways.sqrMagnitude > 0.0001f ? sideways.normalized : Vector3.right;

            // First turn the paper's visible side upward, then add a random heading and small tilt.
            Quaternion flat = Quaternion.FromToRotation(paper.TransformDirection(face), Vector3.up) * paper.rotation;
            Quaternion heading = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
            Vector3 tiltAxis = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Vector3.right;
            Quaternion settled = Quaternion.AngleAxis(Random.Range(0f, landingTilt), tiltAxis) * heading * flat;
            float floorY = GetFloorHeight(landing);
            landing.y = floorY + groundClearance + paperSeparation * i
                - GetLowestMeshOffset(paper, settled);

            flights.Add(new PaperFlight
            {
                paper = paper,
                start = paper.position,
                startRotation = paper.rotation,
                apex = apex,
                landing = landing,
                landingRotation = settled,
                tumbleAxis = Random.onUnitSphere,
                turns = Random.Range(-tumbleTurns, tumbleTurns),
                flutterPhase = Random.Range(0f, Mathf.PI * 2f),
                swayPhase = Random.Range(0f, Mathf.PI * 2f),
                swayCycles = fallSwayCycles * Random.Range(0.8f, 1.2f),
                swayAxis = sideways,
                delay = i * startDelayPerPaper,
                rise = riseDuration * Random.Range(1f - durationVariation, 1f + durationVariation),
                fall = fallDuration * Random.Range(1f - durationVariation, 1f + durationVariation)
            });
        }

        if (flights.Count > 0) running = StartCoroutine(AnimatePapers());
    }

    private float GetFloorHeight(Vector3 landing)
    {
        float floorY = groundPoint.position.y;
        if (!useGroundColliders) return floorY;

        Ray ray = new Ray(new Vector3(landing.x, floorY + 0.5f, landing.z), Vector3.down);
        RaycastHit[] hits = Physics.RaycastAll(ray, 1.25f, groundLayers, QueryTriggerInteraction.Ignore);
        float closestDistance = 0.25f;
        foreach (RaycastHit hit in hits)
        {
            bool isPaper = false;
            foreach (Transform paper in papers)
            {
                if (paper != null && hit.collider.transform.IsChildOf(paper))
                {
                    isPaper = true;
                    break;
                }
            }

            float distance = Mathf.Abs(hit.point.y - groundPoint.position.y);
            if (isPaper || distance >= closestDistance) continue;
            closestDistance = distance;
            floorY = hit.point.y;
        }
        return floorY;
    }

    // Measure the visible mesh, not the transform pivot. The meshes can have
    // off-center pivots, so putting the pivot on the floor makes them float.
    private static float GetLowestMeshOffset(Transform paper, Quaternion landingRotation)
    {
        float lowest = float.PositiveInfinity;
        Quaternion rotationChange = landingRotation * Quaternion.Inverse(paper.rotation);
        MeshFilter[] filters = paper.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null) continue;
            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 center = bounds.center;
            Vector3 extent = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + Vector3.Scale(extent, new Vector3(x, y, z));
                Vector3 offset = filter.transform.TransformPoint(corner) - paper.position;
                lowest = Mathf.Min(lowest, (rotationChange * offset).y);
            }
        }

        return float.IsPositiveInfinity(lowest) ? 0f : lowest;
    }

    /// <summary>Return the papers to the positions and rotations recorded before their first throw.</summary>
    public void ResetPapers()
    {
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }

        for (int i = 0; i < flights.Count; i++)
        {
            PaperFlight flight = flights[i];
            if (flight.paper == null) continue;
            flight.paper.SetPositionAndRotation(flight.start, flight.startRotation);
        }
        flights.Clear();
    }

    private IEnumerator AnimatePapers()
    {
        float elapsed = 0f;
        float end = 0f;
        for (int i = 0; i < flights.Count; i++)
            end = Mathf.Max(end, flights[i].delay + flights[i].rise + flights[i].fall);

        while (elapsed < end)
        {
            for (int i = 0; i < flights.Count; i++)
            {
                PaperFlight f = flights[i];
                if (f.paper == null || elapsed < f.delay) continue;

                float time = elapsed - f.delay;
                float progress;
                float sway = 0f;
                Vector3 position;

                if (time < f.rise)
                {
                    float t = Mathf.Clamp01(time / f.rise);
                    position = Vector3.LerpUnclamped(f.start, f.apex, 1f - (1f - t) * (1f - t));
                    progress = 0.5f * t;
                }
                else
                {
                    float t = Mathf.Clamp01((time - f.rise) / f.fall);
                    position = Vector3.LerpUnclamped(f.apex, f.landing, t * t);
                    // The sheet swings and banks as it floats down, then straightens to land.
                    sway = Mathf.Sin(2f * Mathf.PI * f.swayCycles * t + f.swayPhase)
                        * Mathf.Sin(Mathf.PI * t);
                    position += f.swayAxis * (sway * fallSwayDistance);
                    progress = 0.5f + 0.5f * t;
                }

                // The flutter fades to zero before landing so every paper finishes exactly on the ground.
                float flutter = Mathf.Sin(Mathf.PI * progress) * flutterDistance;
                position += new Vector3(Mathf.Sin(15f * progress + f.flutterPhase), 0f,
                    Mathf.Cos(11f * progress + f.flutterPhase)) * flutter;

                float spinFade = 1f - Mathf.SmoothStep(0.7f, 1f, progress);
                Quaternion baseRotation = Quaternion.Slerp(f.startRotation, f.landingRotation, progress);
                Quaternion tumble = Quaternion.AngleAxis(360f * f.turns * progress * spinFade, f.tumbleAxis);
                Quaternion bank = Quaternion.AngleAxis(sway * fallSwayAngle, f.swayAxis);
                f.paper.SetPositionAndRotation(position, bank * tumble * baseRotation);
            }

            yield return null;
            elapsed += Time.deltaTime;
        }

        for (int i = 0; i < flights.Count; i++)
        {
            PaperFlight f = flights[i];
            if (f.paper != null) f.paper.SetPositionAndRotation(f.landing, f.landingRotation);
        }
        running = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (highPoint != null)
        {
            Gizmos.color = new Color(1f, 0.75f, 0.15f);
            DrawCircle(highPoint.position, highPointScatter);
        }
        if (groundPoint != null)
        {
            Gizmos.color = new Color(0.25f, 0.9f, 0.4f);
            DrawCircle(groundPoint.position, landingScatter);
        }
        if (highPoint != null && groundPoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(highPoint.position, groundPoint.position);
        }
        if (papers == null || highPoint == null) return;
        Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.5f);
        foreach (Transform paper in papers)
            if (paper != null) Gizmos.DrawLine(paper.position, highPoint.position);
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        Gizmos.DrawWireSphere(center, 0.07f);
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
