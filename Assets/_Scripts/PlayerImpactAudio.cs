using UnityEngine;

public static class PlayerImpactAudio
{
    const int SampleRate = 22050;
    static AudioClip cloudHitClip;
    static AudioClip fallClip;
    static AudioSource fallSource;

    public static void PlayCloudHit(AudioClip clip = null, float volume = 0.7f)
    {
        if (volume <= 0f) return;
        if (clip == null)
        {
            if (cloudHitClip == null) cloudHitClip = CreateClip(false);
            clip = cloudHitClip;
        }
        Play(clip, volume, "Condensation Cloud Hit Sound");
    }

    public static void PlayFall(AudioClip clip = null, float volume = 0.65f)
    {
        if (volume <= 0f || (fallSource != null && fallSource.isPlaying)) return;
        if (clip == null)
        {
            if (fallClip == null) fallClip = CreateClip(true);
            clip = fallClip;
        }
        fallSource = Play(clip, volume, "Player Falling Sound");
    }

    static AudioSource Play(AudioClip clip, float volume, string objectName)
    {
        var source = new GameObject(objectName).AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.volume = Mathf.Clamp01(volume);
        source.clip = clip;
        source.Play();
        Object.Destroy(source.gameObject, clip.length);
        return source;
    }

    static AudioClip CreateClip(bool falling)
    {
        var seconds = falling ? 2.5f : 0.7f;
        var samples = new float[Mathf.CeilToInt(seconds * SampleRate)];
        var random = new System.Random(falling ? 731 : 173);
        var filteredNoise = 0f;
        for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            var time = sampleIndex / (float)SampleRate;
            var progress = time / seconds;
            var noise = (float)random.NextDouble() * 2f - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, falling ? 0.12f + progress * 0.4f : 0.18f);
            var attack = Mathf.Clamp01(time / (falling ? 0.25f : 0.01f));
            var fade = Mathf.Clamp01((seconds - time) / (falling ? 0.4f : 0.08f));
            if (falling)
            {
                var flutter = 0.85f + 0.15f * Mathf.Sin(time * 24f);
                samples[sampleIndex] = filteredNoise * attack * fade * flutter * (0.3f + 0.7f * progress);
            }
            else
            {
                var thump = Mathf.Sin(2f * Mathf.PI * (65f * time - 18f * time * time));
                samples[sampleIndex] = (filteredNoise * 0.65f + thump * 0.35f) * attack * fade * Mathf.Exp(-7f * time);
            }
        }
        var clip = AudioClip.Create(falling ? "Falling Wind" : "Cloud Pressure Hit", samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
