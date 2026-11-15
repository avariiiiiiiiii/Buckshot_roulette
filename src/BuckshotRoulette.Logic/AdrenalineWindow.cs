using System;
using System.Collections.Generic;
using System.Linq;

namespace BuckshotRoulette.Logic;

/// <summary>Deadlines use a real/network clock, independent of Unity's slowed clock.</summary>
public sealed class AdrenalineWindow
{
    public const double Duration = 10;
    public const float WorldScale = 0.1f;
    private readonly Dictionary<int, double> _until = new Dictionary<int, double>();
    public int Count => _until.Count;
    public IEnumerable<int> PlayerIds => _until.Keys;
    public int[] Players => _until.Keys.ToArray();
    public double[] Deadlines => _until.Values.ToArray();
    public double Remaining(int player, double now) => _until.TryGetValue(player, out double end) ? Math.Max(0, end - now) : 0;
    public bool Contains(int player, double now) => Remaining(player, now) > 0;

    public bool TryStart(int player, double now)
    {
        if (player <= 0 || !Finite(now) || Contains(player, now)) return false;
        _until[player] = now + Duration;
        return true;
    }

    public bool Remove(int player) => _until.Remove(player);
    public void Clear() => _until.Clear();
    public bool Expire(double now)
    {
        bool changed = false;
        foreach (int player in _until.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            changed |= _until.Remove(player);
        return changed;
    }

    public bool Apply(int[] players, double[] deadlines, double now)
    {
        if (!Finite(now) || players == null || deadlines == null || players.Length != deadlines.Length || players.Length > 64)
            return false;
        var next = new Dictionary<int, double>();
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] <= 0 || !Finite(deadlines[i]) || deadlines[i] > now + Duration + 0.5 || next.ContainsKey(players[i])) return false;
            next.Add(players[i], deadlines[i]);
        }
        _until.Clear();
        foreach (var pair in next) if (pair.Value > now) _until.Add(pair.Key, pair.Value);
        return true;
    }

    public static float ForceMultiplier(float compensation, bool impulse) => impulse ? compensation : compensation * compensation;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
