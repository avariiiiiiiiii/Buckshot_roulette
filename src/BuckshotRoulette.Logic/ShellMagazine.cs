using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuckshotRoulette.Logic;

public enum Shell
{
    Blank,
    Live,
}

/// <summary>
/// Магазин патронов: фиксированный порядок, но снаружи известны только счётчики.
/// Перечисление отдаёт ещё не выстрелянные патроны в порядке выстрела.
/// </summary>
public sealed class ShellMagazine : IEnumerable<Shell>
{
    private readonly Queue<Shell> _queue;

    public ShellMagazine(IEnumerable<Shell> shells)
    {
        _queue = new Queue<Shell>(shells);
        foreach (var s in _queue)
        {
            if (s == Shell.Live) Live++;
            else Blank++;
        }
    }

    public int Live { get; private set; }
    public int Blank { get; private set; }
    public int Remaining => Live + Blank;
    public bool IsEmpty => Remaining == 0;
    public double LiveChance => Remaining == 0 ? 0.0 : (double)Live / Remaining;

    public static ShellMagazine Random(Random rng, int minTotal, int maxTotal)
    {
        if (minTotal < 2) throw new ArgumentOutOfRangeException(nameof(minTotal), "нужно минимум 2 патрона: живой и холостой");
        if (maxTotal < minTotal) throw new ArgumentOutOfRangeException(nameof(maxTotal));

        int total = rng.Next(minTotal, maxTotal + 1);
        int live = rng.Next(1, total);
        var shells = new Shell[total];
        for (int i = 0; i < total; i++) shells[i] = i < live ? Shell.Live : Shell.Blank;
        for (int i = total - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shells[i], shells[j]) = (shells[j], shells[i]);
        }
        return new ShellMagazine(shells);
    }

    public Shell Fire()
    {
        if (_queue.Count == 0) throw new InvalidOperationException("магазин пуст");
        var shell = _queue.Dequeue();
        if (shell == Shell.Live) Live--;
        else Blank--;
        return shell;
    }

    /// <summary>Остаток магазина одной строкой: L — живой, B — холостой.</summary>
    public string Serialize()
    {
        var sb = new StringBuilder(_queue.Count);
        foreach (var s in _queue) sb.Append(s == Shell.Live ? 'L' : 'B');
        return sb.ToString();
    }

    public static ShellMagazine Deserialize(string data)
    {
        var shells = new List<Shell>(data.Length);
        foreach (char c in data)
        {
            switch (c)
            {
                case 'L': shells.Add(Shell.Live); break;
                case 'B': shells.Add(Shell.Blank); break;
                default: throw new FormatException($"неизвестный патрон '{c}'");
            }
        }
        return new ShellMagazine(shells);
    }

    public IEnumerator<Shell> GetEnumerator() => _queue.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
