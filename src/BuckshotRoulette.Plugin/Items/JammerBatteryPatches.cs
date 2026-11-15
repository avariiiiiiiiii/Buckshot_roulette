using HarmonyLib;

namespace BuckshotRoulette.Items;

[HarmonyPatch(typeof(ItemBattery), nameof(ItemBattery.ReceiveCharge))]
internal static class JammerNoReceiveCharge
{
    private static bool Prefix(ItemBattery __instance, ref float __result)
    {
        if (__instance.GetComponent<Jammer>() == null) return true;
        __result = 0f; return false;
    }
}

[HarmonyPatch(typeof(ItemBattery), nameof(ItemBattery.TransferCharge))]
internal static class JammerNoDonateCharge
{
    private static bool Prefix(ItemBattery __instance, ref float __result)
    {
        if (__instance.GetComponent<Jammer>() == null) return true;
        __result = 0f; return false;
    }
}

[HarmonyPatch(typeof(ItemBattery), nameof(ItemBattery.ChargeBattery))]
internal static class JammerNoCharger
{
    private static bool Prefix(ItemBattery __instance) => __instance.GetComponent<Jammer>() == null;
}
