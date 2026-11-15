using BuckshotRoulette.Logic;
using BuckshotRoulette.Shells;
using HarmonyLib;
using UnityEngine;

namespace BuckshotRoulette.Dealer;

/// <summary>
/// Урон по дилеру из оружия игроков идёт через общий пул патронов: боевой — урон проходит, холостой — обнуляется.
/// Оружием считаются огнестрел, лазеры и ближний бой; взрывы, падение предметов и т. п. проходят как есть.
/// Один выстрел — один патрон: попадания с одним номером выстрела (ShotTag) делят результат; непомеченное оружие
/// (ближний бой, пушка тележки) делит его в коротком окне на стрелка, а не на дилера — два игрока тянут два патрона.
/// Источник урона в EnemyHealth.Hurt не передаётся, поэтому он запоминается в HurtCollider.EnemyHurt, откуда Hurt и вызывается.
/// Оглушение ударом ценности: дилера валят только средние и тяжёлые ценности (масса от 1.0), лёгкие (0.5–0.8) — нет.
/// </summary>
internal static class DealerDamagePatches
{
    /// <summary>Номер выстрела живёт, пока могут прилетать его попадания (луч лазера держится несколько секунд).</summary>
    private const double ShotWindowSeconds = 30.0;

    /// <summary>Непомеченное оружие: все попадания одного стрелка в этом окне — один удар.</summary>
    private const double ShooterWindowSeconds = 0.35;

    /// <summary>Ванильные убийства (яма, уничтожение) идут как огромное число — их не трогаем.</summary>
    private const int KillDamageThreshold = 100000;

    private static readonly ShotLedger Ledger = new ShotLedger();

    internal static void ForgetShot(int shotId) => Ledger.Forget(ShotLedger.ShotKey(shotId));
    internal static bool ReserveShotgunShot(int shotId) => Ledger.Reserve(ShotLedger.ShotKey(shotId),
        Time.timeAsDouble, ShotWindowSeconds, ShellPool.TakeForShotgun);

    /// <summary>Ключ попадания оружием, которое сейчас обрабатывается (между префиксом и финалайзером HurtCollider.EnemyHurt).</summary>
    private static long? _hitKey;
    private static double _hitWindow;

    /// <summary>Пресеты игры: Light 0.5–0.8, Medium 1–3.5, Heavy 4–6, Very Heavy 8 (дамп масс всех ценностей).</summary>
    private const float MinStunMass = 1f;

    /// <summary>Масса ценности, чей удар сейчас обрабатывается (между префиксом и финалайзером OnCollisionStay).</summary>
    private static float? _impactMass;

    [HarmonyPatch(typeof(HurtCollider), "EnemyHurt")]
    internal static class MarkWeaponHit
    {
        [HarmonyPrefix]
        private static bool Prefix(HurtCollider __instance, Enemy _enemy, ref bool __result)
        {
            _hitKey = null;
            var tag = __instance.GetComponent<ShotTag>();
            if (tag != null && tag.Id != 0)
            {
                _hitKey = ShotLedger.ShotKey(tag.Id);
                _hitWindow = ShotWindowSeconds;
            }
            else
            {
                if (__instance.GetComponentInParent<ItemGunBullet>() == null && __instance.GetComponentInParent<ItemMelee>() == null
                    && __instance.GetComponentInParent<ItemStunBaton>() == null) return true;
                var shooter = __instance.playerCausingHurt;
                _hitKey = ShotLedger.ShooterKey(shooter != null ? shooter.photonView.ViewID : __instance.GetInstanceID());
                _hitWindow = ShooterWindowSeconds;
            }
            if (!SemiFunc.IsMasterClientOrSingleplayer() || _enemy == null || !__instance.enemyLogic
                || __instance.enemyHost == _enemy || __instance.deathPit || __instance.enemyKill
                || __instance.enemyDamage >= KillDamageThreshold) return true;
            var dealer = _enemy.GetComponent<EnemyDealer>();
            if (dealer == null) return true;
            bool live = Ledger.Resolve(_hitKey.Value, Time.timeAsDouble, _hitWindow, ShellPool.Take, out bool fresh);
            dealer.OnWeaponHit(live, fresh);
            if (live) return true;
            // EnemyHurt applies stun and force BEFORE EnemyHealth.Hurt. Filtering
            // health alone let blank pellets still knock the dealer down.
            __result = false;
            return false;
        }

        [HarmonyFinalizer]
        private static void Finalizer() => _hitKey = null;
    }

    [HarmonyPatch(typeof(EnemyHealth), "Hurt")]
    internal static class RollThroughMagazine
    {
        [HarmonyPrefix]
        private static bool Prefix(EnemyHealth __instance, int _damage)
        {
            if (_hitKey == null || _damage >= KillDamageThreshold || !SemiFunc.IsMasterClientOrSingleplayer()) return true;
            var dealer = __instance.GetComponent<EnemyDealer>();
            if (dealer == null) return true;
            bool live = Ledger.Resolve(_hitKey.Value, Time.timeAsDouble, _hitWindow, () => ShellPool.Take(), out bool fresh);
            dealer.OnWeaponHit(live, fresh);
            return live;
        }
    }

    /// <summary>Оглушение от удара ценностью игра выдаёт внутри OnCollisionStay ударившего предмета — запоминаем его массу.</summary>
    [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "OnCollisionStay")]
    internal static class MarkImpactMass
    {
        [HarmonyPrefix]
        private static void Prefix(PhysGrabObjectImpactDetector __instance)
        {
            var preset = __instance.isValuable && __instance.valuableObject != null ? __instance.valuableObject.physAttributePreset : null;
            _impactMass = preset != null ? preset.mass : (float?)null;
        }

        [HarmonyFinalizer]
        private static void Finalizer() => _impactMass = null;
    }

    [HarmonyPatch(typeof(EnemyStateStunned), nameof(EnemyStateStunned.Set))]
    internal static class LightValuablesDontStun
    {
        [HarmonyPrefix]
        private static bool Prefix(EnemyStateStunned __instance)
        {
            if (_impactMass == null || __instance.GetComponent<EnemyDealer>() == null) return true;
            bool stun = _impactMass >= MinStunMass;
            Plugin.Log.LogInfo($"Удар ценностью массы {_impactMass:F2} по дилеру: {(stun ? "оглушает" : "лёгкая, не оглушает")}");
            return stun;
        }
    }
}
