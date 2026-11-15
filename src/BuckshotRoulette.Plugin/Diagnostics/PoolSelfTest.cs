using System.Collections;
using System.Linq;
using BuckshotRoulette.Core;
using BuckshotRoulette.Dealer;
using BuckshotRoulette.Items;
using UnityEngine;

namespace BuckshotRoulette.Diagnostics;

/// <summary>
/// Отладка общего пула патронов без игрока (только одиночка): на уровне кладёт перед дилером ванильный дробовик, лазер
/// и дробовик дилера и стреляет ими в дилера кодом. В логе на каждый выстрел должно быть ровно одно «Игрок попал по дилеру»
/// (дробины и тики луча делят патрон). Вне раунда (магазин) стреляет дробовиком дилера — пул трогаться не должен.
/// </summary>
internal sealed class PoolSelfTest : MonoBehaviour
{
    private const int Rounds = 3;

    private LevelGenerator _ranFor;

    private void Update()
    {
        if (!Settings.ShellPoolSelfTest.Value || SemiFunc.IsMultiplayer()) return;
        var gen = LevelGenerator.Instance;
        if (gen == null || !gen.Generated || gen == _ranFor) return;
        var player = PlayerAvatar.instance;
        if (player == null || player.transform.position == Vector3.zero) return;
        _ranFor = gen;
        // уровень бывает пересоздан (автостарт) — тест прошлого уровня держит уничтоженные объекты
        StopAllCoroutines();
        StartCoroutine(SemiFunc.RunIsLevel() ? InLevel(player) : OutOfLevel(player));
    }

    private IEnumerator OutOfLevel(PlayerAvatar player)
    {
        yield return new WaitForSeconds(3f);
        var ours = Spawn(BuckshotShotgun.Item, player.transform.position + player.transform.forward * 1.5f + Vector3.up);
        if (ours == null) yield break;
        yield return new WaitForSeconds(2f);
        Freeze(ours);
        for (int i = 1; i <= 3; i++)
        {
            Plugin.Log.LogInfo($"PoolSelfTest: вне раунда, выстрел дробовиком дилера #{i}");
            ours.GetComponent<ItemGun>().Shoot();
            yield return new WaitForSeconds(1.5f);
        }
        Plugin.Log.LogInfo("PoolSelfTest: вне раунда — готово");
    }

    private IEnumerator InLevel(PlayerAvatar player)
    {
        player.playerHealth.godMode = true;
        EnemyDealer dealer = null;
        for (float t = 0f; t < 40f && dealer == null; t += 0.5f)
        {
            dealer = FindObjectsByType<EnemyDealer>(FindObjectsSortMode.None)
                .FirstOrDefault(d => d.CurrentState != EnemyDealer.State.Spawn && d.GetComponent<Enemy>().EnemyParent.Spawned);
            yield return new WaitForSeconds(0.5f);
        }
        if (dealer == null)
        {
            Plugin.Log.LogError("PoolSelfTest: дилер не появился");
            yield break;
        }
        var enemy = dealer.GetComponent<Enemy>();
        var origin = player.transform.position + Vector3.up * 1.5f;
        var vanilla = Spawn(FindItem(name => name == "Item Gun Shotgun"), origin);
        var laser = Spawn(FindItem(name => name.StartsWith("Item Gun") && name.Contains("Laser")), origin + Vector3.right);
        var ours = Spawn(BuckshotShotgun.Item, origin - Vector3.right);
        yield return new WaitForSeconds(2f);

        for (int i = 1; i <= Rounds; i++)
        {
            if (vanilla != null)
            {
                Prepare(enemy, vanilla);
                Plugin.Log.LogInfo($"PoolSelfTest: ванильный дробовик, выстрел #{i}");
                vanilla.GetComponent<ItemGun>().ShootRPC();
                yield return new WaitForSeconds(1.5f);
            }
            if (laser != null)
            {
                Plugin.Log.LogInfo($"PoolSelfTest: лазер, луч #{i}");
                var semiLaser = laser.GetComponent<ItemGunLaser>().semiLaser;
                for (float t = 0f; t < 1.2f; t += Time.deltaTime)
                {
                    Prepare(enemy, laser);
                    var muzzle = laser.GetComponent<ItemGun>().gunMuzzle;
                    semiLaser.LaserActive(muzzle.position, enemy.CenterTransform.position, true);
                    yield return null;
                }
                yield return new WaitForSeconds(1.5f);
            }
            if (ours != null)
            {
                Prepare(enemy, ours);
                SawedOff.On(ours).Arm();
                Plugin.Log.LogInfo($"PoolSelfTest: дробовик дилера с ножовкой, выстрел #{i}");
                ours.GetComponent<ItemGun>().Shoot();
                yield return new WaitForSeconds(1.5f);
            }
        }
        Plugin.Log.LogInfo("PoolSelfTest: на уровне — готово");
    }

    private static Item FindItem(System.Func<string, bool> match)
    {
        var item = StatsManager.instance.itemDictionary.Values.FirstOrDefault(i => i != null && match(i.name));
        if (item == null) Plugin.Log.LogError("PoolSelfTest: предмет не найден в itemDictionary");
        return item;
    }

    private static GameObject Spawn(Item item, Vector3 position)
    {
        if (item == null) return null;
        var go = REPOLib.Modules.Items.SpawnItem(item, position, Quaternion.identity);
        Plugin.Log.LogInfo($"PoolSelfTest: {item.name} -> {(go == null ? "НЕ СОЗДАН" : "создан")}");
        return go;
    }

    private static void Freeze(GameObject item) => item.GetComponent<PhysGrabObject>().rb.isKinematic = true;

    /// <summary>Дилер не умирает от теста; ружьё висит в 2 м от дилера дулом в его центр.</summary>
    private static void Prepare(Enemy enemy, GameObject item)
    {
        enemy.Health.healthCurrent = 100000;
        Freeze(item);
        var muzzle = item.GetComponent<ItemGun>().gunMuzzle;
        var target = enemy.CenterTransform.position;
        var from = target + (enemy.transform.forward + Vector3.up * 0.2f).normalized * 2f;
        var t = item.transform;
        t.rotation = Quaternion.FromToRotation(muzzle.forward, target - from) * t.rotation;
        t.position += from - muzzle.position;
    }
}
