using UnityEngine;

namespace BuckshotRoulette.Valuables;

/// <summary>Звонок старого телефона, синтезируется кодом: две ноты 440/480 Гц, прерывистые пачки с дрожанием 20 Гц.</summary>
internal static class RingTone
{
    private const int SampleRate = 44100;
    private const float Seconds = 2.4f;

    public static AudioClip Create()
    {
        int count = (int)(SampleRate * Seconds);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            // две пачки по 0.8 с и пауза 0.4 с между ними
            float phase = t % 1.2f;
            if (phase > 0.8f) continue;
            float envelope = Mathf.Clamp01(phase / 0.02f) * Mathf.Clamp01((0.8f - phase) / 0.02f);
            float tremolo = 0.5f + 0.5f * Mathf.Sign(Mathf.Sin(t * Mathf.PI * 2f * 20f));
            float tone = Mathf.Sin(t * Mathf.PI * 2f * 440f) + Mathf.Sin(t * Mathf.PI * 2f * 480f);
            data[i] = 0.4f * envelope * (0.35f + 0.65f * tremolo) * tone;
        }
        var clip = AudioClip.Create("BuckshotRoulette Ring", count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
