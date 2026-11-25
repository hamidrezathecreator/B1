using UnityEngine;

public static class PlayerImpactAudio
{
    const int SampleRate = 22050;
    static AudioClip cloudHitClip;
    static AudioClip fallClip;
    static AudioSource fallSource;
    static AudioClip missileWhooshClip;
    static AudioClip fighterJetClip;

    public static void PlayFighterJet(AudioClip clip = null, float volume = 0.85f)
    {
        if (clip == null)
        {
            if (fighterJetClip == null) fighterJetClip = CreateAirPass(true);
            clip = fighterJetClip;
        }
        Play(clip, volume, "Fighter Jet Flyby");
    }

    public static AudioSource PlayMissileWhoosh(Transform missile, AudioClip clip = null, float volume = 0.8f)
    {
        if (missile == null || volume <= 0f) return null;
        if (clip == null)
        {
            if (missileWhooshClip == null) missileWhooshClip = CreateAirPass(false);
            clip = missileWhooshClip;
        }
        var source = new GameObject("Missile Whoosh").AddComponent<AudioSource>();
        source.transform.SetParent(missile, false);
        source.playOnAwake = false;
        source.spatialBlend = 0.7f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 20f;
        source.maxDistance = 1800f;
        source.dopplerLevel = 0f;
        source.volume = Mathf.Clamp01(volume);
        source.clip = clip;
        source.loop = true;
        source.Play();
        return source;
    }

    public static void StopFall()
    {
        if (fallSource == null) return;
        fallSource.Stop();
        Object.Destroy(fallSource.gameObject);
        fallSource = null;
    }

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
        fallSource = Play(clip, volume, "Player Falling Sound", true);
    }

    static AudioSource Play(AudioClip clip, float volume, string objectName, bool loop = false)
    {
        var source = new GameObject(objectName).AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.volume = Mathf.Clamp01(volume);
        source.clip = clip;
        source.loop = loop;
        source.Play();
        if (!loop) Object.Destroy(source.gameObject, clip.length);
        return source;
    }

    static AudioClip CreateAirPass(bool jet)
    {
        float seconds = jet ? 5.5f : 1.6f;
        var samples = new float[Mathf.CeilToInt(seconds * SampleRate) * 2];
        var random = new System.Random(jet ? 3721 : 4259);
        float filtered = 0f;
        float phase = 0f;
        for (int sampleIndex = 0; sampleIndex < samples.Length / 2; sampleIndex++)
        {
            float time = sampleIndex / (float)SampleRate;
            float progress = time / seconds;
            float noise = (float)random.NextDouble() * 2f - 1f;
            filtered = Mathf.Lerp(filtered, noise, jet ? 0.16f : 0.4f);
            float envelope = jet ? Mathf.Pow(Mathf.Sin(Mathf.PI * progress), 1.8f) : 0.85f;
            phase += 2f * Mathf.PI * Mathf.Lerp(jet ? 145f : 320f, jet ? 65f : 210f, progress) / SampleRate;
            float roar = filtered * 0.78f + Mathf.Sin(phase) * 0.13f + Mathf.Sin(phase * 2.03f) * 0.09f;
            float pan = jet ? Mathf.Lerp(-0.85f, 0.85f, progress) : 0f;
            samples[sampleIndex * 2] = roar * envelope * Mathf.Sqrt((1f - pan) * 0.5f);
            samples[sampleIndex * 2 + 1] = roar * envelope * Mathf.Sqrt((1f + pan) * 0.5f);
        }
        var clip = AudioClip.Create(jet ? "Fighter Jet Pass" : "Missile Air Whoosh", samples.Length / 2, 2, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
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
