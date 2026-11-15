using System;

namespace BuckshotRoulette.Logic;

/// <summary>Finite charges and host cooldown, persisted in the game's 0..100 item statistic.</summary>
public sealed class JammerCharges
{
    public const int Capacity = 3;
    public const double CooldownSeconds = 1.5;
    private double _readyAt = double.NegativeInfinity;
    public int Remaining { get; private set; }
    public int SavedPercent => Remaining * 100 / Capacity;

    public JammerCharges(int savedPercent)
    {
        int clamped = Math.Min(100, Math.Max(0, savedPercent));
        Remaining = (int)Math.Round(clamped * Capacity / 100.0, MidpointRounding.AwayFromZero);
    }

    public bool TryUse(double time)
    {
        if (Remaining == 0 || double.IsNaN(time) || double.IsInfinity(time) || time < _readyAt) return false;
        Remaining--;
        _readyAt = time + CooldownSeconds;
        return true;
    }
}
