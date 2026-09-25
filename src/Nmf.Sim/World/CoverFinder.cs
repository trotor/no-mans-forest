using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.World;

/// <summary>Where a man under fire can take cover: a free cell with a solid obstacle next to it, on the enemy's side (spec 2026-09-25-take-cover-design §4).</summary>
public static class CoverFinder
{
    /// <summary>The best cover cell within reach, or null when there is none worth running to.</summary>
    public static Vec2? Find(Simulation sim, Unit unit, Vec2? threat)
    {
        var map = sim.Map;
        var origin = unit.Position.ToCell();
        int radiusCells = CombatRules.CoverSearchCm / SimConstants.CentimetersPerCell;
        long maxSq = (long)CombatRules.CoverSearchCm * CombatRules.CoverSearchCm;
        // With the enemy close by, never run toward him (or past him) for cover: only sideways or away.
        var toThreat = threat is { } t0 ? t0 - unit.Position : Vec2.Zero;
        bool closeThreat = threat is not null && toThreat.LengthSquared < 4 * maxSq;
        var candidates = new List<(int Score, int Order, CellCoord Cell)>();
        int order = 0;
        for (int dy = -radiusCells; dy <= radiusCells; dy++)
        {
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                var cell = new CellCoord(origin.X + dx, origin.Y + dy);
                order++;
                if ((dx == 0 && dy == 0) || !map.InBounds(cell) || !map[cell].IsPassable)
                    continue;
                long distanceSq = (cell.CenterCm - unit.Position).LengthSquared;
                if (distanceSq > maxSq)
                    continue;
                if (closeThreat && (cell.CenterCm - unit.Position).Dot(toThreat) > 0)
                    continue;
                int cover = CoveredAt(map, cell, threat);
                if (cover == 0 || Occupied(sim, unit, cell.CenterCm))
                    continue;
                int penalty = (int)(IntMath.Isqrt(distanceSq) * CombatRules.CoverDistancePenaltyPerM / SimConstants.CentimetersPerCell);
                if (cover - penalty > 0) // thin cover far off is not worth the run; he gets down instead
                    candidates.Add((cover - penalty, order, cell));
            }
        }
        foreach (var (_, _, cell) in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Order))
        {
            if (Pathfinder.FindPath(map, unit.Position, cell.CenterCm) is not null)
                return cell.CenterCm;
        }
        return null;
    }

    /// <summary>
    /// The cover this cell has against the threat: the obstacle in the neighbouring cell the incoming line crosses first
    /// (the same cell walk as sight and fragments). With the threat unknown, the best neighbouring obstacle. 0 when none.
    /// </summary>
    public static int CoveredAt(GridMap map, CellCoord cell, Vec2? threat)
    {
        if (threat is { } t)
        {
            var first = FirstStepToward(cell, t.ToCell());
            return first is { } n && map.InBounds(n) && map[n].ObstacleHeightCm >= CombatRules.CoverObstacleMinCm ? map[n].Cover : 0;
        }
        int best = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var n = new CellCoord(cell.X + dx, cell.Y + dy);
                if (!map.InBounds(n) || map[n].ObstacleHeightCm < CombatRules.CoverObstacleMinCm || map[n].Cover == 0)
                    continue;
                best = Math.Max(best, map[n].Cover);
            }
        }
        return best;
    }

    /// <summary>The first cell a Bresenham walk from <paramref name="from"/> toward <paramref name="to"/> enters; null if they are the same cell.</summary>
    private static CellCoord? FirstStepToward(CellCoord from, CellCoord to)
    {
        if (from == to)
            return null;
        int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
        int sx = Math.Sign(to.X - from.X), sy = Math.Sign(to.Y - from.Y);
        int e2 = 2 * (dx - dy);
        int x = from.X, y = from.Y;
        if (e2 > -dy) x += sx;
        if (e2 < dx) y += sy;
        return new CellCoord(x, y);
    }

    /// <summary>A comrade already lies there or is on his way there.</summary>
    private static bool Occupied(Simulation sim, Unit unit, Vec2 spot)
    {
        long radiusSq = (long)CombatRules.CoverOccupiedCm * CombatRules.CoverOccupiedCm;
        foreach (var other in sim.Units)
        {
            if (other == unit || other.Side != unit.Side || other.IsOutOfAction)
                continue;
            if ((other.Position - spot).LengthSquared < radiusSq
                || (other.MoveTarget is { } goal && (goal - spot).LengthSquared < radiusSq))
                return true;
        }
        return false;
    }
}
