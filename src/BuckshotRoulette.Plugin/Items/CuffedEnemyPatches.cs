using BuckshotRoulette.Items;
using HarmonyLib;

namespace BuckshotRoulette.Patches;

[HarmonyPatch(typeof(Enemy), "Awake")]
internal static class CuffedEnemyInitializationPatch
{
    private static void Postfix(Enemy __instance)
    {
        if (__instance != null && __instance.GetComponent<CuffedEnemy>() == null)
            __instance.gameObject.AddComponent<CuffedEnemy>();
    }
}

[HarmonyPatch(typeof(EnemyTriggerAttack), "OnTriggerStay")]
internal static class CuffedEnemyContactAttackPatch
{
    private static bool Prefix(EnemyTriggerAttack __instance) =>
        __instance == null || !CuffedEnemy.IsTargetCuffed(__instance.Enemy);
}

[HarmonyPatch(typeof(EnemyRobe), "UpdateState")]
internal static class CuffedRobeAttackPatch
{
    private static void Prefix(EnemyRobe __instance, ref EnemyRobe.State __0)
    {
        if (!CuffedEnemy.IsTargetCuffed(__instance.enemy)) return;
        if (__0 == EnemyRobe.State.Attack || __0 == EnemyRobe.State.LookUnderAttack
            || __0 == EnemyRobe.State.StuckAttack)
            __0 = EnemyRobe.State.Roam;
    }
}

[HarmonyPatch(typeof(EnemyHunter), "UpdateState")]
internal static class CuffedHunterStatePatch
{
    private static void Prefix(EnemyHunter __instance, ref EnemyHunter.State __0)
    {
        if (CuffedEnemy.IsTargetCuffed(__instance.enemy)
            && (__0 == EnemyHunter.State.Aim || __0 == EnemyHunter.State.Shoot))
            __0 = EnemyHunter.State.Roam;
    }
}

[HarmonyPatch(typeof(EnemyHunter), "Update")]
internal static class CuffedHunterAttackPatch
{
    private static void Prefix(EnemyHunter __instance)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !CuffedEnemy.IsTargetCuffed(__instance.enemy)) return;
        if (__instance.currentState == EnemyHunter.State.Aim || __instance.currentState == EnemyHunter.State.Shoot)
            __instance.UpdateState(EnemyHunter.State.Roam);
    }
}

[HarmonyPatch(typeof(EnemyBombThrower), "UpdateState")]
internal static class CuffedBombThrowerStatePatch
{
    private static void Prefix(EnemyBombThrower __instance, ref EnemyBombThrower.State __0)
    {
        if (CuffedEnemy.IsTargetCuffed(__instance.enemy)
            && (__0 == EnemyBombThrower.State.Attack || __0 == EnemyBombThrower.State.Melee))
            __0 = EnemyBombThrower.State.Roam;
    }
}

[HarmonyPatch(typeof(EnemyBombThrower), "Update")]
internal static class CuffedBombThrowerAttackPatch
{
    private static void Prefix(EnemyBombThrower __instance)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !CuffedEnemy.IsTargetCuffed(__instance.enemy)) return;
        if (__instance.currentState == EnemyBombThrower.State.Attack || __instance.currentState == EnemyBombThrower.State.Melee)
            __instance.UpdateState(EnemyBombThrower.State.Roam);
    }
}

[HarmonyPatch(typeof(EnemySlowWalker), "UpdateState")]
internal static class CuffedSlowWalkerStatePatch
{
    private static void Prefix(EnemySlowWalker __instance, ref EnemySlowWalker.State __0)
    {
        if (CuffedEnemy.IsTargetCuffed(__instance.enemy)
            && (__0 == EnemySlowWalker.State.Attack || __0 == EnemySlowWalker.State.StuckAttack
                || __0 == EnemySlowWalker.State.LookUnderAttack))
            __0 = EnemySlowWalker.State.Roam;
    }
}

[HarmonyPatch(typeof(EnemySlowWalker), "Update")]
internal static class CuffedSlowWalkerAttackPatch
{
    private static void Prefix(EnemySlowWalker __instance)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !CuffedEnemy.IsTargetCuffed(__instance.enemy)) return;
        if (__instance.currentState == EnemySlowWalker.State.Attack || __instance.currentState == EnemySlowWalker.State.StuckAttack
            || __instance.currentState == EnemySlowWalker.State.LookUnderAttack)
            __instance.UpdateState(EnemySlowWalker.State.Roam);
    }
}

[HarmonyPatch(typeof(SlowWalkerAttack), nameof(SlowWalkerAttack.SlowWalkerAttackStart))]
internal static class CuffedSlowWalkerAnimationAttackPatch
{
    private static bool Prefix(SlowWalkerAttack __instance) => !CuffedEnemy.IsTargetCuffed(__instance.enemy);
}
