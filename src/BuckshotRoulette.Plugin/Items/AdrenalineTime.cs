using System;
using System.Collections.Generic;
using System.Linq;
using BuckshotRoulette.Core;
using BuckshotRoulette.Logic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using REPOLib.Modules;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>Shared world clock, real-time deadlines and independent beneficiaries.</summary>
[DefaultExecutionOrder(-32000)]
internal sealed class AdrenalineTime : MonoBehaviourPunCallbacks
{
    internal static AdrenalineTime Instance { get; private set; }
    private readonly AdrenalineWindow _window = new AdrenalineWindow();
    private NetworkedEvent _snapshots, _requests;
    private LevelGenerator _level;
    private string _token;
    private bool _ready, _worldActive;
    private int _revision, _nonce;
    private float _nextRequest, _nextScan;
    private float _baseScale, _baseFixed, _appliedScale, _appliedFixed;
    private readonly Dictionary<Component, Clock> _clocks = new Dictionary<Component, Clock>();
    private readonly Dictionary<Animator, float> _animation = new Dictionary<Animator, float>();
    private Animator[] _animators = Array.Empty<Animator>();
    private Rigidbody[] _bodies = Array.Empty<Rigidbody>();
    private sealed class Clock
    {
        public double Value, Scaled, Real;
        public bool Normal;
    }

    internal static bool Active => Instance != null && Instance._worldActive;
    internal static float Compensation => Active ? 1f / AdrenalineWindow.WorldScale : 1f;
    private static double Now => SemiFunc.IsMultiplayer() ? PhotonNetwork.Time : Time.realtimeSinceStartupAsDouble;
    private static int PlayerId(PlayerAvatar player) => SemiFunc.IsMultiplayer() ? player.photonView.ViewID : 1;
    private static bool InLevel => SemiFunc.RunIsLevel() && LevelGenerator.Instance != null && LevelGenerator.Instance.Generated;

    internal static void Create(GameObject host)
    {
        host.AddComponent<AdrenalineTime>();
        host.AddComponent<AdrenalinePhysicsStep>();
    }

    private void Awake()
    {
        Instance = this;
        _snapshots = new NetworkedEvent("BuckshotRoulette.Adrenaline.State", OnSnapshot);
        _requests = new NetworkedEvent("BuckshotRoulette.Adrenaline.Request", OnRequest);
    }

