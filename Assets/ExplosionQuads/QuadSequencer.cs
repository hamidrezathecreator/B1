using UnityEngine;

public enum QuadFacingAxis
{
    PositiveZ,
    NegativeZ,
    PositiveX,
    NegativeX,
    PositiveY,
    NegativeY
}

/// <summary>
/// Drives a flipbook-style sprite-sheet material (see SequenceShader) as a single one-shot
/// playback and reports when it has finished. Owns its own Material instance so multiple
/// vehicles/effects sharing the same source material never fight over shader state.
///
/// One-shot frame count:
/// The sheet is laid out as a _TilesX x _TilesY grid, but the *playable* sequence doesn't have
/// to use every tile (e.g. a 4x4 sheet with a few trailing padding tiles, or a sheet packing
/// more than one sequence). This is resolved automatically from the material's own _FrameCount
/// property if the shader defines one, or — if it doesn't — by playing every tile in the grid.
/// </summary>
public class QuadSequncerController : MonoBehaviour
{
    [Header("Facing")]
    [Tooltip("Local mesh axis that should point toward the camera when billboarding. Used by the quad emitter and missile impact system.")]
    [SerializeField] private QuadFacingAxis facingAxis = QuadFacingAxis.PositiveZ;

    [Header("Position")]
    [Tooltip("World-space vertical offset added when this quad is spawned. Positive raises it; negative lowers it.")]
    [SerializeField] private float worldYOffset;

    public QuadFacingAxis FacingAxis => facingAxis;
    public float WorldYOffset => worldYOffset;

    // Cached property IDs — resolved once instead of re-hashing the string on every access.
    private static readonly int TilesXId         = Shader.PropertyToID("_TilesX");
    private static readonly int TilesYId         = Shader.PropertyToID("_TilesY");
    private static readonly int AnimationSpeedId = Shader.PropertyToID("_AnimationSpeed");
    private static readonly int StartTimeId      = Shader.PropertyToID("_StartTime");
    private static readonly int LoopId           = Shader.PropertyToID("_Loop");
    private static readonly int FrameCountId     = Shader.PropertyToID("_FrameCount");

    private Material mat;
    private float startTime;
    private float lifetime;
    private bool isPlaying;

    /// <summary>Frames-per-second this sequence plays at, as read from the material.</summary>
    public float FrameRate { get; private set; }

    /// <summary>Number of frames actually played in one shot (may be less than the full grid).</summary>
    public int FrameCount { get; private set; }

    /// <summary>Total tiles available on the sheet (_TilesX * _TilesY), regardless of FrameCount.</summary>
    public int GridFrameCount { get; private set; }

    /// <summary>
    /// Creates a private material instance from <paramref name="explosionMaterial"/>, assigns it
    /// to this object's Renderer, and configures it for non-looping one-shot playback.
    /// </summary>
    /// <param name="explosionMaterial">Source material using SequenceShader (or a compatible shader).</param>
    public void Initialize(Material explosionMaterial)
    {
        if (explosionMaterial == null)
        {
            Debug.LogError($"[QuadSequencer] {name} Initialize called with a null material.", this);
            return;
        }

        var targetRenderer = GetComponent<Renderer>();
        if (targetRenderer == null)
        {
            Debug.LogError($"[QuadSequencer] {name} has no Renderer to assign the sequence material to.", this);
            return;
        }

        // Dispose any previously created instance in case Initialize runs more than once
        // on a pooled/reused object.
        if (mat != null)
            Destroy(mat);

        mat = new Material(explosionMaterial);
        targetRenderer.material = mat;

        float tilesX = mat.HasProperty(TilesXId) ? mat.GetFloat(TilesXId) : 1f;
        float tilesY = mat.HasProperty(TilesYId) ? mat.GetFloat(TilesYId) : 1f;
        FrameRate = mat.HasProperty(AnimationSpeedId) ? mat.GetFloat(AnimationSpeedId) : 24f;

        if (FrameRate <= 0f)
        {
            Debug.LogWarning($"[QuadSequencer] {name} material animation speed was <= 0; clamping to 1 FPS.", this);
            FrameRate = 1f;
        }

        GridFrameCount = Mathf.Max(1, Mathf.RoundToInt(tilesX * tilesY));

        // Resolve the final frame count, in priority order:
        //   1. The material's own _FrameCount property, if the shader defines one
        //   2. Fall back to the full tile grid
        int materialFrameCount = mat.HasProperty(FrameCountId) ? Mathf.RoundToInt(mat.GetFloat(FrameCountId)) : 0;
        int resolvedFrameCount = materialFrameCount > 0 ? materialFrameCount : GridFrameCount;

        FrameCount = Mathf.Clamp(resolvedFrameCount, 1, GridFrameCount);

        // Non-looping: this controller always plays a single shot and reports completion via IsPlaying().
        mat.SetFloat(LoopId, 0f);

        if (mat.HasProperty(FrameCountId))
            mat.SetFloat(FrameCountId, FrameCount);

        lifetime = FrameCount / FrameRate;
    }

    /// <summary>Starts (or restarts) the one-shot sequence from frame 0.</summary>
    public void Play()
    {
        if (mat == null)
        {
            Debug.LogWarning($"[QuadSequencer] {name} Play() called before Initialize().", this);
            return;
        }

        startTime = Time.time;
        mat.SetFloat(StartTimeId, startTime);
        isPlaying = true;
    }

    /// <summary>Stops reporting the sequence as playing. Does not rewind the visible frame.</summary>
    public void Stop()
    {
        isPlaying = false;
    }

    /// <summary>True while a shot is currently within its lifetime since the last Play().</summary>
    public bool IsPlaying()
    {
        if (!isPlaying) return false;
        return (Time.time - startTime) < lifetime;
    }

    /// <summary>Stops playback and rewinds to frame 0, ready to Play() again.</summary>
    public void Rewind()
    {
        isPlaying = false;
        startTime = 0f;
        mat?.SetFloat(StartTimeId, 0f);
    }

    /// <summary>
    /// Equivalent to <see cref="Rewind"/>. Kept only for backward compatibility with existing
    /// call sites — new code should call Rewind() instead. The reason: this class is a
    /// MonoBehaviour, and MonoBehaviour.Reset() is a special Unity editor message that fires
    /// automatically whenever this component is added or reset from the Inspector, regardless
    /// of any caller's intent. A public gameplay API sharing that exact name silently inherits
    /// that behavior, which is easy to lose track of. Behavior here is harmless (it just zeroes
    /// playback state), but keep new call sites on Rewind() to avoid the ambiguity.
    /// </summary>
    public void Reset() => Rewind();

    /// <summary>Total playback duration in seconds for one shot (FrameCount / FrameRate).</summary>
    public float GetLifetime()
    {
        return lifetime;
    }

    private void OnDestroy()
    {
        if (mat != null)
            Destroy(mat);
    }
}
