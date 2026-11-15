using BuckshotRoulette.Logic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>
/// Предмет пропилен ножовкой: следующий выстрел (или удар оружием ближнего боя) наносит ×2 урона, потом усиление спадает.
/// Не стакается. Пока усиление есть, предмет выглядит иначе: у дробовика дилера реально укорочен ствол,
/// остальные предметы краснеют. Решает хост, состояние рассылается всем через PhotonView предмета.
/// </summary>
internal sealed class SawedOff : MonoBehaviour
{
    private static readonly Color Tint = new Color(1f, 0.45f, 0.42f, 1f);
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly HacksawBoost _boost = new HacksawBoost();
    private PhotonView _view;

    public bool Active => _boost.Active;

    public static SawedOff On(GameObject item) => item.GetComponent<SawedOff>() ?? item.AddComponent<SawedOff>();

    private void Awake() => _view = GetComponent<PhotonView>();

    /// <summary>Наложить усиление (у всех). false — уже наложено.</summary>
    public bool Arm()
    {
        if (!_boost.Arm()) return false;
        ShowCut(true);
        return true;
    }

    /// <summary>Хост: усиление потрачено выстрелом или ударом — снять у всех.</summary>
    public int Consume(int damage)
    {
        int result = _boost.Consume(damage);
        if (SemiFunc.IsMultiplayer() && _view != null) _view.RPC(nameof(SpentRPC), RpcTarget.All);
        else SpentRPC();
        return result;
    }

    [PunRPC]
    private void SpentRPC(PhotonMessageInfo info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(info)) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) _boost.Consume(0);
        ShowCut(false);
        Plugin.Log.LogInfo($"Ножовка: усиление {name} потрачено");
    }

    private void ShowCut(bool cut)
    {
        var buckshot = GetComponentInChildren<BuckshotModel>(true);
        if (buckshot != null)
        {
            buckshot.SetSawed(cut);
            return;
        }
        var block = new MaterialPropertyBlock();
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
        {
            if (!renderer.enabled) continue;
            if (cut)
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(ColorId, Tint);
                renderer.SetPropertyBlock(block);
            }
            else
            {
                renderer.SetPropertyBlock(null);
            }
        }
    }
}

/// <summary>
/// Урон пропиленного огнестрела: хост стреляет в ShootRPC, дробины создаются в ShootBulletRPC внутри него —
/// их HurtCollider (экземпляр пули) получает ×2. После выстрела усиление снимается.
/// </summary>
[HarmonyPatch(typeof(ItemGun), nameof(ItemGun.ShootRPC))]
internal static class SawedOffGunShot
{
    internal static ItemGun BoostedGun;

    [HarmonyPrefix]
    private static void Prefix(ItemGun __instance)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        var sawed = __instance.GetComponent<SawedOff>();
        if (sawed != null && sawed.Active) BoostedGun = __instance;
    }

    [HarmonyFinalizer]
    private static void Finalizer(ItemGun __instance)
    {
        if (BoostedGun != __instance) return;
        BoostedGun = null;
        __instance.GetComponent<SawedOff>().Consume(0);
        Plugin.Log.LogInfo($"Ножовка: выстрел {__instance.name} с двойным уроном");
    }
}

[HarmonyPatch(typeof(ItemGun), nameof(ItemGun.ShootBulletRPC))]
internal static class SawedOffBullet
{
    [HarmonyPostfix]
    private static void Postfix(ItemGun __instance)
    {
        if (SawedOffGunShot.BoostedGun != __instance || __instance.hurtCollider == null) return;
        __instance.hurtCollider.enemyDamage *= 2;
    }
}

/// <summary>Ближний бой: HurtCollider сидит на самом оружии — первый удар по врагу с ×2, после него усиление снимается.</summary>
[HarmonyPatch(typeof(HurtCollider), "EnemyHurt")]
internal static class SawedOffMelee
{
    [HarmonyPrefix]
    private static void Prefix(HurtCollider __instance, ref int __state)
    {
        __state = -1;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        var sawed = __instance.GetComponentInParent<SawedOff>();
        if (sawed == null || !sawed.Active || __instance.GetComponentInParent<ItemGun>() != null) return;
        __state = __instance.enemyDamage;
        __instance.enemyDamage = sawed.Consume(__instance.enemyDamage);
        Plugin.Log.LogInfo($"Ножовка: удар {sawed.name} с двойным уроном ({__state} -> {__instance.enemyDamage})");
    }

    [HarmonyFinalizer]
    private static void Finalizer(HurtCollider __instance, int __state)
    {
        if (__state >= 0) __instance.enemyDamage = __state;
    }
}
