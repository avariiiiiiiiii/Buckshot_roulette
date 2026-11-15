using HarmonyLib;
using BuckshotRoulette.Dealer;
using BuckshotRoulette.Items;
using UnityEngine;

namespace BuckshotRoulette.Shells;

/// <summary>
/// Номер выстрела на HurtCollider: все попадания с одним номером — одно нажатие курка и один патрон общего пула
/// (Logic/ShotLedger). Дробины ружья получают номер выстрела хоста, лучи лазеров — новый номер на каждое включение луча.
/// </summary>
internal sealed class ShotTag : MonoBehaviour
{
    private static int _lastId;

    /// <summary>Хост: номер выстрела, который сейчас идёт в ItemGun.ShootRPC (0 — выстрела нет).</summary>
    internal static int Pending;

    public int Id;

    public static int Next() => ++_lastId;
}

/// <summary>Луч лазера: HurtCollider включается на время луча и выключается между выстрелами — каждое включение новый выстрел.</summary>
internal sealed class LaserShot : MonoBehaviour
{
    private ShotTag _tag;

    private void Awake() => _tag = GetComponent<ShotTag>() ?? gameObject.AddComponent<ShotTag>();

    private void OnEnable() => _tag.Id = ShotTag.Next();

    private void OnDisable()
    {
        if (_tag != null && _tag.Id != 0) DealerDamagePatches.ForgetShot(_tag.Id);
    }

    internal static void Mark(SemiLaser laser)
    {
        var hurt = laser != null ? laser.GetComponentInChildren<HurtCollider>(true) : null;
        if (hurt != null && hurt.GetComponent<LaserShot>() == null) hurt.gameObject.AddComponent<LaserShot>();
    }
}

[HarmonyPatch(typeof(ItemGun), nameof(ItemGun.ShootRPC))]
internal static class ShotTagGunShot
{
    [HarmonyPrefix]
    private static bool Prefix(ItemGun __instance)
    {
        // Our shotgun enters only after its host-selected live round was
        // dispatched by FireShellRPC. Direct native RPCs must not bypass the pool.
        if (__instance.GetComponent<BuckshotShells>() != null) return ShotTag.Pending != 0;
        if (SemiFunc.IsMasterClientOrSingleplayer()) ShotTag.Pending = ShotTag.Next();
        return true;
    }

    [HarmonyFinalizer]
    private static void Finalizer(ItemGun __instance)
    {
        ShotTag.Pending = 0;
    }
}

[HarmonyPatch(typeof(ItemGun), nameof(ItemGun.ShootBulletRPC))]
internal static class ShotTagBullet
{
    [HarmonyPostfix]
    private static void Postfix(ItemGun __instance)
    {
        if (ShotTag.Pending == 0 || __instance.hurtCollider == null) return;
        var tag = __instance.hurtCollider.GetComponent<ShotTag>() ?? __instance.hurtCollider.gameObject.AddComponent<ShotTag>();
        tag.Id = ShotTag.Pending;
    }
}

[HarmonyPatch(typeof(ItemGunLaser), "Start")]
internal static class ShotTagGunLaser
{
    [HarmonyPostfix]
    private static void Postfix(ItemGunLaser __instance) => LaserShot.Mark(__instance.semiLaser);
}

[HarmonyPatch(typeof(ItemCartLaser), "Start")]
internal static class ShotTagCartLaser
{
    [HarmonyPostfix]
    private static void Postfix(ItemCartLaser __instance) => LaserShot.Mark(__instance.semiLaser);
}
