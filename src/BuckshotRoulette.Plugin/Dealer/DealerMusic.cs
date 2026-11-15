using System.Collections;
using System.IO;
using BuckshotRoulette.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace BuckshotRoulette.Dealer;

/// <summary>Музыка General (Bleyzi) вокруг дилера: 3D-звук, слышно только в радиусе, глушится стенами как остальные звуки игры.</summary>
internal sealed class DealerMusic : MonoBehaviour
{
    private static AudioClip _clip;
    private static bool _loading;

    private AudioSource _source;

    private static string TrackPath => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "Audio", "general.ogg");

    private void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = true;
        _source.spatialBlend = 1f;
        _source.dopplerLevel = 0f;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = Settings.MusicNearDistance.Value;
        _source.maxDistance = Settings.MusicRadius.Value;
        _source.volume = Settings.MusicVolume.Value;
        if (AudioManager.instance != null) _source.outputAudioMixerGroup = AudioManager.instance.SoundMasterGroup;

        var lowPass = gameObject.AddComponent<AudioLowPassLogic>();
        lowPass.ForceStart = true;
        lowPass.AlwaysActive = true;
    }

    private void OnEnable()
    {
        if (_clip == null && !_loading) Plugin.Instance.StartCoroutine(Load());
    }

    private void Update()
    {
        if (_clip == null) return;
        if (_source.clip == null) _source.clip = _clip;
        if (!_source.isPlaying) _source.Play();
    }

    private static IEnumerator Load()
    {
        _loading = true;
        string uri = "file:///" + TrackPath.Replace('\\', '/');
        using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogError($"Не удалось загрузить музыку дилера ({TrackPath}): {request.error}");
            }
            else
            {
                _clip = DownloadHandlerAudioClip.GetContent(request);
                _clip.name = "General";
                Plugin.Log.LogInfo($"Музыка дилера загружена: {_clip.length:F1} с");
            }
        }
        _loading = false;
    }
}
