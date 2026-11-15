namespace BuckshotRoulette.Logic;

/// <summary>Усиление ножовкой: ×2 на один выстрел, не стакается, после выстрела снимается.</summary>
public sealed class HacksawBoost
{
    public bool Active { get; private set; }

    /// <summary>Возвращает false, если усиление уже наложено (ножовка не тратится).</summary>
    public bool Arm()
    {
        if (Active) return false;
        Active = true;
        return true;
    }

    public int Consume(int damage)
    {
        if (!Active) return damage;
        Active = false;
        return damage * 2;
    }
}
