using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Scatters scene pieces via PrimeTween. Call Explode or ExplodePartly from Timeline or a UnityEvent.</summary>
public sealed class ExplosionScatter : MonoBehaviour
{
    public enum ScatterRiseStyle
    {
        [InspectorName("Explosive pop")]
        ExplosivePop,
        [InspectorName("Smooth lift")]
        SmoothLift,
        [InspectorName("Steady speed")]
        Steady,
    }

    public enum ScatterFallStyle
    {
        [InspectorName("Heavy drop")]
        HeavyDrop,
        [InspectorName("Accelerate (gravity)")]
        Accelerate,
        [InspectorName("Steady speed")]
        Steady,
    }

    [Serializable]
    public sealed class MergeGroup
    {
        public Transform driver;
        public Transform[] followers = Array.Empty<Transform>();
    }

    readonly struct ScatterPath
    {
        public readonly Vector3 apex;
        public readonly Vector3 landing;
        public readonly Vector3 flatOut;
        public readonly Quaternion tumbleDelta;

        public ScatterPath(Vector3 apex, Vector3 landing, Vector3 flatOut, Quaternion tumbleDelta)
        {
            this.apex = apex;
            this.landing = landing;
            this.flatOut = flatOut;
            this.tumbleDelta = tumbleDelta;
        }
    }

    struct RestPose
    {
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 position;
        public Quaternion rotation;
    }

    readonly struct FollowerStart
    {
        public readonly Transform transform;
        public readonly Vector3 localPosition;
        public readonly Quaternion localRotation;

        public FollowerStart(Transform transform, Vector3 localPosition, Quaternion localRotation)
        {
            this.transform = transform;
            this.localPosition = localPosition;
            this.localRotation = localRotation;
        }
    }

    readonly struct PreparedDriverAnim
    {
        public readonly Transform driver;
        public readonly FollowerStart[] followers;
        public readonly Vector3 start;
        public readonly Quaternion startRotation;
        public readonly Vector3 apex;
        public readonly Vector3 landing;
        public readonly Vector3 flatOut;
        public readonly Quaternion tumbleDelta;
        public readonly float totalDuration;

        public PreparedDriverAnim(Transform driver, FollowerStart[] followers, Vector3 start, Quaternion startRotation,
            Vector3 apex, Vector3 landing, Vector3 flatOut, Quaternion tumbleDelta, float totalDuration)
        {
            this.driver = driver;
            this.followers = followers;
            this.start = start;
            this.startRotation = startRotation;
            this.apex = apex;
            this.landing = landing;
            this.flatOut = flatOut;
            this.tumbleDelta = tumbleDelta;
            this.totalDuration = totalDuration;
        }
    }

    [Header("Pieces")]
    [SerializeField] Transform[] pieces = Array.Empty<Transform>();
    [SerializeField] Transform launchPoint;
    [SerializeField] Transform groundPoint;
    [Tooltip("Pieces with these names (or under an object with these names) never move, even if listed above.")]
    [SerializeField] string[] stayStillNames = { "Topology" };

    [Header("Blast Scatter")]
    [Tooltip("Explosion origin. Each piece moves outward from here through its own position.")]
    [SerializeField] Transform blastOrigin;
    [FormerlySerializedAs("launchRadius")]
    [SerializeField, HideInInspector] float legacyLaunchRadius;
    [Tooltip("Peak travel along the outward blast direction (horizontal part of the arc).")]
    [Min(0f)] [SerializeField] float launchHeight = 3f;
    [Range(0.05f, 0.85f)] [SerializeField] float launchHeightVariation = 0.45f;
    [Tooltip("Extra peak height straight up (world Y only), on top of the outward arc.")]
    [Min(0f)] [SerializeField] float upwardHeight = 1.5f;
    [Range(0.05f, 0.85f)] [SerializeField] float upwardHeightVariation = 0.45f;
    [Tooltip("Extra lift mixed into the outward direction (0 = pure radial, 1 = strong upward arc).")]
    [Range(0f, 1.5f)] [SerializeField] float upwardBlend = 0.35f;
    [Tooltip("Sideways wobble at the apex, perpendicular to the blast direction (keep small).")]
    [Min(0f)] [SerializeField] float lateralJitter = 0.35f;
    [Tooltip("How far each piece can skid along the horizontal blast direction when landing.")]
    [Min(0f)] [SerializeField] float landingRadius = 2f;
    [Tooltip("Impulse multiplier for pieces almost level with the blast vs. far above/below (dot with world up).")]
    [Range(0.25f, 1.5f)] [SerializeField] float lowVerticalDotImpulse = 0.82f;
    [Range(0.25f, 2f)] [SerializeField] float highVerticalDotImpulse = 1.12f;
    [Tooltip("Closer pieces get a stronger push.")]
    [Min(0f)] [SerializeField] float distanceFalloff = 0.08f;
    [Tooltip("Max tumble in degrees from rest rotation (per axis), applied over the whole arc through landing.")]
    [Range(0f, 180f)] [SerializeField] float rotationVariance = 55f;
    [Range(0.05f, 0.85f)] [SerializeField] float rotationVarianceVariation = 0.45f;
    [Tooltip("Off: land near each piece's outward skid from rest. On: snap landing under that piece's apex (Ground Point only drives ground height / raycasts).")]
    [SerializeField] bool gatherAtGroundPoint;
    [SerializeField] LayerMask groundLayers = ~0;
    [Min(0f)] [SerializeField] float raycastHeight = 20f;
    [Min(0f)] [SerializeField] float groundClearance = 0.01f;

