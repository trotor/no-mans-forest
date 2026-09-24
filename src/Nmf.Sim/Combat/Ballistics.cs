using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Combat;

internal readonly record struct NearMiss(Unit Unit, int DistanceCm);

internal sealed record ShotResult(Vec2 End, Unit? Hit, IReadOnlyList<NearMiss> NearMisses);

/// <summary>One bullet: aim error, then terrain and cover along the flight, then the first man in its path.</summary>
internal static class Ballistics
{
    public static ShotResult Trace(Simulation sim, Unit shooter, Unit target)
    {
        var map = sim.Map;
        var weapon = shooter.Weapon ?? throw new InvalidOperationException("An unarmed unit cannot fire.");
        var from = shooter.Position;
        var toTarget = target.Position - from;
        long distance = Math.Max(1, IntMath.Isqrt(toTarget.LengthSquared));

        int spread = weapon.SpreadMrad * CombatRules.StanceSpreadPct(shooter.Stance) / 100 * (100 + shooter.Suppression / 5) / 100;
        int lateralMrad = spread == 0 ? 0 : sim.Rng.NextInt(-spread, spread + 1);
        int verticalMrad = spread == 0 ? 0 : sim.Rng.NextInt(-spread, spread + 1);

        long lateral = distance * lateralMrad / 1000;
        var aim = new Vec2(
            target.Position.X + (int)(-toTarget.Y * lateral / distance),
            target.Position.Y + (int)(toTarget.X * lateral / distance));
        long fromHeight = map.CellAt(from).GroundHeightCm + StanceRules.EyeHeightCm(shooter.Stance);
        long aimHeight = map.CellAt(target.Position).GroundHeightCm
                         + StanceRules.HeightCm(target.Stance) * CombatRules.AimPointPct / 100
                         + distance * verticalMrad / 1000;

        var dir = aim - from;
        long dirLength = Math.Max(1, IntMath.Isqrt(dir.LengthSquared));
        long HeightAt(long s) => fromHeight + (aimHeight - fromHeight) * s / dirLength;

        long stopAt = StopDistance(sim, from, dir, dirLength, weapon.RangeCm, HeightAt);

        Unit? hit = null;
        long hitAt = long.MaxValue;
        foreach (var unit in sim.Units)
        {
            if (unit == shooter || !unit.IsAlive)
                continue;
            var (along, side) = Project(unit.Position - from, dir, dirLength);
            if (along <= 0 || along > stopAt || along >= hitAt)
                continue;
            int radius = unit.Stance == Stance.Prone ? CombatRules.ProneHitRadiusCm : CombatRules.UnitHitRadiusCm;
            long ground = map.CellAt(unit.Position).GroundHeightCm;
            long height = HeightAt(along);
            if (side <= radius && height >= ground && height <= ground + StanceRules.HeightCm(unit.Stance))
            {
                hit = unit;
                hitAt = along;
            }
        }

        long flight = hit is null ? stopAt : hitAt;
        var misses = new List<NearMiss>();
        foreach (var unit in sim.Units)
        {
            if (unit == hit || unit.Side == shooter.Side || unit.IsOutOfAction)
                continue;
            var (along, side) = Project(unit.Position - from, dir, dirLength);
            if (along > 0 && along <= flight && side <= CombatRules.NearMissRadiusCm)
                misses.Add(new NearMiss(unit, (int)side));
        }

        var end = hit?.Position ?? new Vec2(from.X + (int)(dir.X * flight / dirLength), from.Y + (int)(dir.Y * flight / dirLength));
        return new ShotResult(end, hit, misses);
    }

    /// <summary>Distance along the flight line and sideways distance from it, in centimetres.</summary>
    private static (long Along, long Side) Project(Vec2 rel, Vec2 dir, long dirLength) =>
        (((long)rel.X * dir.X + (long)rel.Y * dir.Y) / dirLength,
         Math.Abs((long)rel.X * dir.Y - (long)rel.Y * dir.X) / dirLength);

    /// <summary>Where along the line the bullet stops: in the ground, in cover, at the map edge or at maximum range.</summary>
    private static long StopDistance(Simulation sim, Vec2 from, Vec2 dir, long dirLength, long range, Func<long, long> heightAt)
    {
        var map = sim.Map;
        var end = new Vec2(from.X + (int)(dir.X * range / dirLength), from.Y + (int)(dir.Y * range / dirLength));
        var a = from.ToCell();
        var b = end.ToCell();
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int sx = Math.Sign(b.X - a.X), sy = Math.Sign(b.Y - a.Y);
        int err = dx - dy;
        int x = a.X, y = a.Y;
        long last = 0;
        while (x != b.X || y != b.Y)
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            var coord = new CellCoord(x, y);
            if (!map.InBounds(coord))
                return last;
            var (s, _) = Project(coord.CenterCm - from, dir, dirLength);
            if (s <= 0)
                continue;
            if (s > range)
                return range;
            last = s;
            var cell = map[coord];
            long h = heightAt(s);
            if (h <= cell.GroundHeightCm)
                return s;
            if (h < cell.GroundHeightCm + cell.ObstacleHeightCm && cell.Cover > 0 && sim.Rng.NextInt(255) < cell.Cover)
                return s;
        }
        return range;
    }
}
