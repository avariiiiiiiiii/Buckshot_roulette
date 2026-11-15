using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuckshotRoulette.Core;
using BuckshotRoulette.Dealer;
using BuckshotRoulette.Valuables;
using UnityEngine;
using UnityEngine.AI;

namespace BuckshotRoulette.Diagnostics;

/// <summary>Тестовый набор мода внутри фургона; дилер отдельно на NavMesh карты. Только хост/одиночка.</summary>
internal sealed class DebugSpawner : MonoBehaviour
{
    private LevelGenerator _spawnedFor;

    /// <summary>
    /// Игра может перенести врага при первом появлении, поэтому тестовую точку он также забирает в OnSpawn.
    /// </summary>
    public static Vector3? PendingDealerPoint;

    private void Update()
    {
        if (!Settings.SpawnValuablesOnLevelStart.Value && !Settings.SpawnDealerOnLevelStart.Value
            && !Settings.SpawnMassKitOnLevelStart.Value && !Settings.SpawnItemsOnLevelStart.Value) return;
        var gen = LevelGenerator.Instance;
        if (gen == null || !gen.Generated || gen == _spawnedFor) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        if (!RunManager.instance.levels.Contains(RunManager.instance.levelCurrent)) return;
        var player = PlayerAvatar.instance;
        if (player == null || player.transform.position == Vector3.zero) return;
        if ((Settings.SpawnItemsOnLevelStart.Value || Settings.SpawnValuablesOnLevelStart.Value)
            && TruckSafetySpawnPoint.instance == null) return;

        _spawnedFor = gen;
        if (Settings.SpawnValuablesOnLevelStart.Value) SpawnValuables();
        if (Settings.SpawnDealerOnLevelStart.Value) SpawnDealer(player);
        if (Settings.SpawnMassKitOnLevelStart.Value) SpawnMassKit(player);
        if (Settings.SpawnItemsOnLevelStart.Value) SpawnItems();
    }

    private static void SpawnItems()
    {
        SpawnItem(Items.BuckshotShotgun.Item, 0);
        SpawnItem(Items.HacksawItem.Item, 1);
        SpawnItem(Items.HandcuffsItem.Item, 2);
        SpawnItem(Items.JammerItem.Item, 3);
        SpawnItem(Items.AdrenalineItem.Item, 4);
    }

    private static readonly Vector2[] TruckSlots = {
        new Vector2(-.70f, 0f), new Vector2(0f, -.60f), new Vector2(.60f, -.60f),
        new Vector2(0f, 0f), new Vector2(.60f, 0f), new Vector2(-.70f, .75f),
        new Vector2(0f, .65f), new Vector2(.60f, .65f), new Vector2(-.70f, -.75f),
    };

    private static Vector3 TruckPosition(int slot)
    {
        var truck = TruckSafetySpawnPoint.instance.transform;
        var yaw = Quaternion.Euler(0f, truck.eulerAngles.y, 0f);
        var point = TruckSlots[slot];
        return truck.position + yaw * new Vector3(point.x, .35f, point.y);
    }

    private static Quaternion TruckRotation => Quaternion.Euler(0f, TruckSafetySpawnPoint.instance.transform.eulerAngles.y, 0f);

