using BuckshotRoulette.Core;
using HarmonyLib;

namespace BuckshotRoulette.Diagnostics;

/// <summary>Включает встроенную тестовую ветку игры: из меню сразу в одиночный забег, без кликов по UI.</summary>
[HarmonyPatch(typeof(MenuPageMain), "Update")]
internal static class AutoStart
{
    private static bool _done;

    [HarmonyPrefix]
    private static void Run()
    {
        if (_done || !Settings.AutoStartSingleplayer.Value) return;
        if (RunManager.instance == null || MenuManager.instance == null || MenuManager.instance.pagePopUpScheduled) return;
        _done = true;
        RunManager.instance.loadLevel = Settings.AutoStartLevel.Value;
        RunManager.instance.localSingleplayerTest = true;
        Plugin.Log.LogInfo($"AutoStart: одиночный забег, loadLevel={Settings.AutoStartLevel.Value}");
    }
}
