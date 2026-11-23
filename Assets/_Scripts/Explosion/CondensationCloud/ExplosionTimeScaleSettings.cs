using UnityEngine;

/// <summary>Global explosion playback time scale (slow-mo ramp), authored on <see cref="CondensationCloudSystem"/>.</summary>
public static class ExplosionTimeScaleSettings
{
    public readonly struct Profile
    {
        public readonly float Start;
        public readonly float End;
        public readonly float RampDuration;

        public Profile(float start, float end, float rampDuration)
        {
            Start = Mathf.Max(0f, start);
            End = Mathf.Max(0f, end);
            RampDuration = Mathf.Max(0f, rampDuration);
        }

        public static Profile Identity => new Profile(1f, 1f, 0f);

        public float Evaluate(float wallElapsedSincePlaybackStart)
        {
            if (RampDuration <= 0f || Mathf.Approximately(Start, End)) return End;
            float t = Mathf.Clamp01(wallElapsedSincePlaybackStart / RampDuration);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(Start, End, t);
        }
    }

    public static Profile Current { get; private set; } = Profile.Identity;

    public static void Apply(Profile profile) => Current = profile;
    public static void Clear() => Current = Profile.Identity;
}