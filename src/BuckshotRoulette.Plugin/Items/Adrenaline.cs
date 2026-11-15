using BuckshotRoulette.Core;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Items;

internal sealed class Adrenaline : MonoBehaviour
{
    private PhysGrabObject _grab;
    private PhotonView _view;
    private ItemAttributes _attributes;
    private bool _used;
    private float _nextRequest;
    private float _removeAt;
    private bool _destroyRequested;
    private static Sound _injection;

    private void Awake()
    {
        _grab = GetComponent<PhysGrabObject>(); _view = GetComponent<PhotonView>();
        _attributes = GetComponent<ItemAttributes>();
    }

    private void Update()
    {
        if (_grab == null) return;
        if (_used)
        {
            if (!_destroyRequested && SemiFunc.IsMasterClientOrSingleplayer() && Time.unscaledTime >= _removeAt)
            {
                _destroyRequested = true;
                _grab.DestroyPhysGrabObject();
            }
            return;
        }
        if (SemiFunc.IsMasterClientOrSingleplayer() && _grab.playerGrabbing.Count > 0)
        {
            _grab.TurnXYZ(Quaternion.Euler(20f, 0f, 0f), Quaternion.identity, Quaternion.identity);
            _grab.OverrideGrabVerticalPosition(-.2f);
        }
        if (!_grab.heldByLocalPlayer || !SemiFunc.RunIsLevel()) return;
        string key = SemiFunc.InputDisplayReplaceTags("[interact]");
        SemiFunc.UIItemInfoText(null, Texts.T($"{key} Ввести адреналин", $"{key} Inject adrenaline"));
        if (Time.unscaledTime < _nextRequest || PlayerController.instance == null || PlayerController.instance.InputDisableTimer > 0f
            || !SemiFunc.InputDown(InputKey.Interact)) return;
        _nextRequest = Time.unscaledTime + .5f;
        if (SemiFunc.IsMultiplayer()) _view.RPC(nameof(UseRequestRPC), RpcTarget.MasterClient);
        else Use(PlayerAvatar.instance);
    }

    [PunRPC]
    private void UseRequestRPC(PhotonMessageInfo info = default)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || info.Sender == null || _used) return;
        foreach (var grabber in _grab.playerGrabbing)
        {
            var player = grabber != null ? grabber.playerAvatar : null;
            if (player == null || player.photonView == null || player.photonView.OwnerActorNr != info.Sender.ActorNumber) continue;
            Use(player); return;
        }
    }

    private void Use(PlayerAvatar player)
    {
        if (_used || !SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsLevel() || _attributes.shopItem
            || player == null || player.deadSet || player.isDisabled || StatsManager.instance == null
            || string.IsNullOrEmpty(_attributes.instanceName) || !StatsManager.instance.item.ContainsKey(_attributes.instanceName)
            || AdrenalineTime.Instance == null || !AdrenalineTime.Instance.TryActivate(player)) return;
        _used = true;
        StatsManager.instance.ItemUpdateStatBattery(_attributes.instanceName, 0);
        StatsManager.instance.ItemRemove(_attributes.instanceName);
        _attributes.DisableUI(_disable: true);
        if (SemiFunc.IsMultiplayer()) _view.RPC(nameof(InjectedRPC), RpcTarget.All);
        else Injected();
        Plugin.Log.LogInfo($"Адреналин: {player.name}, 10 реальных секунд, мир ×0.1");
    }

    [PunRPC]
    private void InjectedRPC(PhotonMessageInfo info = default)
    {
        if (info.Sender != PhotonNetwork.MasterClient) return;
        Injected();
    }

    private void Injected()
    {
        _used = true;
        _removeAt = Time.unscaledTime + .35f;
        _attributes.DisableUI(_disable: true);
        _injection ??= new Sound { Sounds = new[] { InjectionClip() }, Volume = .5f, PitchRandom = .025f, VolumeRandom = 0f };
        _injection.Play(transform.position);
    }

    private static AudioClip InjectionClip()
    {
        const int rate = 44100; const float length = .3f;
        var rng = new System.Random(123); var samples = new float[(int)(rate * length)];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / rate;
            float hiss = ((float)rng.NextDouble() * 2f - 1f) * .22f * Mathf.Exp(-t * 13f);
            float click = Mathf.Sin(t * Mathf.PI * 2f * 750f) * .4f * Mathf.Exp(-t * 160f);
            samples[i] = (hiss + click) * Mathf.Clamp01(t / .001f);
        }
        var clip = AudioClip.Create("Buckshot adrenaline injection", samples.Length, 1, rate, false);
        clip.SetData(samples, 0); return clip;
    }
}
