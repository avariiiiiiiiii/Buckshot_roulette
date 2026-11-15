using System;
using UnityEngine;

namespace BuckshotRoulette.Items;

/// <summary>Предметы магазина мода: префабы собираются в одном неактивном контейнере и регистрируются через REPOLib.</summary>
internal static class ShopItems
{
    public static void RegisterAll()
    {
        var holder = new GameObject("BuckshotRoulette Item Prefabs");
        holder.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(holder);

        Register("дробовик", () => BuckshotShotgun.Register(holder.transform));
        Register("ножовку", () => HacksawItem.Register(holder.transform));
        Register("наручники", () => HandcuffsItem.Register(holder.transform));
        Register("глушилку", () => JammerItem.Register(holder.transform));
        Register("адреналин", () => AdrenalineItem.Register(holder.transform));
    }

    private static void Register(string what, Action register)
    {
        try { register(); }
        catch (Exception e) { Plugin.Log.LogError($"Не удалось собрать {what}: {e}"); }
    }
}
