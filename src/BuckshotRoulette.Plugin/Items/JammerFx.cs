using UnityEngine;

namespace BuckshotRoulette.Items;

internal static class JammerFx
{
    private static Sound _pulse;

    public static void Play(Vector3 position)
    {
        _pulse ??= new Sound { Sounds = new[] { CreateClip() }, Volume = 0.65f, VolumeRandom = 0f, PitchRandom = 0f };
        _pulse.Play(position);
    }

    private static AudioClip CreateClip()
    {
        const int rate = 44100;
        const float duration = 0.65f;
        var samples = new float[Mathf.CeilToInt(rate * duration)];
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / rate;
            float frequency = 1800f + 1100f * Mathf.Sin(t * Mathf.PI * 9f);
            phase += frequency * Mathf.PI * 2f / rate;
            float envelope = Mathf.Clamp01(t / 0.015f) * Mathf.Clamp01((duration - t) / 0.12f);
            float bursts = 0.45f + 0.55f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 24f), 2f);
            samples[i] = (Mathf.Sin(phase) + Mathf.Sin(phase * 2f) * 0.12f) * envelope * bursts * 0.45f;
        }
        var clip = AudioClip.Create("BuckshotRoulette Repeller Pulse", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
