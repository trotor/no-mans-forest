using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Combat;

internal readonly record struct NearMiss(Unit Unit, int DistanceCm, int RadiusCm);

internal sealed record ShotResult(Vec2 End, Unit? Hit, IReadOnlyList<NearMiss> NearMisses);

/// <summary>One bullet: aim error, then terrain and cover along the flight, then the first man in its path.</summary>
internal static class Ballistics
{
    public static ShotResult Trace(Simulation sim, Unit shooter, Unit target) =>
        Trace(sim, shooter, target.Position, AimAboveGroundCm(sim.Map, target), target);

    /// <summary>The middle of his body — or, in a foxhole, of what shows above the rim (his head and shoulders).</summary>
    public static int AimAboveGroundCm(GridMap map, Unit target)
    {
        int height = StanceRules.HeightCm(target.Stance);
        int rim = CoverFinder.InPit(map, target.Position) ? CoverFinder.RimAboveCm(map, target.Position.ToCell()) : 0;
        return rim > 0 && rim < height ? (rim + height) / 2 : height * CombatRules.AimPointPct / 100;
    }

    /// <summary>Area fire: a round at a place (spec 2026-09-26-squads-area-fire-design §3).</summary>
    public static ShotResult TraceAt(Simulation sim, Unit shooter, Vec2 point) =>
        Trace(sim, shooter, point, CombatRules.AreaAimHeightCm, null);

    /// <param name="aimAboveGroundCm">How high above the ground at <paramref name="aimAt"/> he aims.</param>
    /// <param name="target">The man aimed at; none for area fire (every enemy near the place then counts as aimed at).</param>
    private static ShotResult Trace(Simulation sim, Unit shooter, Vec2 aimAt, int aimAboveGroundCm, Unit? target)
    {
        var map = sim.Map;
        var weapon = shooter.Weapon ?? throw new InvalidOperationException("An unarmed unit cannot fire.");
        var from = shooter.Position;
        var toTarget = aimAt - from;
        long distance = Math.Max(1, IntMath.Isqrt(toTarget.LengthSquared));

        int movingPct = shooter.MoveTarget is null ? 100
            : shooter.MoveMode == MoveMode.Run ? CombatRules.RunningFireSpreadPct : CombatRules.WalkingFireSpreadPct;
        int spread = CombatRules.EffectiveSpreadMicroRad(weapon.SpreadMrad, shooter, movingPct)
                     * (150 - shooter.Marksmanship) / 100 * (target is null ? 100 : CombatRules.TargetMovingSpreadPct(target)) / 100;
        int lateralMicroRad = spread == 0 ? 0 : sim.Rng.NextInt(-spread, spread + 1);
        int verticalMicroRad = spread == 0 ? 0 : sim.Rng.NextInt(-spread, spread + 1);

        long lateral = distance * lateralMicroRad / 1_000_000;
        var aim = new Vec2(
            aimAt.X + (int)(-toTarget.Y * lateral / distance),
            aimAt.Y + (int)(toTarget.X * lateral / distance));
        long fromHeight = map.CellAt(from).GroundHeightCm + StanceRules.EyeHeightCm(shooter.Stance);
        long aimHeight = map.CellAt(aimAt).GroundHeightCm + aimAboveGroundCm + distance * verticalMicroRad / 1_000_000;

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
            if (CoverFinder.InPit(map, unit.Position))
                radius = radius * CombatRules.PitHitWidthPct / 100; // only his head and shoulders show
            long ground = map.CellAt(unit.Position).GroundHeightCm;
            long height = HeightAt(along);
            if (side <= radius && height >= ground && height <= ground + StanceRules.HeightCm(unit.Stance))
            {
                hit = unit;
                hitAt = along;
            }
        }

        long flight = hit is null ? stopAt : hitAt;
        var end = hit?.Position ?? new Vec2(from.X + (int)(dir.X * flight / dirLength), from.Y + (int)(dir.Y * flight / dirLength));
        var misses = new List<NearMiss>();
        foreach (var unit in sim.Units)
        {
            if (unit == hit || unit.Side == shooter.Side || unit.IsOutOfAction)
                continue;
            var (along, side) = Project(unit.Position - from, dir, dirLength);
            if (along <= 0)
                continue;
            // Beyond where the bullet stopped, what counts is how close to him it struck (cover right in front of him).
            long missBy = along <= flight ? side : IntMath.Isqrt((unit.Position - end).LengthSquared);
            // The man aimed at knows he is being shot at; bystanders only notice bullets close by. Under area fire every
            // enemy close to the place is the man aimed at.
            // (The place counts only if the rounds got that far; stopped short, only where they struck.)
            long aimedSq = (long)CombatRules.AimedMissRadiusCm * CombatRules.AimedMissRadiusCm;
            bool reached = flight >= distance - CombatRules.AimedMissRadiusCm;
            long nearPlaceSq = Math.Min(reached ? (unit.Position - aimAt).LengthSquared : long.MaxValue, (unit.Position - end).LengthSquared);
            bool aimedAt = unit == target || (target is null && nearPlaceSq <= aimedSq);
            int radius = aimedAt ? CombatRules.AimedMissRadiusCm : CombatRules.NearMissRadiusCm;
            if (target is null && aimedAt)
                missBy = Math.Min(missBy, IntMath.Isqrt(nearPlaceSq));
            if (missBy <= radius)
                misses.Add(new NearMiss(unit, (int)missBy, radius));
        }

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
            // The log or stone he rests his rifle on, right in front of him, is not in his way.
            bool resting = Math.Max(Math.Abs(coord.X - a.X), Math.Abs(coord.Y - a.Y)) <= 1;
            if (h <= cell.GroundHeightCm)
                return s;
            if (h < cell.GroundHeightCm + cell.ObstacleHeightCm && cell.Cover > 0 && sim.Rng.NextInt(255) < cell.Cover)
                return s;
            if (!resting && h < cell.GroundHeightCm + cell.LowCoverHeightCm && cell.LowCover > 0 && sim.Rng.NextInt(255) < cell.LowCover)
                return s;
        }
        return range;
    }
}
