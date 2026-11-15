using System;
using System.Collections.Generic;

namespace BuckshotRoulette.Logic;

/// <summary>
/// Какой патрон из общего пула достался каждому выстрелу. Одно нажатие курка = один патрон: все попадания одного выстрела
/// (дробины, тики луча, повторные касания удара) получают один и тот же результат.
/// Ключ — либо номер выстрела (ShotKey), либо стрелок, когда выстрел не помечен (ShooterKey, короткое окно).
/// </summary>
public sealed class ShotLedger
{
    private readonly Dictionary<long, (double Until, bool Live, bool Seen)> _entries = new Dictionary<long, (double, bool, bool)>();
    private readonly List<long> _expired = new List<long>();

    public int Count => _entries.Count;

    public static long ShotKey(int shotId) => (uint)shotId;

    public static long ShooterKey(int shooterId) => (1L << 32) | (uint)shooterId;

    /// <summary>
    /// true — боевой. Первое попадание ключа в окне берёт патрон (take) и fresh = true, остальные получают тот же результат.
    /// </summary>
    public bool Resolve(long key, double now, double window, Func<bool> take, out bool fresh)
    {
        Prune(now);
        if (_entries.TryGetValue(key, out var entry))
        {
            fresh = !entry.Seen;
            _entries[key] = (entry.Until, entry.Live, true);
            return entry.Live;
        }
        bool live = take();
        _entries[key] = (now + window, live, true);
        fresh = true;
        return live;
    }

    public bool Forget(long key) => _entries.Remove(key);

    public bool Reserve(long key, double now, double window, Func<bool> take)
    {
        Prune(now);
        if (_entries.TryGetValue(key, out var entry)) return entry.Live;
        bool live = take();
        _entries[key] = (now + window, live, false);
        return live;
    }

    private void Prune(double now)
    {
        foreach (var pair in _entries)
            if (pair.Value.Until <= now) _expired.Add(pair.Key);
        foreach (var key in _expired) _entries.Remove(key);
        _expired.Clear();
    }
}
