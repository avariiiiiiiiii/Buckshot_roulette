using BuckshotRoulette.Core;
using Strobotnik.Klattersynth;
using UnityEngine;
using UnityEngine.Audio;

namespace BuckshotRoulette.Valuables;

/// <summary>Роботный голос игры (синтезатор Klatter, тот же, что у чата) из точки в мире. Работает локально на каждом клиенте.</summary>
internal static class RobotVoice
{
    // A very low fundamental makes the short monster names sound like a growl.
    // Keep the game's robot timbre, with a steady pitch and a slower articulation.
    private const int BaseFrequency = 170;
    private const float MouthLifetime = 14f;

    public static void Speak(Transform at, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return;

        string localeCode = Resolve(ref phrase);
        string speechText = TTSFormatting.GetSpeechText(phrase, localeCode);
        if (string.IsNullOrWhiteSpace(speechText)) return;

        var mouth = new GameObject("BuckshotRoulette TTS Mouth");
        mouth.SetActive(false);
        mouth.transform.SetParent(at, false);

        var source = mouth.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.volume = 1f;
        source.spatialBlend = 0.85f;
        source.dopplerLevel = 0f;
        source.bypassReverbZones = true;
        source.priority = 0;
        source.minDistance = 6f;
        source.maxDistance = 30f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.outputAudioMixerGroup = MixerGroup();

        var lowPass = mouth.AddComponent<AudioLowPassLogic>();
        lowPass.ForceStart = true;
        lowPass.AlwaysActive = true;
        lowPass.HasCustomVolume = true;
        lowPass.CustomVolume = 0.85f;
        mouth.GetComponent<AudioLowPassFilter>().cutoffFrequency = 22000f;

        var speech = mouth.AddComponent<Speech>();
        speech.voiceBaseFrequency = BaseFrequency;
        speech.msPerSpeechFrame = 16;
        speech.flutter = 0;
        mouth.SetActive(true);

        speech.speak(BaseFrequency, SpeechSynth.VoicingSource.natural, speechText, bracketsAsPhonemes: true, phrase);
        Object.Destroy(mouth, MouthLifetime);
    }

    /// <summary>Локаль озвучки: латиница читается английскими правилами, кириллица — русскими.</summary>
    private static string Resolve(ref string phrase)
    {
        foreach (char c in phrase)
        {
            if (c >= 'Ѐ' && c <= 'ӿ') return "ru-RU";
        }
        return TTSPronunciation.ResolveLocale(ref phrase, "en-US");
    }

    private static AudioMixerGroup MixerGroup()
    {
        var prefab = Resources.Load<GameObject>("TTS");
        if (prefab != null) return prefab.GetComponent<AudioSource>().outputAudioMixerGroup;
        Plugin.Log.LogWarning("Префаб TTS не найден: голос без микшера");
        return null;
    }
}
