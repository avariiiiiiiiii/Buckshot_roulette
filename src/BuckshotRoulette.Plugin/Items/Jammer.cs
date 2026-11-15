using System.Collections;
using BuckshotRoulette.Core;
using BuckshotRoulette.Logic;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>Три одноразовых импульса. Сохранение заряда использует штатную статистику предмета.</summary>
internal sealed class Jammer : MonoBehaviour
{
    private const float Cooldown = 1.5f;
    private PhysGrabObject _grab;
    private PhotonView _view;
    private ItemAttributes _attributes;
    private ItemBattery _battery;
    private Transform _mesh;
    private readonly MeshRenderer[] _indicators = new MeshRenderer[3];
    private int _charges = 3;
    private bool _ready;
    private bool _spent;
    private float _nextUse;
    private float _nextRequest;
    private float _nextStateRequest;
    private JammerCharges _counter;

    private void Awake()
    {
        _grab = GetComponent<PhysGrabObject>();
        _view = GetComponent<PhotonView>();
        _attributes = GetComponent<ItemAttributes>();
        _battery = GetComponent<ItemBattery>();
        _mesh = transform.Find("Mesh");
        for (int i = 0; i < _indicators.Length; i++)
            _indicators[i] = _mesh.Find("Charge_" + (i + 1)).GetComponent<MeshRenderer>();
    }

    private void LoadSavedCharges()
    {
        _counter = new JammerCharges(StatsManager.instance.GetBatteryLevel(_attributes.instanceName));
        _charges = _counter.Remaining;
        _ready = true;
        UpdateIndicators();
        if (_charges == 0) Spend();
    }

    private void Update()
    {
        if (!_ready)
        {
            if (SemiFunc.IsMasterClientOrSingleplayer() && LevelGenerator.Instance != null && LevelGenerator.Instance.Generated
                && StatsManager.instance != null && !string.IsNullOrEmpty(_attributes.instanceName)
                && StatsManager.instance.item.ContainsKey(_attributes.instanceName))
                LoadSavedCharges();
            else if (SemiFunc.IsMultiplayer() && !SemiFunc.IsMasterClientOrSingleplayer()
                && LevelGenerator.Instance != null && LevelGenerator.Instance.Generated && Time.time >= _nextStateRequest)
            {
                _nextStateRequest = Time.time + 1f;
                _view.RPC(nameof(StateRequestRPC), RpcTarget.MasterClient);
            }
            return;
        }
        if (SemiFunc.IsMasterClientOrSingleplayer() && _charges == 0 && !_spent) Spend();
        if (SemiFunc.IsMasterClientOrSingleplayer() && _grab.playerGrabbing.Count > 0)
        {
            _grab.TurnXYZ(Quaternion.Euler(8f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f), Quaternion.identity);
            _grab.OverrideGrabVerticalPosition(-0.2f);
        }
        if (_spent || _charges <= 0 || !_grab.heldByLocalPlayer || !SemiFunc.RunIsLevel()
            || Time.time < _nextUse || Time.time < _nextRequest || PlayerController.instance == null
            || PlayerController.instance.InputDisableTimer > 0f || !SemiFunc.InputDown(InputKey.Interact)) return;
        if (SemiFunc.IsMultiplayer())
        {
            _nextRequest = Time.time + Cooldown;
            _view.RPC(nameof(UseRequestRPC), RpcTarget.MasterClient);
        }
        else Activate();
    }

    [PunRPC]
    private void UseRequestRPC(PhotonMessageInfo info = default)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || info.Sender == null || !_ready || _spent) return;
        foreach (var grabber in _grab.playerGrabbing)
        {
            var player = grabber != null ? grabber.playerAvatar : null;
            if (player == null || player.photonView == null || player.photonView.OwnerActorNr != info.Sender.ActorNumber
                || player.isDisabled || player.deadSet) continue;
            Activate();
            return;
        }
    }

    private void Activate()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !_ready || _spent || _charges <= 0
            || !SemiFunc.RunIsLevel() || _attributes.shopItem || Time.time < _nextUse
            || !StatsManager.instance.item.ContainsKey(_attributes.instanceName)) return;
        // A client that became master already has the host's last charge snapshot.
        _counter ??= new JammerCharges(_charges * 100 / 3);
        if (!_counter.TryUse(Time.timeAsDouble)) return;
        _nextUse = Time.time + Cooldown;
        _charges = _counter.Remaining;
        StatsManager.instance.ItemUpdateStatBattery(_attributes.instanceName, _counter.SavedPercent);
        var source = _mesh.TransformPoint(new Vector3(0f, 0.021f, 0.025f) * JammerItem.ModelScale);
        int affected = JammerRetreat.Pulse(source, Settings.JammerRadius.Value);
        Plugin.Log.LogInfo($"Глушилка: отступают {affected}, осталось {_charges}/3 зарядов");
        if (SemiFunc.IsMultiplayer()) _view.RPC(nameof(PulseRPC), RpcTarget.All, _charges, source);
        else ApplyPulse(_charges, source);
        if (_charges == 0) Spend();
    }

    [PunRPC]
    private void StateRequestRPC(PhotonMessageInfo info = default)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !_ready || info.Sender == null) return;
        _view.RPC(nameof(StateRPC), info.Sender, _charges);
    }

    [PunRPC]
    private void StateRPC(int charges, PhotonMessageInfo info = default)
    {
        if (info.Sender != PhotonNetwork.MasterClient || charges < 0 || charges > 3) return;
        _charges = charges;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) _counter = null;
        _ready = true;
        UpdateIndicators();
    }

    [PunRPC]
    private void PulseRPC(int charges, Vector3 position, PhotonMessageInfo info = default)
    {
        if (info.Sender != PhotonNetwork.MasterClient || charges < 0 || charges > 3) return;
        ApplyPulse(charges, position);
    }

    private void ApplyPulse(int charges, Vector3 position)
    {
        _charges = charges;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) _counter = null;
        _ready = true;
        UpdateIndicators();
        JammerFx.Play(position);
    }

    private void UpdateIndicators()
    {
        // The native BatteryUI reads these values while the item is held.
        _battery.batteryLife = _charges * 100f / 3f;
        _battery.batteryLifeInt = _charges;
        for (int i = 0; i < _indicators.Length; i++)
            _indicators[i].sharedMaterial = i < _charges ? JammerItem.ChargedMaterial : JammerItem.EmptyMaterial;
    }

    private void Spend()
    {
        if (_spent) return;
        _spent = true;
        StatsManager.instance.ItemRemove(_attributes.instanceName);
        _attributes.DisableUI(_disable: true);
        StartCoroutine(RemoveSpent());
    }

    private IEnumerator RemoveSpent()
    {
        yield return new WaitForSeconds(1f);
        if (_grab != null && SemiFunc.IsMasterClientOrSingleplayer()) _grab.DestroyPhysGrabObject();
    }
}
