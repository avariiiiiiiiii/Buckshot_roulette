using BuckshotRoulette.Items;
using Photon.Pun;
using UnityEngine;

namespace BuckshotRoulette.Shells;

/// <summary>Исполнение уже выбранного хостом патрона; клиенты не выбирают патрон повторно.</summary>
internal static class BuckshotFire
{
    internal static void Execute(ItemGun gun, bool live, int shotId, PhotonMessageInfo info = default)
    {
        if (live)
        {
            ShotTag.Pending = shotId;
            try { gun.ShootRPC(info); }
            finally { ShotTag.Pending = 0; }
            return;
        }

        // No native ShootRPC and no projectiles: a blank cannot hurt, stun,
        // knock back or break an enemy, player or valuable.
        GunFx.Blank(gun.gunMuzzle);
        gun.StartTriggerAnimation();
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        if (gun.investigateRadius > 0f && EnemyDirector.instance != null)
            EnemyDirector.instance.SetInvestigate(gun.transform.position, gun.investigateRadius);
        var grab = gun.GetComponent<PhysGrabObject>();
        if (grab != null && grab.rb != null)
            grab.rb.AddForceAtPosition(-gun.gunMuzzle.forward * gun.gunRecoilForce, gun.gunMuzzle.position, ForceMode.Impulse);
        gun.GetComponent<ItemBattery>().RemoveFullBar(1);
        var sawed = gun.GetComponent<SawedOff>();
        if (sawed != null && sawed.Active) sawed.Consume(0);
    }
}