    [Header("Exclusion volume")]
    [Tooltip("Pieces must leave this box for the whole flight. Child BoxCollider named ExclusionVolume is auto-picked if empty.")]
    [SerializeField] BoxCollider exclusionVolume;
    [Min(0f)] [SerializeField] float exclusionMargin = 0.35f;

    [Header("Merge (built with the editor scan — one tween per group)")]
    [Min(0f)] [SerializeField] float mergeThreshold = 1.25f;
    [Range(2, 3)] [SerializeField] int maxPiecesPerMergeGroup = 3;
    [SerializeField] List<MergeGroup> mergeGroups = new List<MergeGroup>();

    [Header("Timing")]
    [Tooltip("Adds to total scatter time. One continuous arc — not a separate rise phase.")]
    [Min(0.01f)] [SerializeField] float riseDuration = 0.35f;
    [Tooltip("Adds to total scatter time. One continuous arc — not a separate fall phase.")]
    [Min(0.01f)] [SerializeField] float fallDuration = 0.9f;
    [Range(0f, 0.5f)] [SerializeField] float durationVariation = 0.12f;
    [Tooltip("Easing along the whole arc (time → position). Avoids a mid-air pause at the peak.")]
    [SerializeField] ScatterRiseStyle riseStyle = ScatterRiseStyle.ExplosivePop;
    [HideInInspector] [SerializeField] ScatterFallStyle fallStyle = ScatterFallStyle.HeavyDrop;

    [Header("Gizmos")]
    [SerializeField] bool drawTrajectoryGizmos = true;
    [SerializeField] bool drawTrajectoryWhenNotSelected;
    [Min(1)] [SerializeField] int gizmoTrajectorySampleCount = 12;
    [Min(0.02f)] [SerializeField] float gizmoCubeSize = 0.12f;

    readonly List<Sequence> activeTweens = new List<Sequence>();
    readonly Dictionary<Transform, RestPose> restPoses = new Dictionary<Transform, RestPose>();
    readonly Dictionary<Transform, FollowerStart[]> followerStartsByDriver = new Dictionary<Transform, FollowerStart[]>();
    readonly HashSet<Transform> groupedFollowers = new HashSet<Transform>();

    int partialExplosionStep;
    int nextPieceIndex;

#if UNITY_EDITOR
    readonly List<EditorDriverAnim> editorDriverAnims = new List<EditorDriverAnim>();
    bool editorPreviewActive;
    double editorPreviewStart;

    readonly struct EditorDriverAnim
    {
        public readonly PreparedDriverAnim anim;
        public readonly double riseStart;

        public EditorDriverAnim(PreparedDriverAnim anim, double riseStart)
        {
            this.anim = anim;
            this.riseStart = riseStart;
        }
    }
#endif

    public bool IsEditorPreviewActive
    {
        get
        {
#if UNITY_EDITOR
            return editorPreviewActive;
#else
            return false;
#endif
        }
    }

    Transform LaunchOrigin => ResolveOwnedReference(launchPoint, "LaunchPoint", "Launch Point");
    Transform GroundReference => ResolveOwnedReference(groundPoint, "GroundPoint", "Ground Point");
    Transform BlastOrigin => IsUnderScatter(blastOrigin, transform) ? blastOrigin : LaunchOrigin;

    void Awake()
    {
        SanitizeOwnedReferences();
        SanitizeLegacyLaunchValues();
        CacheStartState();
    }

    void OnEnable() => SanitizeOwnedReferences();

    void OnValidate()
    {
        SanitizeOwnedReferences();
        if (lateralJitter < 0.01f && legacyLaunchRadius > 0.01f)
            lateralJitter = Mathf.Clamp(legacyLaunchRadius * 0.12f, 0.05f, 2f);
        if (launchHeightVariation > 0.9f) launchHeightVariation = 0.45f;
        if (upwardHeightVariation > 0.9f) upwardHeightVariation = 0.45f;
        launchHeightVariation = Mathf.Clamp(launchHeightVariation, 0.05f, 0.85f);
        upwardHeightVariation = Mathf.Clamp(upwardHeightVariation, 0.05f, 0.85f);
        rotationVarianceVariation = Mathf.Clamp(rotationVarianceVariation, 0.05f, 0.85f);
        SanitizeLegacyLaunchValues();
    }

    void SanitizeLegacyLaunchValues()
    {
        if (launchHeight < 0.15f && legacyLaunchRadius > 0.15f)
            launchHeight = Mathf.Clamp(legacyLaunchRadius * 0.4f, 1f, legacyLaunchRadius);
        if (upwardHeight < 0.15f && legacyLaunchRadius > 0.15f)
            upwardHeight = Mathf.Clamp(legacyLaunchRadius * 0.18f, 0.5f, legacyLaunchRadius * 0.5f);
    }

    float EffectiveLaunchHeight() =>
        launchHeight >= 0.15f ? launchHeight :
        legacyLaunchRadius > 0.15f ? Mathf.Clamp(legacyLaunchRadius * 0.4f, 1f, legacyLaunchRadius) : 3f;

    float EffectiveUpwardHeight() =>
        upwardHeight >= 0.15f ? upwardHeight :
        legacyLaunchRadius > 0.15f ? Mathf.Clamp(legacyLaunchRadius * 0.18f, 0.5f, legacyLaunchRadius * 0.5f) : 1.5f;

