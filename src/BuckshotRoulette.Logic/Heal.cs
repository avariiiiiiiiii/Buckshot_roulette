using System;

namespace BuckshotRoulette.Logic;

public static class Heal
{
    /// <summary>Проценты от максимума здоровья, округление вверх (как в игре: CeilToInt(max * p)).</summary>
    public static int PercentOfMax(int maxHealth, int percent) => (int)Math.Ceiling(maxHealth * percent / 100.0);
}

/// <summary>Просроченное лекарство: 50% +20% HP, 50% -10% HP.</summary>
public static class Medicine
{
    public static int RollDelta(int maxHealth, Random rng) =>
        rng.Next(2) == 0 ? Heal.PercentOfMax(maxHealth, 20) : -Heal.PercentOfMax(maxHealth, 10);
}
