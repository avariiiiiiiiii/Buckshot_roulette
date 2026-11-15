using System.Linq;
using System.Text;
using BuckshotRoulette.Core;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine;

namespace BuckshotRoulette.Diagnostics;

/// <summary>Дамп иерархий префабов, на которых строятся клоны (враг, ружьё, расходник, ценности).</summary>
[HarmonyPatch(typeof(StatsManager), "RunStartStats")]
internal static class PrefabInspect
{
    private static bool _done;

    private static readonly string[] Targets =
    {
        "Enemies/Enemy - Animal",
        "Items/Item Gun Shotgun",
        "Items/Item Health Pack Small",
        "Items/Item Melee Sword",
    };

    [HarmonyPostfix]
    private static void Run()
    {
        if (_done || !Settings.DiagnosticsEnabled.Value) return;
        _done = true;

        var sb = new StringBuilder("\n===== BuckshotRoulette: префабы =====\n");

        sb.AppendLine("-- Ценности: путь | volumeType | цена min..max");
        var valuables = REPOLib.Modules.Valuables.AllValuables.Where(r => r != null && r.Prefab != null).ToList();
        foreach (var r in valuables.OrderBy(r => r.ResourcePath))
        {
            var vo = r.Prefab.GetComponent<ValuableObject>();
            sb.AppendLine($"{r.ResourcePath} | {vo?.volumeType} | {vo?.valuePreset?.valueMin}..{vo?.valuePreset?.valueMax}");
        }

        foreach (var path in Targets.Concat(valuables
                     .Where(r => r.Prefab.GetComponent<ValuableObject>()?.volumeType == ValuableVolume.Type.Tiny)
                     .Select(r => r.ResourcePath).Take(2)))
        {
            var go = Resources.Load<GameObject>(path);
            if (go == null && NetworkPrefabs.Prefabs.TryGetValue(path, out var reg)) go = reg;
            sb.AppendLine($"-- Иерархия: {path}{(go == null ? " (НЕ НАЙДЕН)" : "")}");
            if (go != null) Describe(go.transform, 0, sb);
        }

        Plugin.Log.LogInfo(sb.ToString());
    }

    private static void Describe(Transform t, int depth, StringBuilder sb)
    {
        var comps = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name);
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name} [{string.Join(", ", comps)}]");
        foreach (Transform child in t) Describe(child, depth + 1, sb);
    }
}
