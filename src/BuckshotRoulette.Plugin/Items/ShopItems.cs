using System;
using System.Collections.Generic;
using System.Linq;
using BuckshotRoulette.Core;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>
/// Предметы магазина мода: префабы собираются в одном неактивном контейнере и регистрируются через REPOLib.
/// Сразу после регистрации предмету задаются поля, которые игра читает при наполнении магазина
/// (ShopManager.GetAllItemsFromStatsManager): тип, объём, лимит на магазин и статус Finalized.
/// Клоны аптечки наследуют объём healthPack, поэтому без явного объёма предметы попадали бы
/// в отдельный список аптечек, которому не хватает полок, и могли вовсе не появиться в магазине.
/// </summary>
internal static class ShopItems
{
    private static readonly List<Item> Items = new List<Item>();

    public static IReadOnlyList<Item> Registered => Items;

    public static void RegisterAll()
    {
        var holder = new GameObject("BuckshotRoulette Item Prefabs");
        holder.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(holder);

        Register("дробовик", () => { BuckshotShotgun.Register(holder.transform); return BuckshotShotgun.Item; }, perShop: 1, volume: null);
        Register("ножовку", () => { HacksawItem.Register(holder.transform); return HacksawItem.Item; }, perShop: 2, volume: SemiFunc.itemVolume.small);
        Register("наручники", () => { HandcuffsItem.Register(holder.transform); return HandcuffsItem.Item; }, perShop: 1, volume: SemiFunc.itemVolume.small);
        Register("глушилку", () => { JammerItem.Register(holder.transform); return JammerItem.Item; }, perShop: 1, volume: SemiFunc.itemVolume.small);
        Register("адреналин", () => { AdrenalineItem.Register(holder.transform); return AdrenalineItem.Item; }, perShop: 2, volume: SemiFunc.itemVolume.small);
    }

    private static void Register(string what, Func<Item> register, int perShop, SemiFunc.itemVolume? volume)
    {
        try
        {
            var item = register();
            if (item == null)
            {
                Plugin.Log.LogError($"Не удалось собрать {what}: модуль не вернул предмет");
                return;
            }
            PrepareForShop(item, perShop, volume);
            Items.Add(item);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Не удалось собрать {what}: {e}");
        }
    }

    /// <summary>
    /// Предмет должен продаваться: статус Finalized, физический, не скрытый товар, без ограничения по числу игроков.
    /// Объём полки задаётся явно для клонов аптечки (они наследуют полку healthPack); дробовик остаётся
    /// на полке ванильного ружья, которая в магазине заведомо есть.
    /// </summary>
    private static void PrepareForShop(Item item, int perShop, SemiFunc.itemVolume? volume)
    {
        item.status = Item.Status.Finalized;
        item.physicalItem = true;
        if (volume.HasValue) item.itemVolume = volume.Value;
        item.itemSecretShopType = SemiFunc.itemSecretShopType.none;
        item.minPlayerCount = 1;
        item.maxAmountInShop = perShop;
        item.maxPurchase = false;
        Plugin.Log.LogInfo($"Магазин: {item.name} — объём {item.itemVolume}, до {perShop} шт. за магазин, статус {item.status}");
    }

    /// <summary>
    /// Страховка на случай, если предмет не попал в StatsManager к моменту наполнения магазина:
    /// хост при входе в магазин дописывает недостающие предметы мода.
    /// </summary>
    public static void EnsureInShop()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.RunIsShop()) return;
        if (Items.Count == 0) return;
        var dictionary = StatsManager.instance.itemDictionary;
        var missing = Items.Where(item => item != null && !dictionary.ContainsKey(item.name)).ToList();
        if (missing.Count == 0) return;
        foreach (var item in missing) dictionary[item.name] = item;
        Plugin.Log.LogWarning($"Магазин: предметы мода дописаны в StatsManager — {string.Join(", ", missing.Select(item => item.name))}");
    }
}
