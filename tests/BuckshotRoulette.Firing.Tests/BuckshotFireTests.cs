using BuckshotRoulette.Shells;
using Xunit;

public class BuckshotFireTests
{
    public BuckshotFireTests()
    {
        SemiFunc.Master = true;
        ShotTag.Pending = 0;
        GunFx.Blanks = 0;
        EnemyDirector.instance = new EnemyDirector();
    }

    [Fact]
    public void Blank_has_effects_and_spends_one_bar_without_creating_damage()
    {
        var gun = new ItemGun();
        BuckshotFire.Execute(gun, false, 42);
        Assert.Equal(0, gun.NativeShots);
        Assert.Equal(1, GunFx.Blanks);
        Assert.Equal(1, gun.TriggerAnimations);
        Assert.Equal(1, gun.Battery.Spent);
        Assert.Equal(1, gun.Grab.rb.Impulses);
        Assert.Equal(1, EnemyDirector.instance.Alerts);
        Assert.Equal(0, ShotTag.Pending);
    }

    [Fact]
    public void Client_plays_blank_without_spending_battery_or_applying_force()
    {
        SemiFunc.Master = false;
        var gun = new ItemGun();
        BuckshotFire.Execute(gun, false, 43);
        Assert.Equal(0, gun.NativeShots);
        Assert.Equal(1, GunFx.Blanks);
        Assert.Equal(1, gun.TriggerAnimations);
        Assert.Equal(0, gun.Battery.Spent);
        Assert.Equal(0, gun.Grab.rb.Impulses);
        Assert.Equal(0, EnemyDirector.instance.Alerts);
    }

    [Fact]
    public void Live_passes_the_reserved_identity_to_native_shot_and_spends_no_extra_bar()
    {
        var gun = new ItemGun();
        BuckshotFire.Execute(gun, true, 44);
        Assert.Equal(1, gun.NativeShots);
        Assert.Equal(44, gun.ObservedShot);
        Assert.Equal(0, GunFx.Blanks);
        Assert.Equal(0, gun.Battery.Spent); // Native ShootRPC owns the live round deduction.
        Assert.Equal(0, ShotTag.Pending);
    }

    [Fact]
    public void Failed_native_shot_does_not_leak_identity_to_another_weapon()
    {
        var gun = new ItemGun { ThrowOnShot = true };
        Assert.Throws<System.InvalidOperationException>(() => BuckshotFire.Execute(gun, true, 45));
        Assert.Equal(0, ShotTag.Pending);
    }

    [Fact]
    public void Blank_spends_the_next_shot_hacksaw_boost_without_dealing_damage()
    {
        var sawed = new BuckshotRoulette.Items.SawedOff();
        var gun = new ItemGun { Sawed = sawed };
        BuckshotFire.Execute(gun, false, 46);
        Assert.Equal(1, sawed.Consumed);
        Assert.False(sawed.Active);
        Assert.Equal(0, gun.NativeShots);
    }
}
