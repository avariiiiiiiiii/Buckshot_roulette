using BepInEx;
using BepInEx.Logging;
using BuckshotRoulette.Core;
using BuckshotRoulette.Dealer;
using BuckshotRoulette.Diagnostics;
using BuckshotRoulette.Items;
using BuckshotRoulette.Shells;
using BuckshotRoulette.Valuables;
using HarmonyLib;

namespace BuckshotRoulette;

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
[BepInDependency("REPOLib", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; }
    internal static Plugin Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Settings.Bind(Config);

        var harmony = new Harmony(ModInfo.Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);

        EffectsHost.Create(gameObject);
        AdrenalineTime.Create(gameObject);
        ShellPool.Register();
        gameObject.AddComponent<ShellPoolCallbacks>();
        GunFx.Load();
        CustomValuables.RegisterAll();
        try { DealerFactory.RegisterAll(); }
        catch (System.Exception e) { Log.LogError($"Не удалось собрать дилера: {e}"); }
        ShopItems.RegisterAll();
        gameObject.AddComponent<DebugSpawner>();
        gameObject.AddComponent<PoolSelfTest>();

        Log.LogInfo($"{ModInfo.Name} {ModInfo.Version} загружен");
    }
}
