using System;
using BuckshotRoulette.Logic;
using Xunit;

namespace BuckshotRoulette.Logic.Tests;

public class AdrenalineTests
{
    [Fact]
    public void Deadlines_are_ten_real_seconds_and_duplicate_use_does_not_extend_them()
    {
        var state = new AdrenalineWindow();
        Assert.True(state.TryStart(12, 100));
        Assert.False(state.TryStart(12, 105));
        Assert.Equal(5, state.Remaining(12, 105));
        Assert.False(state.Expire(109.999));
        Assert.True(state.Expire(110));
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void Two_players_have_independent_windows_without_multiplying_world_slowdown()
    {
        var state = new AdrenalineWindow();
        Assert.True(state.TryStart(1, 0));
        Assert.True(state.TryStart(2, 4));
        Assert.True(state.Expire(10));
        Assert.False(state.Contains(1, 10));
        Assert.True(state.Contains(2, 10));
        Assert.Equal(4, state.Remaining(2, 10));
        Assert.Equal(.1f, AdrenalineWindow.WorldScale);
    }

    [Fact]
    public void Late_join_and_host_migration_keep_the_original_deadline()
    {
        var host = new AdrenalineWindow();
        host.TryStart(7, 20);
        var client = new AdrenalineWindow();
        Assert.True(client.Apply(host.Players, host.Deadlines, 23));
        Assert.Equal(7, client.Remaining(7, 23));
        var newHost = new AdrenalineWindow();
        Assert.True(newHost.Apply(client.Players, client.Deadlines, 28));
        Assert.Equal(2, newHost.Remaining(7, 28));
        Assert.True(newHost.Expire(30));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_clocks_cannot_consume_an_injection(double time)
    {
        Assert.False(new AdrenalineWindow().TryStart(1, time));
    }

    [Fact]
    public void Invalid_snapshots_do_not_overwrite_the_last_valid_state()
    {
        var state = new AdrenalineWindow();
        state.TryStart(5, 0);
        Assert.False(state.Apply(new[] { 1, 1 }, new[] { 8d, 9d }, 0));
        Assert.False(state.Apply(new[] { 1 }, new[] { 100d }, 0));
        Assert.False(state.Apply(new[] { 1 }, new[] { double.NaN }, 0));
        Assert.False(state.Apply(new[] { 1 }, Array.Empty<double>(), 0));
        Assert.True(state.Contains(5, 0));
        Assert.Equal(1, state.Count);
    }

    [Fact]
    public void Compensated_body_falls_and_moves_at_normal_real_time_speed()
    {
        const double dt = .02, gravity = -9.81;
        double normalV = 2, boostedV = 20, normalY = 0, boostedY = 0;
        float continuous = AdrenalineWindow.ForceMultiplier(10, false);
        for (int i = 0; i < 50; i++)
        {
            normalV += gravity * dt;
            boostedV += gravity * continuous * dt * .1;
            normalY += normalV * dt;
            boostedY += boostedV * dt * .1;
        }
        Assert.Equal(normalY, boostedY, 8);
        Assert.Equal(normalV, boostedV / 10, 8);
        Assert.Equal(10, AdrenalineWindow.ForceMultiplier(10, true));
    }
}