    static bool IsUnderScatter(Transform candidate, Transform scatterRoot) =>
        candidate != null && scatterRoot != null &&
        (candidate == scatterRoot || candidate.IsChildOf(scatterRoot));

    Transform ResolveOwnedReference(Transform current, params string[] childNames)
    {
        if (IsUnderScatter(current, transform)) return current;
        foreach (var name in childNames)
        {
            var found = transform.Find(name);
            if (found != null) return found;
        }
        foreach (Transform child in transform)
        {
            foreach (var name in childNames)
            {
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
        }
#if UNITY_EDITOR
        if (current != null)
            Debug.LogWarning(
                "ExplosionScatter '" + name + "': ignoring '" + current.name +
                "' — assign Launch/Ground children on this object only.", this);
#endif
        return null;
    }

    void SanitizeOwnedReferences()
    {
        var launch = ResolveOwnedReference(launchPoint, "LaunchPoint", "Launch Point");
        var ground = ResolveOwnedReference(groundPoint, "GroundPoint", "Ground Point");
        if (launch != null) launchPoint = launch;
        if (ground != null) groundPoint = ground;
        if (blastOrigin == null || !IsUnderScatter(blastOrigin, transform))
            blastOrigin = launchPoint;
        ResolveExclusionVolume();
    }

    void ResolveExclusionVolume()
    {
        if (exclusionVolume != null && IsUnderScatter(exclusionVolume.transform, transform)) return;

        foreach (var name in new[] { "ExclusionVolume", "Exclusion Zone", "Exclusion" })
        {
            var found = transform.Find(name);
            if (found != null && found.TryGetComponent(out BoxCollider box))
            {
                exclusionVolume = box;
                return;
            }
        }

        foreach (Transform child in transform)
        {
            if (string.Equals(child.name, "LaunchPoint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(child.name, "GroundPoint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(child.name, "Launch Point", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(child.name, "Ground Point", StringComparison.OrdinalIgnoreCase))
                continue;
            if (child.TryGetComponent(out BoxCollider childBox))
            {
                exclusionVolume = childBox;
                return;
            }
        }
    }

    public void Configure(Transform[] newPieces, Transform blastCenter, float throwRadius, float landRadius,
        float height, LayerMask ground)
    {
        StopActiveTweens();
        pieces = newPieces ?? Array.Empty<Transform>();
        launchPoint = blastCenter;
        blastOrigin = blastCenter;
        lateralJitter = Mathf.Clamp(throwRadius * 0.12f, 0.05f, throwRadius);
        landingRadius = landRadius;
        launchHeight = height;
        groundLayers = ground;
        raycastHeight = Mathf.Max(raycastHeight, 500f);
        gatherAtGroundPoint = false;
        partialExplosionStep = 0;
        nextPieceIndex = 0;
        mergeGroups.Clear();
        CacheStartState();
    }

    public bool TryGetPiecesBounds(out Bounds bounds)
    {
        bounds = default;
        var has = false;
        if (pieces == null) return false;
        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }
        return has;
    }

    [ContextMenu("Test Explosion")]
    public void Explode()
    {
        SanitizeOwnedReferences();
        StopActiveTweens();
        EnsureStartCached();
        RestoreStartState();
        if (!HasSetup()) return;

        partialExplosionStep = 4;
        nextPieceIndex = pieces.Length;
        ExplodeAllEligible();
    }

    [ContextMenu("Test Explode Once")]
    public void ExplodeOnce()
    {
        if (!HasSetup()) return;
        if (partialExplosionStep >= 4) return;

        partialExplosionStep = 4;
        while (nextPieceIndex < pieces.Length)
            ExplodeEntryAtIndex(nextPieceIndex++);
    }

    [ContextMenu("Test Partial Explosion")]
    public void ExplodePartly()
    {
        if (!HasSetup()) return;
        if (partialExplosionStep >= 4) return;

        partialExplosionStep++;
        int endIndex = Mathf.CeilToInt(pieces.Length * (partialExplosionStep / 4f));
        while (nextPieceIndex < endIndex)
            ExplodeEntryAtIndex(nextPieceIndex++);
    }

    void ExplodeAllEligible()
    {
        var handled = new HashSet<Transform>();

        foreach (var group in mergeGroups)
        {
            if (group?.driver == null || StaysStill(group.driver)) continue;
            if (handled.Contains(group.driver)) continue;
            handled.Add(group.driver);
            if (group.followers != null)
                foreach (var f in group.followers)
                    if (f != null) handled.Add(f);
            ExplodeDriver(group.driver, group.followers, HashSeed(group.driver));
        }

        foreach (var piece in pieces)
        {
            if (piece == null || handled.Contains(piece) || StaysStill(piece)) continue;
            ExplodeSingle(piece, HashSeed(piece));
        }
    }

    void ExplodeEntryAtIndex(int index)
    {
        if (index < 0 || index >= pieces.Length) return;
        var piece = pieces[index];
        if (piece == null || StaysStill(piece)) return;

        if (groupedFollowers.Contains(piece)) return;

        foreach (var group in mergeGroups)
        {
            if (group?.driver != piece) continue;
            ExplodeDriver(group.driver, group.followers, HashSeed(group.driver));
            return;
        }

        ExplodeSingle(piece, HashSeed(piece));
    }

    void ExplodeSingle(Transform piece, int seed) =>
        ExplodeDriver(piece, null, seed);

    void ExplodeDriver(Transform driver, Transform[] followers, int seed)
    {
        if (!TryPrepareDriverAnim(driver, followers, seed, out var prepared)) return;

        if (prepared.followers != null)
            followerStartsByDriver[driver] = prepared.followers;

        ApplyScatterFrame(prepared, driver, 0f);

        Sequence sequence = Sequence.Create()
            .Chain(Tween.Custom(prepared.driver, 0f, 1f, prepared.totalDuration, (target, u) =>
            {
                if (target == null) return;
                ApplyScatterFrame(prepared, target, u);
            }, ease: Ease.Linear));
        activeTweens.Add(sequence);
    }

    bool TryPrepareDriverAnim(Transform driver, Transform[] followers, int seed, out PreparedDriverAnim prepared)
    {
        prepared = default;
        if (driver == null) return false;

        Vector3 start = driver.position;
        Quaternion startRotation = driver.rotation;
        var path = BuildScatterPath(start, startRotation, seed);

        FollowerStart[] followerStarts = null;
        if (followers != null && followers.Length > 0)
        {
            var list = new List<FollowerStart>(followers.Length);
            foreach (var f in followers)
            {
                if (f == null || f == driver) continue;
                list.Add(new FollowerStart(f,
                    f.position - start,
                    Quaternion.Inverse(startRotation) * f.rotation));
            }
            if (list.Count > 0) followerStarts = list.ToArray();
        }

        float rise = RandomizedDuration(riseDuration, seed);
        float fall = RandomizedDuration(fallDuration, seed + 17);
        float total = rise + fall;

        prepared = new PreparedDriverAnim(driver, followerStarts, start, startRotation, path.apex, path.landing,
            path.flatOut, path.tumbleDelta, total);
        return true;
    }

    void ApplyScatterFrame(PreparedDriverAnim prepared, Transform driver, float timelineU)
    {
        if (driver == null) return;
        float curveT = ArcTimeToParameter(Mathf.Clamp01(timelineU));
        Vector3 pos = EvaluateScatterArc(prepared.start, prepared.apex, prepared.landing, curveT);
        float rotT = Mathf.Clamp01(timelineU);
        Quaternion rot = prepared.startRotation *
            Quaternion.SlerpUnclamped(Quaternion.identity, prepared.tumbleDelta, rotT);
        driver.SetPositionAndRotation(pos, rot);
        ApplyFollowers(driver, rot, prepared.start, prepared.startRotation, prepared.followers);
    }

    /// <summary>Single easing over the full arc (0→1). Never splits at the geometric peak.</summary>
    float ArcTimeToParameter(float timelineU)
    {
        timelineU = Mathf.Clamp01(timelineU);
        return riseStyle switch
        {
            ScatterRiseStyle.Steady => timelineU,
            ScatterRiseStyle.SmoothLift => 1f - (1f - timelineU) * (1f - timelineU),
            _ => 1f - Mathf.Pow(1f - timelineU, 3f),
        };
    }

    /// <summary>Smooth arc through apex at curveT = 0.5 (control point derived from rest, peak, and landing).</summary>
    static Vector3 EvaluateScatterArc(Vector3 start, Vector3 apex, Vector3 landing, float curveT)
    {
        curveT = Mathf.Clamp01(curveT);
        Vector3 control = 2f * apex - 0.5f * (start + landing);
        float u = 1f - curveT;
        return u * u * start + 2f * u * curveT * control + curveT * curveT * landing;
    }

    static void ApplyFollowers(Transform driver, Quaternion driverRot, Vector3 driverStart, Quaternion driverStartRot,
        FollowerStart[] followers)
    {
        if (followers == null || followers.Length == 0) return;
        var delta = driverRot * Quaternion.Inverse(driverStartRot);
        var driverPos = driver.position;
        foreach (var f in followers)
        {
            if (f.transform == null) continue;
            f.transform.SetPositionAndRotation(driverPos + delta * f.localPosition, delta * driverStartRot * f.localRotation);
        }
    }

    ScatterPath BuildScatterPath(Vector3 start, Quaternion startRotation, int seed)
    {
        Vector3 flatOut = HorizontalOutward(start, seed);
        var apex = ApexSpot(start, seed, flatOut);
        float minPeakY = start.y + EffectiveUpwardHeight() * 0.35f;
        if (apex.y < minPeakY)
            apex.y = minPeakY;
        var landing = SnapLandingY(LandingSpot(start, seed, flatOut, apex), start.y);
        landing = PushOutOfExclusion(landing, flatOut);
        var tumbleDelta = SampleTumbleDelta(startRotation, seed);
        return new ScatterPath(apex, landing, flatOut, tumbleDelta);
    }

    /// <summary>Local-space rotation from rest to landing — multiplied onto startRotation each frame.</summary>
    Quaternion SampleTumbleDelta(Quaternion startRotation, int seed)
    {
        if (rotationVariance < 0.01f) return Quaternion.identity;
        float maxAngle = rotationVariance * HeightVariationMultiplier(seed + 91, rotationVarianceVariation);
        maxAngle = Mathf.Max(1f, maxAngle);
        float Ex(int s) => Application.isPlaying
            ? UnityEngine.Random.Range(-maxAngle, maxAngle)
            : Mathf.Lerp(-maxAngle, maxAngle, Hash01(s));
        Quaternion eulerTumble = Quaternion.Euler(Ex(seed + 92), Ex(seed + 93), Ex(seed + 94));

        float chaosBlend = Mathf.Clamp01(rotationVariance / 180f);
        if (chaosBlend < 0.02f) return eulerTumble;

        Quaternion randomDelta = Application.isPlaying
            ? Quaternion.Inverse(startRotation) * UnityEngine.Random.rotationUniform
            : Quaternion.Inverse(startRotation) * HashRotationUniform(seed + 95);
        return Quaternion.SlerpUnclamped(eulerTumble, randomDelta, chaosBlend);
    }

    static Quaternion HashRotationUniform(int seed)
    {
        float u1 = Hash01(seed);
        float u2 = Hash01(seed + 1);
        float sqrtOneMinusU1 = Mathf.Sqrt(1f - u1);
        float sqrtU1 = Mathf.Sqrt(u1);
        float t = 2f * Mathf.PI * u2;
        return new Quaternion(sqrtOneMinusU1 * Mathf.Sin(t), sqrtOneMinusU1 * Mathf.Cos(t),
            sqrtU1 * Mathf.Sin(t), sqrtU1 * Mathf.Cos(t));
    }

    Vector3 ApexSpot(Vector3 start, int seed, Vector3 flatOut)
    {
        Vector3 outward = OutwardDirection(start);
        float impulse = ImpulseScale(start, outward);
        float reachBase = EffectiveLaunchHeight();
        float upBase = EffectiveUpwardHeight();
        float outwardReach = reachBase * impulse * HeightVariationMultiplier(seed, launchHeightVariation);
        float verticalReach = upBase * impulse * HeightVariationMultiplier(seed + 2, upwardHeightVariation);
        outwardReach = Mathf.Max(reachBase * 0.25f * impulse, outwardReach);

        if (exclusionVolume != null && IsInsideExclusion(start))
        {
            var bounds = GetExclusionBounds();
            outwardReach = Mathf.Max(outwardReach, MinTravelToExitBounds(start, flatOut, bounds));
            verticalReach = Mathf.Max(verticalReach, MinTravelToExitBounds(start, Vector3.up, bounds));
        }

        Vector3 side = Vector3.Cross(Vector3.up, flatOut).normalized;
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        float sideOffset = SampleRange(seed + 3, -lateralJitter, lateralJitter);

        var apex = start + flatOut * outwardReach + Vector3.up * verticalReach + side * sideOffset;
        return PushOutOfExclusion(apex, flatOut);
    }

    Vector3 LandingSpot(Vector3 start, int seed, Vector3 flatOut, Vector3 apex)
    {
        var ground = GroundReference;
        if (gatherAtGroundPoint && ground != null)
        {
            var at = new Vector3(apex.x, ground.position.y, apex.z);
            return PushOutOfExclusion(at, flatOut);
        }

        Vector3 outward = OutwardDirection(start);
        float impulse = ImpulseScale(start, outward);

        float slide = landingRadius * impulse * SampleRange(seed + 7, 0.45f, 1f);
        if (exclusionVolume != null && IsInsideExclusion(start))
            slide = Mathf.Max(slide, MinTravelToExitBounds(start, flatOut, GetExclusionBounds()));

        Vector3 side = Vector3.Cross(Vector3.up, flatOut).normalized;
        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        float sideSlide = landingRadius * 0.12f * SampleRange(seed + 8, -1f, 1f);

        var landing = start + flatOut * slide + side * sideSlide;
        return PushOutOfExclusion(landing, flatOut);
    }

    bool TryGetExclusionBounds(out Bounds bounds)
    {
        bounds = GetExclusionBounds();
        return exclusionVolume != null && bounds.size.sqrMagnitude > 0.0001f;
    }

    Bounds GetExclusionBounds() =>
        exclusionVolume != null ? exclusionVolume.bounds : default;

    bool IsInsideExclusion(Vector3 worldPoint)
    {
        if (exclusionVolume == null) return false;
        Vector3 local = exclusionVolume.transform.InverseTransformPoint(worldPoint);
        Vector3 half = exclusionVolume.size * 0.5f;
        Vector3 rel = local - exclusionVolume.center;
        return Mathf.Abs(rel.x) <= half.x && Mathf.Abs(rel.y) <= half.y && Mathf.Abs(rel.z) <= half.z;
    }

    float MinTravelToExitBounds(Vector3 inside, Vector3 direction, Bounds bounds)
    {
        if (!IsInsideExclusion(inside)) return 0f;
        if (direction.sqrMagnitude < 0.0001f) return exclusionMargin;
        direction.Normalize();
        Vector3 p = inside;
        const float step = 0.12f;
        float traveled = 0f;
        for (int i = 0; i < 512; i++)
        {
            if (!IsInsideExclusion(p)) return traveled + exclusionMargin;
            p += direction * step;
            traveled += step;
        }
        return traveled + exclusionMargin;
    }

    Vector3 PushOutOfExclusion(Vector3 point, Vector3 flatOut)
    {
        if (exclusionVolume == null || !IsInsideExclusion(point)) return point;

        Vector3 surface = exclusionVolume.ClosestPoint(point);
        Vector3 outDir = flatOut.sqrMagnitude > 0.01f ? flatOut : point - exclusionVolume.bounds.center;
        outDir.y = 0f;
        if (outDir.sqrMagnitude < 0.01f)
            outDir = surface.sqrMagnitude > 0.01f ? (point - surface) : Vector3.up;
        outDir.Normalize();

        var candidate = surface + outDir * exclusionMargin;
        if (!IsInsideExclusion(candidate)) return candidate;

        Vector3[] dirs = { flatOut, Vector3.up, Vector3.Cross(Vector3.up, flatOut) };
        foreach (var dir in dirs)
        {
            if (dir.sqrMagnitude < 0.01f) continue;
            var d = dir.normalized;
            var p = point;
            if (!MarchOutOfExclusion(ref p, d)) continue;
            return p + d * exclusionMargin;
        }

        MarchOutOfExclusion(ref point, outDir);
        return point + outDir * exclusionMargin;
    }

    bool MarchOutOfExclusion(ref Vector3 point, Vector3 direction)
    {
        const float step = 0.08f;
        for (int i = 0; i < 512; i++)
        {
            if (!IsInsideExclusion(point)) return true;
            point += direction * step;
        }
        return !IsInsideExclusion(point);
    }

    float ImpulseScale(Vector3 start, Vector3 outward)
    {
        float verticalDot = Mathf.Clamp(Vector3.Dot(outward.normalized, Vector3.up), -1f, 1f);
        float verticalT = verticalDot * 0.5f + 0.5f;
        float scale = Mathf.Lerp(lowVerticalDotImpulse, highVerticalDotImpulse, verticalT);

        var origin = BlastOrigin;
        if (origin != null)
        {
            float dist = Vector3.Distance(start, origin.position);
            scale *= 1f / (1f + dist * distanceFalloff);
        }
        return Mathf.Max(0.2f, scale);
    }

    Vector3 OutwardDirection(Vector3 start)
    {
        var origin = BlastOrigin;
        if (origin == null) return Vector3.up;
        Vector3 v = start - origin.position;
        if (v.sqrMagnitude < 0.0001f) return Vector3.up;
        return v.normalized;
    }

    Vector3 HorizontalOutward(Vector3 start, int seed)
    {
        var origin = BlastOrigin;
        if (origin == null) return Vector3.forward;
        Vector3 v = start - origin.position;
        v.y = 0f;
        if (v.sqrMagnitude < 0.04f)
        {
            float a = SampleRange(seed + 50, 0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }
        return v.normalized;
    }

    float HeightVariationMultiplier(int seed, float variation)
    {
        float v = Mathf.Clamp(variation, 0.05f, 0.85f);
        float lo = Mathf.Max(0.35f, 1f - v);
        float hi = 1f + v;
        return SampleRange(seed, lo, hi);
    }

    Vector3 SnapLandingY(Vector3 landing, float startHeight)
    {
        landing.y = FindGroundHeight(landing, startHeight) + groundClearance;
        return landing;
    }

    bool StaysStill(Transform piece)
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
        SanitizeOwnedReferences();
        StopActiveTweens();
#if UNITY_EDITOR
        if (!Application.isPlaying) EditorStopPreviewScatter(false);
#endif
        EnsureStartCached();
        RestoreStartState();
        partialExplosionStep = 0;
        nextPieceIndex = 0;
    }

#if UNITY_EDITOR
    [ContextMenu("Create Exclusion Volume Child")]
    void EditorCreateExclusionVolumeChild()
    {
        if (exclusionVolume != null && IsUnderScatter(exclusionVolume.transform, transform))
        {
            UnityEditor.Selection.activeTransform = exclusionVolume.transform;
            return;
        }

        var go = new GameObject("ExclusionVolume");
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Create Exclusion Volume");
        go.transform.SetParent(transform, false);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(8f, 4f, 8f);
        exclusionVolume = box;
        UnityEditor.Selection.activeGameObject = go;
    }

    [ContextMenu("Editor Test Scatter")]
    public void EditorStartPreviewScatter()
    {
        if (Application.isPlaying)
        {
            Explode();
            return;
        }

        if (!HasSetup()) return;

        SanitizeOwnedReferences();
        EditorStopPreviewScatter(false);
        StopActiveTweens();
        EnsureStartCached();
        RestoreStartState();
        RegisterEditorPreviewUndo();

        editorDriverAnims.Clear();
        var handled = new HashSet<Transform>();
        double start = UnityEditor.EditorApplication.timeSinceStartup;
        editorPreviewStart = start;

        foreach (var group in mergeGroups)
        {
            if (group?.driver == null || StaysStill(group.driver) || handled.Contains(group.driver)) continue;
            handled.Add(group.driver);
            if (group.followers != null)
                foreach (var f in group.followers)
                    if (f != null) handled.Add(f);
            if (TryPrepareDriverAnim(group.driver, group.followers, HashSeed(group.driver), out var anim))
            {
                ApplyScatterFrame(anim, group.driver, 0f);
                editorDriverAnims.Add(new EditorDriverAnim(anim, start));
            }
        }

        foreach (var piece in pieces)
        {
            if (piece == null || handled.Contains(piece) || StaysStill(piece)) continue;
            if (TryPrepareDriverAnim(piece, null, HashSeed(piece), out var anim))
            {
                ApplyScatterFrame(anim, piece, 0f);
                editorDriverAnims.Add(new EditorDriverAnim(anim, start));
            }
        }

        if (editorDriverAnims.Count == 0) return;

        editorPreviewActive = true;
        partialExplosionStep = 4;
        nextPieceIndex = pieces.Length;
        UnityEditor.EditorApplication.update -= EditorPreviewTick;
        UnityEditor.EditorApplication.update += EditorPreviewTick;
    }

    public void EditorStopPreviewScatter(bool restoreTransforms)
    {
        UnityEditor.EditorApplication.update -= EditorPreviewTick;
        editorPreviewActive = false;
        editorDriverAnims.Clear();
        if (restoreTransforms) RestoreStartState();
        UnityEditor.SceneView.RepaintAll();
    }

    void RegisterEditorPreviewUndo()
    {
        UnityEditor.Undo.RegisterCompleteObjectUndo(this, "Test Scatter");
        if (pieces != null)
            foreach (var piece in pieces)
            {
                if (piece == null) continue;
                UnityEditor.Undo.RegisterCompleteObjectUndo(piece, "Test Scatter");
            }
    }

    void EditorPreviewTick()
    {
        if (Application.isPlaying || !editorPreviewActive) return;

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        bool anyRunning = false;

        foreach (var entry in editorDriverAnims)
        {
            var anim = entry.anim;
            if (anim.driver == null) continue;

            double elapsed = now - entry.riseStart;
            if (elapsed < anim.totalDuration)
            {
                float u = anim.totalDuration > 0.0001 ? (float)(elapsed / anim.totalDuration) : 1f;
                ApplyScatterFrame(anim, anim.driver, u);
                anyRunning = true;
                continue;
            }

            ApplyScatterFrame(anim, anim.driver, 1f);
        }

        if (!anyRunning)
            EditorStopPreviewScatter(false);
        else
        {
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }
    }

#endif

    bool HasSetup()
    {
        if (pieces != null && pieces.Length > 0 && (!gatherAtGroundPoint || GroundReference != null)) return true;
        Debug.LogWarning(gatherAtGroundPoint
            ? "ExplosionScatter needs pieces and a Ground Point child on '" + name + "' when gathering."
            : "ExplosionScatter needs pieces.", this);
        return false;
    }

    float FindGroundHeight(Vector3 landing, float startHeight)
    {
        Ray ray = new Ray(landing + Vector3.up * raycastHeight, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, raycastHeight * 2f, groundLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        var ground = GroundReference;
        return ground != null ? ground.position.y : startHeight;
    }

    float RandomizedDuration(float value, int seed)
    {
        if (Application.isPlaying)
            return value * UnityEngine.Random.Range(1f - durationVariation, 1f + durationVariation);
        return value * DeterministicRange(seed + 40, 1f - durationVariation, 1f + durationVariation);
    }

    void EnsureStartCached()
    {
        if (HasCachedStartState()) return;
        CacheStartState();
    }

    bool HasCachedStartState()
    {
        if (pieces == null || pieces.Length == 0) return restPoses.Count == 0;
        var required = 0;
        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            required++;
            if (!restPoses.ContainsKey(piece)) return false;
        }
        return required > 0;
    }

    void CacheStartState()
    {
        restPoses.Clear();
        followerStartsByDriver.Clear();
        groupedFollowers.Clear();

        RebuildFollowerIndex();

        if (pieces == null) return;
        foreach (Transform piece in pieces)
        {
            if (piece == null) continue;
            restPoses[piece] = new RestPose
            {
                parent = piece.parent,
                localPosition = piece.localPosition,
                localRotation = piece.localRotation,
                position = piece.position,
                rotation = piece.rotation
            };
        }
    }

    void RebuildFollowerIndex()
    {
        groupedFollowers.Clear();
        if (mergeGroups == null) return;
        foreach (var group in mergeGroups)
        {
            if (group?.followers == null) continue;
            foreach (var f in group.followers)
                if (f != null) groupedFollowers.Add(f);
        }
    }

    void RestoreStartState()
    {
        if (restPoses.Count == 0) return;
        foreach (var pair in restPoses)
        {
            if (pair.Key == null) continue;
            if (pair.Value.parent != null && pair.Key.parent == pair.Value.parent)
                pair.Key.SetLocalPositionAndRotation(pair.Value.localPosition, pair.Value.localRotation);
            else
                pair.Key.SetPositionAndRotation(pair.Value.position, pair.Value.rotation);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(pair.Key);
#endif
        }
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.SceneView.RepaintAll();
#endif
    }

    void StopActiveTweens()
    {
        for (int i = 0; i < activeTweens.Count; i++)
            if (activeTweens[i].isAlive) activeTweens[i].Stop();
        activeTweens.Clear();
    }

    void OnDisable()
    {
        StopActiveTweens();
#if UNITY_EDITOR
        if (!Application.isPlaying) EditorStopPreviewScatter(false);
#endif
    }

    public void ClearMergeGroups()
    {
        mergeGroups.Clear();
        RebuildFollowerIndex();
    }

    /// <summary>Editor only: clusters nearby pieces so they share one tween at runtime.</summary>
    [ContextMenu("Scan & Build Merge Groups")]
    public int RebuildMergeGroupsFromPieces()
    {
        mergeGroups ??= new List<MergeGroup>();
        mergeGroups.Clear();
        groupedFollowers.Clear();

        if (pieces == null || pieces.Length == 0)
        {
            RebuildFollowerIndex();
            return 0;
        }

        var eligible = new List<Transform>();
        foreach (var p in pieces)
            if (p != null && !StaysStill(p)) eligible.Add(p);

        var assigned = new HashSet<Transform>();
        int groupCount = 0;

        foreach (var seed in eligible)
        {
            if (assigned.Contains(seed)) continue;

            var cluster = new List<Transform> { seed };
            assigned.Add(seed);

            var neighbors = new List<(Transform t, float d)>();
            foreach (var other in eligible)
            {
                if (other == seed || assigned.Contains(other)) continue;
                float d = Vector3.Distance(seed.position, other.position);
                if (d <= mergeThreshold) neighbors.Add((other, d));
            }
            neighbors.Sort((a, b) => a.d.CompareTo(b.d));

            int slots = maxPiecesPerMergeGroup - 1;
            for (int i = 0; i < neighbors.Count && i < slots; i++)
            {
                var t = neighbors[i].t;
                cluster.Add(t);
                assigned.Add(t);
            }

            if (cluster.Count == 1)
            {
                // Solos are driven from the pieces list, not a merge group.
                continue;
            }

            Vector3 centroid = Vector3.zero;
            foreach (var t in cluster) centroid += t.position;
            centroid /= cluster.Count;

            Transform driver = cluster[0];
            float best = float.MaxValue;
            foreach (var t in cluster)
            {
                float d = (t.position - centroid).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    driver = t;
                }
            }

            var followers = new List<Transform>();
            foreach (var t in cluster)
                if (t != driver) followers.Add(t);

            mergeGroups.Add(new MergeGroup { driver = driver, followers = followers.ToArray() });
            groupCount++;
        }

        RebuildFollowerIndex();
        return groupCount;
    }

    void OnDrawGizmos()
    {
        if (!drawTrajectoryGizmos || !drawTrajectoryWhenNotSelected) return;
        DrawScatterGizmos();
    }

    void OnDrawGizmosSelected()
    {
        if (!drawTrajectoryGizmos) return;
        DrawScatterGizmos();
        DrawBlastGizmos();
    }

    void DrawBlastGizmos()
    {
        var origin = BlastOrigin;
        if (origin != null)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.05f, 0.95f);
            Gizmos.DrawWireSphere(origin.position, 0.15f);
            Gizmos.DrawLine(origin.position, origin.position + Vector3.up * 0.6f);
        }
        var ground = GroundReference;
        if (ground != null && !gatherAtGroundPoint)
            DrawFlatRadius(ground.position, landingRadius, new Color(0.2f, 0.85f, 0.35f, 0.85f));

