using System;
using System.Numerics;

namespace BuckshotRoulette.Logic;

/// <summary>Глушилка: моб уходит от источника на f = d/2, где d — расстояние до источника.</summary>
public static class Retreat
{
    public static float Distance(float distanceToSource) => distanceToSource * 0.5f;

    /// <summary>Точка отступления по горизонтали (высота моба сохраняется). Null, если моб стоит ровно на источнике.</summary>
    public static Vector3? Target(Vector3 enemy, Vector3 source)
    {
        float dx = enemy.X - source.X;
        float dz = enemy.Z - source.Z;
        float d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < 1e-4f) return null;
        float step = Distance(d);
        return new Vector3(enemy.X + dx / d * step, enemy.Y, enemy.Z + dz / d * step);
    }
}
