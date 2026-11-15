using UnityEngine;

namespace BuckshotRoulette.Items;

internal static class HandcuffsFx
{
    private static Sound _snap;

    public static void Play(Vector3 position)
    {
        _snap ??= new Sound
        {
            Sounds = new[] { CreateSnap() },
            Volume = 0.75f,
            VolumeRandom = 0f,
            PitchRandom = 0.025f
        };
        _snap.Play(position);
    }

    private static AudioClip CreateSnap()
    {
        const int sampleRate = 44100;
        const float duration = 0.28f;
        int samples = Mathf.CeilToInt(sampleRate * duration);
        var data = new float[samples];
        var random = new System.Random(4817);

        for (int i = 0; i < samples; i++)
        {
            float time = (float)i / sampleRate;
            float sound = Click(time, 0f, 1f, random)
                + Click(time, 0.045f, 0.58f, random);
            float fadeIn = Mathf.Clamp01(time / 0.001f);
            float fadeOut = Mathf.Clamp01((duration - time) / 0.045f);
            data[i] = Mathf.Clamp(sound * fadeIn * fadeOut * 0.65f, -1f, 1f);
        }

        var clip = AudioClip.Create("BuckshotRoulette Handcuff Snap", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static float Click(float time, float start, float volume, System.Random random)
    {
        float local = time - start;
        if (local < 0f) return 0f;
        float noise = ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-local * 95f);
        float ring = Mathf.Sin(local * Mathf.PI * 2f * 1450f) * Mathf.Exp(-local * 24f) * 0.55f
            + Mathf.Sin(local * Mathf.PI * 2f * 3270f) * Mathf.Exp(-local * 38f) * 0.22f;
        float lowClack = Mathf.Sin(local * Mathf.PI * 2f * 420f) * Mathf.Exp(-local * 43f) * 0.45f;
        return (noise * 0.3f + ring + lowClack) * volume;
    }
}
