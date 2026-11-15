using BuckshotRoulette.Logic;
using Xunit;

namespace BuckshotRoulette.Logic.Tests;

public class JammerChargesTests
{
    [Theory]
    [InlineData(100, 3)]
    [InlineData(66, 2)]
    [InlineData(33, 1)]
    [InlineData(0, 0)]
    [InlineData(-100, 0)]
    [InlineData(200, 3)]
    public void Restores_saved_charge_counts_without_refilling(int saved, int expected)
    {
        Assert.Equal(expected, new JammerCharges(saved).Remaining);
    }

    [Fact]
    public void Duplicate_or_early_network_requests_do_not_spend_another_charge()
    {
        var counter = new JammerCharges(100);
        Assert.True(counter.TryUse(10));
        Assert.False(counter.TryUse(10));
        Assert.False(counter.TryUse(11.49));
        Assert.Equal(2, counter.Remaining);
        Assert.True(counter.TryUse(11.5));
        Assert.Equal(1, counter.Remaining);
    }

    [Fact]
    public void Spending_survives_two_level_changes_and_stops_after_three_uses()
    {
        var counter = new JammerCharges(100);
        Assert.True(counter.TryUse(0));
        Assert.Equal(66, counter.SavedPercent);
        counter = new JammerCharges(counter.SavedPercent);
        Assert.True(counter.TryUse(0));
        Assert.Equal(33, counter.SavedPercent);
        counter = new JammerCharges(counter.SavedPercent);
        Assert.True(counter.TryUse(0));
        Assert.Equal(0, counter.SavedPercent);
        Assert.False(counter.TryUse(100));
        Assert.False(new JammerCharges(counter.SavedPercent).TryUse(0));
    }

    [Fact]
    public void Invalid_timestamps_do_not_spend_charges()
    {
        var counter = new JammerCharges(100);
        Assert.False(counter.TryUse(double.NaN));
        Assert.False(counter.TryUse(double.PositiveInfinity));
        Assert.False(counter.TryUse(double.NegativeInfinity));
        Assert.Equal(3, counter.Remaining);
    }
}