        if (TryGetExclusionBounds(out var bounds))
        {
            Gizmos.color = new Color(0.2f, 0.75f, 1f, 0.85f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }

    void DrawScatterGizmos()
    {
        if (pieces == null || pieces.Length == 0) return;
        EnsureStartCached();

        int stride = Mathf.Max(1, pieces.Length / Mathf.Max(1, gizmoTrajectorySampleCount));

        for (int i = 0; i < pieces.Length; i += stride)
        {
            var piece = pieces[i];
            if (piece == null || StaysStill(piece) || groupedFollowers.Contains(piece)) continue;

            int seed = HashSeed(piece);
            Vector3 start;
            Quaternion startRotation;
            if (restPoses.TryGetValue(piece, out var rest))
            {
                start = rest.position;
                startRotation = rest.rotation;
            }
            else
            {
                start = piece.position;
                startRotation = piece.rotation;
            }

            var path = BuildScatterPath(start, startRotation, seed);

            DrawCube(start, new Color(0.2f, 0.95f, 0.35f, 0.9f));
            DrawCube(path.apex, new Color(1f, 0.55f, 0.1f, 0.9f));
            DrawCube(path.landing, new Color(0.95f, 0.25f, 0.2f, 0.9f));

            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Vector3 prev = start;
            const int curveSegments = 18;
            for (int s = 1; s <= curveSegments; s++)
            {
                float curveT = s / (float)curveSegments;
                Vector3 p = EvaluateScatterArc(start, path.apex, path.landing, curveT);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }

            if (rotationVariance >= 0.01f)
            {
                var endRot = startRotation * path.tumbleDelta;
                Vector3 tip = path.landing + endRot * Vector3.forward * gizmoCubeSize * 4f;
                Gizmos.color = new Color(0.75f, 0.5f, 1f, 0.7f);
                Gizmos.DrawLine(path.landing, tip);
            }

            var origin = BlastOrigin;
            if (origin != null)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.25f);
                Gizmos.DrawLine(origin.position, start);
            }
        }
    }

    void DrawCube(Vector3 center, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, Vector3.one * gizmoCubeSize);
    }

    static void DrawFlatRadius(Vector3 center, float radius, Color color)
    {
        Gizmos.color = color;
        const int segments = 32;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float b = (i + 1) * Mathf.PI * 2f / segments;
            Gizmos.DrawLine(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius,
                center + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius);
        }
    }

    static int HashSeed(Transform t) => t == null ? 0 : t.GetInstanceID();

    float SampleRange(int seed, float min, float max)
    {
        if (Application.isPlaying) return UnityEngine.Random.Range(min, max);
        return DeterministicRange(seed, min, max);
    }

    static float DeterministicRange(int seed, float min, float max) =>
        Mathf.Lerp(min, max, Hash01(seed));

    static float Hash01(int seed)
    {
        uint u = (uint)seed;
        u ^= u << 13;
        u ^= u >> 17;
        u ^= u << 5;
        return (u & 0x00FFFFFFu) / 16777215f;
    }

}