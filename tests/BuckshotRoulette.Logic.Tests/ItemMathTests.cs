using System;
using System.Numerics;
using BuckshotRoulette.Logic;
using Xunit;

namespace BuckshotRoulette.Logic.Tests;

public class RetreatTests
{
    [Fact]
    public void Distance_is_half_of_distance_to_source()
    {
        Assert.Equal(5f, Retreat.Distance(10f), 4);
        Assert.Equal(0f, Retreat.Distance(0f), 4);
    }

    [Fact]
    public void Target_is_on_opposite_side_of_source_at_half_distance()
    {
        // источник в (0,0), моб в (10,0) -> уходит в (15,0): ещё на 5 м от себя прочь
        var t = Retreat.Target(new Vector3(10, 3, 0), new Vector3(0, 0, 0));
        Assert.NotNull(t);
        Assert.Equal(15f, t!.Value.X, 3);
        Assert.Equal(0f, t.Value.Z, 3);
        Assert.Equal(3f, t.Value.Y, 3);
    }

    [Fact]
    public void Target_ignores_vertical_offset_in_distance()
    {
        var t = Retreat.Target(new Vector3(0, 50, 8), new Vector3(0, 0, 0));
        Assert.Equal(12f, t!.Value.Z, 3);
    }

    [Fact]
    public void Target_is_null_when_enemy_stands_on_source()
    {
        Assert.Null(Retreat.Target(new Vector3(1, 0, 1), new Vector3(1, 7, 1)));
    }
}

public class MedicineTests
{
    [Fact]
    public void Roll_gives_only_plus20_or_minus10_percent()
    {
        var rng = new Random(1);
        for (int i = 0; i < 500; i++)
        {
            int d = Medicine.RollDelta(100, rng);
            Assert.True(d == 20 || d == -10, $"неожиданное {d}");
        }
    }

    [Fact]
    public void Roll_is_roughly_fifty_fifty()
    {
        var rng = new Random(2);
        int heals = 0;
        for (int i = 0; i < 4000; i++) if (Medicine.RollDelta(100, rng) > 0) heals++;
        Assert.InRange(heals, 1800, 2200);
    }

    [Fact]
    public void Roll_scales_with_max_health_and_rounds_up_magnitude()
    {
        var rng = new Random(3);
        for (int i = 0; i < 200; i++)
        {
            int d = Medicine.RollDelta(125, rng);
            Assert.True(d == 25 || d == -13, $"неожиданное {d}");
        }
    }
}

public class HealTests
{
    [Theory]
    [InlineData(100, 10, 10)]
    [InlineData(125, 10, 13)]
    [InlineData(100, 20, 20)]
    [InlineData(1, 10, 1)]
    public void Percent_of_max_rounds_up(int max, int percent, int expected)
    {
        Assert.Equal(expected, Heal.PercentOfMax(max, percent));
    }
}

public class HacksawTests
{
    [Fact]
    public void Unarmed_leaves_damage_alone()
    {
        var h = new HacksawBoost();
        Assert.Equal(40, h.Consume(40));
    }

    [Fact]
    public void Armed_doubles_once_then_resets()
    {
        var h = new HacksawBoost();
        Assert.True(h.Arm());
        Assert.Equal(80, h.Consume(40));
        Assert.False(h.Active);
        Assert.Equal(40, h.Consume(40));
    }

    [Fact]
    public void Arm_twice_does_not_stack_and_reports_refusal()
    {
        var h = new HacksawBoost();
        Assert.True(h.Arm());
        Assert.False(h.Arm());
        Assert.Equal(100, h.Consume(50));
        Assert.Equal(50, h.Consume(50));
    }

    [Fact]
    public void Can_arm_again_after_shot()
    {
        var h = new HacksawBoost();
        h.Arm();
        h.Consume(10);
        Assert.True(h.Arm());
    }
}
