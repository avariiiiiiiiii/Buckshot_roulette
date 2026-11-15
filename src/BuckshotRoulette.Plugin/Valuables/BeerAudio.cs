using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BuckshotRoulette.Valuables;

/// <summary>Native physics audio uses the game's existing per-client Break/Destroy RPCs.</summary>
internal static class BeerAudio
{
    private static AudioClip[] _hits;
    private static AudioClip _spill;

    public static void Configure(ValuableObject valuable)
    {
        _hits ??= new[] { LoadPcm("beer_hit_1.wav"), LoadPcm("beer_hit_2.wav") };
        _spill ??= LoadPcm("beer_spill.wav");
        var preset = valuable.audioPreset != null ? UnityEngine.Object.Instantiate(valuable.audioPreset)
            : ScriptableObject.CreateInstance<PhysAudio>();
        preset.name = "Buckshot Beer Aluminum and Spill";
        preset.breakLight = Hit(0.55f);
        preset.breakMedium = Hit(0.7f);
        preset.breakHeavy = Hit(0.85f);
        preset.destroy = new Sound { Sounds = new[] { _spill }, Type = AudioManager.AudioType.MaterialImpact,
            Volume = 0.8f, VolumeRandom = 0f, Pitch = 1f, PitchRandom = 0.025f };
        valuable.audioPreset = preset;
        valuable.audioPresetPitch = 1f;
    }

    private static Sound Hit(float volume) => new Sound { Sounds = _hits, Type = AudioManager.AudioType.MaterialImpact,
        Volume = volume, VolumeRandom = 0.04f, Pitch = 1f, PitchRandom = 0.08f };

    private static AudioClip LoadPcm(string name)
    {
        string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "Audio", name);
        using var file = new BinaryReader(File.OpenRead(path));
        string FourCc() => Encoding.ASCII.GetString(file.ReadBytes(4));
        if (FourCc() != "RIFF") throw new InvalidDataException("Invalid WAV: " + name);
        file.ReadUInt32();
        if (FourCc() != "WAVE") throw new InvalidDataException("Invalid WAV: " + name);
        ushort format = 0, channels = 0, bits = 0;
        int rate = 0;
        byte[] data = null;
        while (file.BaseStream.Position + 8 <= file.BaseStream.Length)
        {
            string chunk = FourCc();
            uint size = file.ReadUInt32();
            long end = file.BaseStream.Position + size;
            if (end > file.BaseStream.Length) throw new InvalidDataException("Truncated WAV: " + name);
            if (chunk == "fmt " && size >= 16)
            {
                format = file.ReadUInt16(); channels = file.ReadUInt16(); rate = file.ReadInt32();
                file.ReadInt32(); file.ReadUInt16(); bits = file.ReadUInt16();
            }
            else if (chunk == "data") data = file.ReadBytes(checked((int)size));
            file.BaseStream.Position = end + (size & 1);
        }
        if (format != 1 || channels != 1 || bits != 16 || rate != 44100 || data == null || data.Length % 2 != 0)
            throw new InvalidDataException("Expected 44.1 kHz mono PCM16: " + name);
        var samples = new float[data.Length / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8)) / 32768f;
        var clip = AudioClip.Create("Buckshot " + name, samples.Length, channels, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