    private static void PutOnTruckFloor(GameObject go, Vector3 position)
    {
        if (go == null) return;
        var hits = Physics.RaycastAll(position + Vector3.up * .1f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.normal.y < .65f || hit.collider.GetComponentInParent<PhysGrabObject>() != null
                || hit.collider.GetComponentInParent<PlayerAvatar>() != null) continue;
            Physics.SyncTransforms();
            float bottom = float.PositiveInfinity;
            foreach (var collider in go.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger) bottom = Mathf.Min(bottom, collider.bounds.min.y);
            if (float.IsInfinity(bottom)) return;
            var root = go.GetComponent<Rigidbody>();
            var target = go.transform.position + Vector3.up * (hit.point.y + .025f - bottom);
            go.transform.position = target;
            if (root != null) { root.position = target; root.linearVelocity = Vector3.zero; root.angularVelocity = Vector3.zero; }
            return;
        }
        Plugin.Log.LogWarning($"DebugSpawn: не найден пол фургона для {go.name}, точка {position}");
    }

    private static void SpawnItem(Item item, int slot)
    {
        if (item == null) return;
        var position = TruckPosition(slot);
        var go = REPOLib.Modules.Items.SpawnItem(item, position, TruckRotation);
        PutOnTruckFloor(go, position);
        Plugin.Log.LogInfo($"DebugSpawn: {item.itemName} -> {(go == null ? "НЕ СОЗДАН" : "в фургоне " + go.transform.position)}");
    }

    /// <summary>
    /// Проверка оглушения дилера ударом: удар ценностью оглушает, если её масса не меньше порога типа врага.
    /// В лог — массы всех ценностей игры, перед игроком в ряд — по одной ценности каждой массы, от лёгкой к тяжёлой.
    /// </summary>
    private static void SpawnMassKit(PlayerAvatar player)
    {
        var byMass = new SortedDictionary<float, PrefabRef>();
        var sb = new StringBuilder("Массы ценностей (порог оглушения дилера):\n");
        foreach (var prefabRef in REPOLib.Modules.Valuables.AllValuables)
        {
            var valuable = prefabRef.Prefab != null ? prefabRef.Prefab.GetComponent<ValuableObject>() : null;
            if (valuable == null || valuable.physAttributePreset == null) continue;
            float mass = valuable.physAttributePreset.mass;
            sb.AppendLine($"  {prefabRef.PrefabName}: {valuable.physAttributePreset.name} = {mass:F2}");
            if (!byMass.ContainsKey(mass)) byMass[mass] = prefabRef;
        }
        Plugin.Log.LogInfo(sb.ToString());

        var origin = player.transform.position + player.transform.forward * 2.5f + Vector3.up * 0.6f;
        int i = 0;
        foreach (var kv in byMass)
        {
            var pos = origin + player.transform.right * ((i++ - (byMass.Count - 1) / 2f) * 1.3f);
            var go = REPOLib.Modules.Valuables.SpawnValuable(kv.Value, pos, Quaternion.identity);
            Plugin.Log.LogInfo($"DebugSpawn: {kv.Value.PrefabName} (масса {kv.Key:F2}) -> {(go == null ? "НЕ СОЗДАН" : "лежит")}");
        }
    }

    private void SpawnValuables()
    {
        int i = 0;
        foreach (var prefabRef in CustomValuables.Registered)
        {
            var pos = TruckPosition(5 + i++);
            var go = REPOLib.Modules.Valuables.SpawnValuable(prefabRef, pos, TruckRotation);
            PutOnTruckFloor(go, pos);
            Plugin.Log.LogInfo($"DebugSpawn: {prefabRef.PrefabName} -> {(go == null ? "НЕ СОЗДАН" : "в фургоне " + go.transform.position)}");
        }
    }

    private void SpawnDealer(PlayerAvatar player)
    {
        if (DealerFactory.Setup == null)
        {
            Plugin.Log.LogError("DebugSpawn: дилер не зарегистрирован");
            return;
        }
        var points = SemiFunc.LevelPointsGetAll().Where(point => point != null && !point.Truck)
            .OrderByDescending(point => Vector3.Distance(point.transform.position, player.transform.position)).ToArray();
        Vector3? target = null;
        foreach (var point in points)
        {
            if (Vector3.Distance(point.transform.position, player.transform.position) < 15f) continue;
            if (!NavMesh.SamplePosition(point.transform.position, out var hit, 1.5f, NavMesh.AllAreas)) continue;
            target = hit.position;
            break;
        }
        if (!target.HasValue)
        {
            Plugin.Log.LogError("DebugSpawn: удалённая точка для дилера не найдена, спавн отменён");
            return;
        }
        PendingDealerPoint = target;
        var spawned = REPOLib.Modules.Enemies.SpawnEnemy(DealerFactory.Setup, target.Value, Quaternion.identity, spawnDespawned: false);
        if (spawned == null || spawned.Count == 0) PendingDealerPoint = null;
        Plugin.Log.LogInfo($"DebugSpawn: дилер -> {(spawned == null ? "НЕ СОЗДАН" : spawned.Count + " шт. у " + target)}");
    }
}
