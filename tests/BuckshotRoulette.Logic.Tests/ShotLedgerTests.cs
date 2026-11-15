using BuckshotRoulette.Logic;
using Xunit;

namespace BuckshotRoulette.Logic.Tests;

public class ShotLedgerTests
{
    private int _taken;
    private bool _next = true;

    private bool Take()
    {
        _taken++;
        return _next;
    }

    [Fact]
    public void Hits_of_one_shot_share_one_shell()
    {
        var ledger = new ShotLedger();
        Assert.True(ledger.Resolve(ShotLedger.ShotKey(7), 0, 1, Take, out bool first));
        _next = false;
        Assert.True(ledger.Resolve(ShotLedger.ShotKey(7), 0.5, 1, Take, out bool second));
        Assert.Equal(1, _taken);
        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void Two_shooters_at_the_same_moment_take_two_shells()
    {
        var ledger = new ShotLedger();
        ledger.Resolve(ShotLedger.ShooterKey(1), 0, 0.35, Take, out _);
        _next = false;
        Assert.False(ledger.Resolve(ShotLedger.ShooterKey(2), 0.01, 0.35, Take, out bool fresh));
        Assert.Equal(2, _taken);
        Assert.True(fresh);
    }

    [Fact]
    public void Same_key_after_window_takes_a_new_shell()
    {
        var ledger = new ShotLedger();
        ledger.Resolve(ShotLedger.ShooterKey(1), 0, 0.35, Take, out _);
        _next = false;
        Assert.False(ledger.Resolve(ShotLedger.ShooterKey(1), 0.4, 0.35, Take, out bool fresh));
        Assert.Equal(2, _taken);
        Assert.True(fresh);
    }

    [Fact]
    public void Expired_entries_are_dropped()
    {
        var ledger = new ShotLedger();
        ledger.Resolve(ShotLedger.ShotKey(1), 0, 1, Take, out _);
        ledger.Resolve(ShotLedger.ShotKey(2), 0, 1, Take, out _);
        ledger.Resolve(ShotLedger.ShotKey(3), 5, 1, Take, out _);
        Assert.Equal(1, ledger.Count);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    [InlineData(0)]
    public void Shot_and_shooter_keys_never_collide(int id)
    {
        Assert.NotEqual(ShotLedger.ShotKey(id), ShotLedger.ShooterKey(id));
    }

    [Fact]
    public void Reserved_miss_spends_a_shell_and_first_dealer_hit_does_not_spend_twice()
    {
        var ledger = new ShotLedger();
        _next = false;
        Assert.False(ledger.Reserve(ShotLedger.ShotKey(1), 0, 30, Take));
        Assert.Equal(1, _taken); // No hit follows: the miss still spent its shell.
        Assert.False(ledger.Resolve(ShotLedger.ShotKey(1), .1, 30, Take, out bool fresh));
        Assert.True(fresh);
        for (int pellet = 1; pellet < 6; pellet++)
        {
            Assert.False(ledger.Resolve(ShotLedger.ShotKey(1), .2, 30, Take, out fresh));
            Assert.False(fresh);
        }
        Assert.Equal(1, _taken);
    }

    [Fact]
    public void Firing_a_full_shotgun_into_empty_space_exhausts_the_pool()
    {
        var pool = new ShellMagazine(new[] { Shell.Live, Shell.Blank, Shell.Live, Shell.Blank });
        var ledger = new ShotLedger();
        for (int shot = 1; shot <= 4; shot++)
            ledger.Reserve(ShotLedger.ShotKey(shot), shot, 30, () => pool.Fire() == Shell.Live);
        Assert.True(pool.IsEmpty);
        Assert.Equal(0, pool.Live);
        Assert.Equal(0, pool.Blank);
    }

    [Fact]
    public void Duplicate_reservations_and_other_weapon_hits_cannot_steal_a_shotguns_reserved_result()
    {
        var ledger = new ShotLedger();
        Assert.True(ledger.Reserve(ShotLedger.ShotKey(10), 0, 30, Take));
        _next = false;
        Assert.True(ledger.Reserve(ShotLedger.ShotKey(10), .1, 30, Take));
        Assert.False(ledger.Resolve(ShotLedger.ShotKey(11), .2, 30, Take, out _));
        Assert.True(ledger.Resolve(ShotLedger.ShotKey(10), .3, 30, Take, out _));
        Assert.Equal(2, _taken);
    }
}
