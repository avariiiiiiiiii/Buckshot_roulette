using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuckshotRoulette.Core;
using HarmonyLib;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>
/// Магазин: перед наполнением полок хост проверяет, что предметы мода есть в StatsManager, а после наполнения
/// одной строкой пишет в лог, какие из них попали в пул магазина. Это не режим отладки.
/// Оба перегруза ShopInitialize() и ShopInitialize(List&lt;ItemSpawn&gt;) патчатся явно: по одному имени
/// Harmony нашёл бы два метода и не применил патч.
/// </summary>
[HarmonyPatch]
internal static class ShopAvailability
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ShopManager), "ShopInitialize", new System.Type[0]);
        yield return AccessTools.Method(typeof(ShopManager), "ShopInitialize", new[] { typeof(List<PunManager.ItemSpawn>) });
    }

    [HarmonyPrefix]
    private static void Before() => ShopItems.EnsureInShop();

    [HarmonyPostfix]
    private static void After()
    {
        var shop = ShopManager.instance;
        if (shop == null || !SemiFunc.IsMasterClientOrSingleplayer()) return;
        var shelves = shop.potentialItems.Concat(shop.potentialItemConsumables)
            .Concat(shop.potentialItemUpgrades).Concat(shop.potentialItemHealthPacks).ToList();
        var ours = ShopItems.Registered;
        if (ours.Count == 0) return;
        var present = ours.Where(item => item != null && shelves.Any(shelf => shelf != null && shelf.name == item.name)).ToList();
        Plugin.Log.LogInfo($"Магазин: предметов мода в пуле {present.Count} из {ours.Count}" +
                           (present.Count > 0 ? $" — {string.Join(", ", present.Select(item => item.name))}" : " — ни одного, проверьте объёмы полок"));
    }
}
