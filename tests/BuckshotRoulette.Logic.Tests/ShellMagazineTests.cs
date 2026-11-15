using System;
using System.Linq;
using BuckshotRoulette.Logic;
using Xunit;

namespace BuckshotRoulette.Logic.Tests;

public class ShellMagazineTests
{
    [Fact]
    public void LiveChance_is_live_over_remaining()
    {
        var m = new ShellMagazine(new[] { Shell.Live, Shell.Live, Shell.Live, Shell.Blank, Shell.Blank });
        Assert.Equal(3, m.Live);
        Assert.Equal(2, m.Blank);
        Assert.Equal(0.6, m.LiveChance, 6);
    }

    [Fact]
    public void Fire_pops_in_order_and_updates_counts_and_chance()
    {
        var m = new ShellMagazine(new[] { Shell.Blank, Shell.Live, Shell.Live, Shell.Live });
        Assert.Equal(Shell.Blank, m.Fire());
        Assert.Equal(0, m.Blank);
        Assert.Equal(3, m.Live);
        Assert.Equal(3, m.Remaining);
        Assert.Equal(1.0, m.LiveChance, 6);
    }

    [Fact]
    public void Fire_on_empty_throws()
    {
        var m = new ShellMagazine(Array.Empty<Shell>());
        Assert.True(m.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => { m.Fire(); });
    }

    [Fact]
    public void LiveChance_of_empty_is_zero()
    {
        Assert.Equal(0.0, new ShellMagazine(Array.Empty<Shell>()).LiveChance);
    }

    [Fact]
    public void Random_respects_size_bounds_and_has_both_kinds()
    {
        var rng = new Random(12345);
        for (int i = 0; i < 2000; i++)
        {
            var m = ShellMagazine.Random(rng, 2, 8);
            Assert.InRange(m.Remaining, 2, 8);
            Assert.True(m.Live >= 1);
            Assert.True(m.Blank >= 1);
        }
    }

    [Fact]
    public void Random_is_deterministic_for_a_seed()
    {
        var a = ShellMagazine.Random(new Random(7), 2, 8).ToArray();
        var b = ShellMagazine.Random(new Random(7), 2, 8).ToArray();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Random_orders_are_shuffled()
    {
        var firsts = Enumerable.Range(0, 200)
            .Select(s => ShellMagazine.Random(new Random(s), 4, 4).ToArray()[0])
            .Distinct().Count();
        Assert.Equal(2, firsts);
    }

    [Fact]
    public void Random_rejects_bad_bounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShellMagazine.Random(new Random(1), 1, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShellMagazine.Random(new Random(1), 5, 4));
    }

    [Fact]
    public void Serialize_roundtrip_keeps_order_and_position()
    {
        var m = ShellMagazine.Random(new Random(99), 3, 8);
        m.Fire();
        var copy = ShellMagazine.Deserialize(m.Serialize());
        Assert.Equal(m.ToArray(), copy.ToArray());
        Assert.Equal(m.Live, copy.Live);
        Assert.Equal(m.Blank, copy.Blank);
    }

    [Fact]
    public void Deserialize_rejects_garbage()
    {
        Assert.Throws<FormatException>(() => ShellMagazine.Deserialize("LXB"));
    }
}
