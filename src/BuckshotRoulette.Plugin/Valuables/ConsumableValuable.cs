using BuckshotRoulette.Core;
using BuckshotRoulette.Logic;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Valuables;

internal enum ConsumableKind
{
    Beer,
    Cigarettes,
    Medicine,
}

/// <summary>
/// Ценность, которую можно использовать: взять в руку и нажать E. Одноразовая, цена не падает.
/// Эффект применяется даже при полном здоровье: лечить нечего, но предмет тратится и играет эффект.
/// </summary>
internal sealed class ConsumableValuable : MonoBehaviour
{
    public ConsumableKind Kind;

    private PhotonView _view;
    private PhysGrabObject _grab;
    private bool _used;

    private void Awake()
    {
        _view = GetComponent<PhotonView>();
        _grab = GetComponent<PhysGrabObject>();
    }

    private void Update()
    {
        if (_used || _grab == null || !_grab.heldByLocalPlayer) return;

        // подсказка над инвентарём — игра держит текст, только пока его вызывают каждый кадр
        string key = SemiFunc.InputDisplayReplaceTags("[interact]");
        SemiFunc.UIItemInfoText(null, $"{Label()} <color=#FFFFFF>[{key}]</color>");

        if (!SemiFunc.InputDown(InputKey.Interact)) return;

        var player = PlayerAvatar.instance;
        if (player == null || player.playerHealth == null) return;

        _used = true;
        Apply(player);
        MarkUsedVisual();
        if (_view != null && _view.ViewID != 0)
            _view.RPC(nameof(UsedRPC), RpcTarget.Others, Kind == ConsumableKind.Medicine ? 0 : 1, player.photonView.ViewID);
    }

    /// <summary>
    /// Пьяный голос: ниже и «плавает» (как сонный голос от транквилизатора, но медленнее), столько же, сколько головокружение.
    /// Высота меняется у источника голоса этого игрока — поэтому на каждом клиенте.
    /// </summary>
    private static void DrunkVoice(PlayerAvatar player)
    {
        if (player != null && player.voiceChat != null)
            player.voiceChat.OverridePitch(DrunkPitch, 0.8f, 2f, DrunkSeconds, DrunkWobble, DrunkWobbleSpeed);
    }

    private const float DrunkSeconds = 10f;
    private const float DrunkPitch = 0.82f;
    private const float DrunkWobble = 0.14f;
    private const float DrunkWobbleSpeed = 2.5f;

    private string Label() => Kind switch
    {
        ConsumableKind.Beer => Texts.T("Выпить пиво", "Drink beer"),
        ConsumableKind.Cigarettes => Texts.T("Выкурить сигарету", "Smoke a cigarette"),
        _ => Texts.T("Принять лекарство", "Take medicine"),
    };

    private void Apply(PlayerAvatar player)
    {
        var health = player.playerHealth;
        int max = health.maxHealth;
        int before = health.health;

        switch (Kind)
        {
            case ConsumableKind.Beer:
                health.Heal(Heal.PercentOfMax(max, 10), true);
                PlayerController.instance.EnergyCurrent = PlayerController.instance.EnergyStart;
                EffectsHost.Instance.StartNausea(DrunkSeconds);
                DrunkVoice(player);
                HealEffect.Play(player.transform);
                break;

            case ConsumableKind.Cigarettes:
                health.Heal(Heal.PercentOfMax(max, 10), true);
                HealEffect.Play(player.transform);
                break;

            case ConsumableKind.Medicine:
                int delta = Medicine.RollDelta(max, new System.Random());
                if (delta > 0)
                {
                    health.Heal(delta, true);
                    HealEffect.Play(player.transform);
                }
                else
                {
                    // плохое лекарство не убивает само по себе: остаётся минимум 1 HP
                    health.Hurt(Mathf.Min(-delta, Mathf.Max(0, health.health - 1)), false);
                }
                break;
        }

        Plugin.Log.LogInfo($"{Kind}: HP {before} -> {health.health} (макс {max})");
    }

    /// <summary>Использованный предмет остаётся (сдаётся на квоту), но темнеет.</summary>
    private void MarkUsedVisual()
    {
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            foreach (var m in r.materials)
            {
                m.SetColor("_Color", m.GetColor("_Color") * 0.35f);
                m.SetColor("_EmissionColor", Color.black);
            }
        }
    }

    /// <param name="healed">1 — другим игрокам показать эффект лечения вокруг держащего; 0 — только пометить использованным.</param>
    /// <param name="userViewId">Кто использовал: после пива у остальных его голос тоже звучит пьяно.</param>
    [PunRPC]
    private void UsedRPC(int healed, int userViewId)
    {
        _used = true;
        MarkUsedVisual();
        if (Kind == ConsumableKind.Beer) DrunkVoice(SemiFunc.PlayerAvatarGetFromPhotonID(userViewId));
        if (healed != 1 || _grab == null) return;
        foreach (var grabber in _grab.playerGrabbing)
        {
            if (grabber != null && grabber.playerAvatar != null) HealEffect.Play(grabber.playerAvatar.transform);
        }
    }
}