    private void Update()
    {
        RestoreAnimations();
        foreach (var pair in _clocks) AdvanceClock(pair.Key, pair.Value);
        if (_level != LevelGenerator.Instance || !InLevel)
        {
            ResetEffect();
            _level = LevelGenerator.Instance;
            if (!InLevel) return;
        }
        if (!_ready)
        {
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                _token = Guid.NewGuid().ToString("N");
                _ready = true;
                Publish();
            }
            else if (Time.unscaledTime >= _nextRequest)
            {
                _nextRequest = Time.unscaledTime + 1f;
                if (_nonce <= 0) _nonce = 1;
                _requests.RaiseEvent(_nonce, NetworkingEvents.RaiseAll, SendOptions.SendReliable);
            }
        }
        bool changed = _window.Expire(Now);
        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            foreach (int id in _window.Players)
            {
                var player = ResolvePlayer(id);
                if (player == null || player.deadSet || player.isDisabled) changed |= _window.Remove(id);
            }
            if (changed) Publish();
        }
        ApplyWorld(_window.Count > 0);
        if (!_worldActive) return;
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + .2f;
            _bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            _animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
        }
        foreach (var body in _bodies) if (body != null && !body.isKinematic) AdrenalinePhysics.Factor(body);
        var local = PlayerAvatar.instance;
        if (local != null && Beneficiary(local))
        {
            float remaining = (float)_window.Remaining(PlayerId(local), Now);
            SemiFunc.UIItemInfoText(null, Texts.T($"Адреналин · {remaining:F1} с", $"Adrenaline · {remaining:F1}s"));
        }
    }

    private void FixedUpdate() => AdrenalinePhysics.RestoreStepParameters();

    internal void ApplyAnimations()
    {
        if (!_worldActive) return;
        foreach (var animator in _animators)
        {
            if (animator == null || !animator.isActiveAndEnabled || !NormalTime(animator)) continue;
            float original = animator.speed;
            _animation[animator] = original;
            animator.speed = original * Compensation;
        }
    }

    private void RestoreAnimations()
    {
        foreach (var pair in _animation)
            if (pair.Key != null && Mathf.Approximately(pair.Key.speed, pair.Value * Compensation)) pair.Key.speed = pair.Value;
        _animation.Clear();
    }

    internal bool TryActivate(PlayerAvatar player)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !_ready || !InLevel || player == null
            || player.deadSet || player.isDisabled || !_window.TryStart(PlayerId(player), Now)) return false;
        Publish();
        ApplyWorld(true);
        _nextScan = 0f;
        return true;
    }

    private static PlayerAvatar ResolvePlayer(int id) => SemiFunc.IsMultiplayer()
        ? PhotonView.Find(id)?.GetComponent<PlayerAvatar>() : PlayerAvatar.instance;

    private bool Beneficiary(PlayerAvatar player) => player != null && _window.Contains(PlayerId(player), Now);

    internal static bool NormalTime(Component component)
    {
        if (!Active || component == null) return false;
        if (component is PlayerController controller) return Instance.Beneficiary(controller.playerAvatarScript);
        if (component is PhysGrabber grabber) return Instance.Beneficiary(grabber.playerAvatar);
        // Cameras are separate from PlayerAvatar's hierarchy.
        if (component.GetType().Name.StartsWith("Camera", StringComparison.Ordinal)) return Instance.Beneficiary(PlayerAvatar.instance);
        var avatar = component.GetComponentInParent<PlayerAvatar>();
        if (avatar != null && Instance.Beneficiary(avatar)) return true;
        foreach (int id in Instance._window.PlayerIds)
        {
            var active = ResolvePlayer(id);
            if (active != null && active.tumble != null && component.transform.IsChildOf(active.tumble.transform)) return true;
        }
        var physical = component.GetComponentInParent<PhysGrabObject>();
        if (physical == null) return false;
        foreach (var holder in physical.playerGrabbing)
            if (holder != null && Instance.Beneficiary(holder.playerAvatar)) return true;
        return false;
    }

    internal static bool NormalBody(Rigidbody body) => NormalTime(body)
        || (PlayerController.instance != null && PlayerController.instance.rb == body && Instance.Beneficiary(PlayerAvatar.instance));

    private void ApplyWorld(bool enabled)
    {
        if (enabled == _worldActive) return;
        // Settle component clocks at the old rate before switching modes.
        foreach (var pair in _clocks) AdvanceClock(pair.Key, pair.Value);
        RestoreAnimations();
        if (enabled)
        {
            _baseScale = Time.timeScale;
            _baseFixed = Time.fixedDeltaTime;
            _appliedScale = _baseScale * AdrenalineWindow.WorldScale;
            _appliedFixed = _baseFixed * AdrenalineWindow.WorldScale;
            Time.timeScale = _appliedScale;
            Time.fixedDeltaTime = _appliedFixed;
            _worldActive = true;
        }
        else
        {
            AdrenalinePhysics.RestoreAll();
            if (Mathf.Approximately(Time.timeScale, _appliedScale)) Time.timeScale = _baseScale;
            if (Mathf.Approximately(Time.fixedDeltaTime, _appliedFixed)) Time.fixedDeltaTime = _baseFixed;
            _worldActive = false;
        }
        foreach (var pair in _clocks) pair.Value.Normal = NormalTime(pair.Key);
    }

    private void ResetEffect()
    {
        ApplyWorld(false);
        _window.Clear(); _ready = false; _token = null; _revision = 0; _nextRequest = 0f;
        _nonce = _nonce == int.MaxValue ? 1 : _nonce + 1;
        // Persistent player/item cooldowns must keep their compensated clock offset.
        _bodies = Array.Empty<Rigidbody>(); _animators = Array.Empty<Animator>();
        foreach (var owner in _clocks.Keys.Where(owner => owner == null).ToArray()) _clocks.Remove(owner);
    }

    private void Publish(int target = 0, int nonce = 0)
    {
        if (!SemiFunc.IsMultiplayer()) return;
        if (target == 0) _revision++;
        _snapshots.RaiseEvent(new object[] { _token, _revision, _window.Players, _window.Deadlines, target, nonce },
            NetworkingEvents.RaiseAll, SendOptions.SendReliable);
    }

    private void OnRequest(EventData data)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !_ready || !InLevel || !(data.CustomData is int nonce)
            || nonce <= 0 || !PhotonNetwork.CurrentRoom.Players.ContainsKey(data.Sender)) return;
        Publish(data.Sender, nonce);
    }

    private void OnSnapshot(EventData data)
    {
        if (!InLevel || _level != LevelGenerator.Instance || SemiFunc.IsMasterClientOrSingleplayer() || PhotonNetwork.MasterClient == null
            || data.Sender != PhotonNetwork.MasterClient.ActorNumber || !(data.CustomData is object[] values) || values.Length != 6
            || !(values[0] is string token) || string.IsNullOrEmpty(token) || !(values[1] is int revision)
            || !(values[2] is int[] players) || !(values[3] is double[] deadlines)
            || !(values[4] is int target) || !(values[5] is int nonce)) return;
        if (target != 0 && (target != PhotonNetwork.LocalPlayer.ActorNumber || nonce != _nonce)) return;
        if (!_ready && target == 0) return;
        if (_ready && (token != _token || revision < _revision)) return;
        if (!_window.Apply(players, deadlines, Now)) return;
        _token = token; _revision = revision; _ready = true; _nextScan = 0f;
        ApplyWorld(_window.Count > 0);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (!_ready || !InLevel) return;
        if (newMasterClient.IsLocal) { _window.Expire(Now); Publish(); }
        else { _ready = false; _nextRequest = 0f; _nonce = _nonce == int.MaxValue ? 1 : _nonce + 1; }
    }

    public override void OnLeftRoom() => ResetEffect();
    public override void OnDisable()
    {
        ResetEffect();
        base.OnDisable();
    }

    private static void AdvanceClock(Component owner, Clock clock)
    {
        double scaled = Time.timeAsDouble, real = Time.realtimeSinceStartupAsDouble;
        clock.Value += clock.Normal ? (real - clock.Real) * Instance._baseScale : scaled - clock.Scaled;
        clock.Scaled = scaled; clock.Real = real; clock.Normal = NormalTime(owner);
    }

    internal static float Delta(Component owner) => NormalTime(owner) ? Time.unscaledDeltaTime * Instance._baseScale : Time.deltaTime;
    internal static float SmoothDelta(Component owner) => NormalTime(owner) ? Time.smoothDeltaTime * Compensation : Time.smoothDeltaTime;
    internal static float FixedDelta(Component owner) => NormalTime(owner) ? Time.fixedUnscaledDeltaTime * Instance._baseScale : Time.fixedDeltaTime;
    internal static double ClockTime(Component owner)
    {
        if (Instance == null || owner == null) return Time.timeAsDouble;
        if (!Instance._clocks.TryGetValue(owner, out var clock))
        {
            clock = new Clock { Value = Time.timeAsDouble, Scaled = Time.timeAsDouble, Real = Time.realtimeSinceStartupAsDouble, Normal = NormalTime(owner) };
            Instance._clocks[owner] = clock;
        }
        AdvanceClock(owner, clock);
        return clock.Value;
    }
    internal static float ClockFloat(Component owner) => (float)ClockTime(owner);

    internal static CustomYieldInstruction Wait(float seconds, Component owner) => new LocalWait(seconds, owner);
    private sealed class LocalWait : CustomYieldInstruction
    {
        private float _remaining;
        private readonly Component _owner;
        internal LocalWait(float seconds, Component owner) { _remaining = seconds; _owner = owner; }
        public override bool keepWaiting { get { _remaining -= Delta(_owner); return _remaining > 0f; } }
    }
}
