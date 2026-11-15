using System.Linq;
using System.Text;
using BuckshotRoulette.Core;
using HarmonyLib;
using UnityEngine;

namespace BuckshotRoulette.Diagnostics;

/// <summary>Разовый дамп состояния игры в лог: реальные имена и значения, которых нет в декомпиляции.</summary>
[HarmonyPatch(typeof(EnemyDirector), "Start")]
internal static class RuntimeDump
{
    private static bool _done;

    [HarmonyPostfix]
    private static void Run(EnemyDirector __instance)
    {
        if (_done || !Settings.DiagnosticsEnabled.Value) return;
        _done = true;

        var sb = new StringBuilder("\n===== BuckshotRoulette: дамп =====\n");

        sb.AppendLine("-- Враги по сложности (EnemySetup | spawnObjects | lvl | базовая скорость агента, м/с)");
        void Enemies(string title, System.Collections.Generic.List<EnemySetup> list)
        {
            sb.AppendLine(title);
            foreach (var e in list)
            {
                var speeds = e.spawnObjects
                    .Select(p => p.Prefab != null ? p.Prefab.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true) : null)
                    .Where(a => a != null)
                    .Select(a => a.speed.ToString("F1"));
                sb.AppendLine($"  {e.name} | {string.Join(", ", e.spawnObjects.Select(p => p.ResourcePath))} | lvl {e.levelsCompletedMin}..{e.levelsCompletedMax} | {string.Join(", ", speeds)}");
            }
        }
        Enemies("difficulty1", __instance.enemiesDifficulty1);
        Enemies("difficulty2", __instance.enemiesDifficulty2);
        Enemies("difficulty3", __instance.enemiesDifficulty3);

        Plugin.Log.LogInfo(sb.ToString());
    }
}

/// <summary>Предметы появляются в словаре только после старта забега.</summary>
[HarmonyPatch(typeof(StatsManager), "RunStartStats")]
internal static class ItemDump
{
    private static bool _done;

    [HarmonyPostfix]
    private static void Run()
    {
        if (_done || !Settings.DiagnosticsEnabled.Value) return;
        _done = true;

        var sb = new StringBuilder("\n===== BuckshotRoulette: предметы =====\n");
        sb.AppendLine("-- itemDictionary: ключ | itemName | тип | цена min..max | maxAmount");
        foreach (var kv in StatsManager.instance.itemDictionary)
        {
            var it = kv.Value;
            sb.AppendLine($"{kv.Key} | {it.itemName} | {it.itemType} | {it.value?.valueMin}..{it.value?.valueMax} | {it.maxAmount} | prefab={it.prefab?.ResourcePath}");
        }

        Plugin.Log.LogInfo(sb.ToString());
    }
}
