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
                int cover = CoveredAt(map, cell, threat);
                if (cover == 0 || Occupied(sim, unit, cell.CenterCm))
                    continue;
                int penalty = (int)(IntMath.Isqrt(distanceSq) * CombatRules.CoverDistancePenaltyPerM / SimConstants.CentimetersPerCell);
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
    /// The best cover a neighbouring obstacle gives this cell against the threat (neighbours within 45° of its direction),
    /// or against any direction when the threat is unknown; 0 when none.
    /// </summary>
    public static int CoveredAt(GridMap map, CellCoord cell, Vec2? threat)
    {
        var toThreat = threat is { } t ? t - cell.CenterCm : Vec2.Zero;
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
                if (threat is not null && !Facing(new Vec2(dx, dy), toThreat))
                    continue;
                best = Math.Max(best, map[n].Cover);
            }
        }
        return best;
    }

    /// <summary>The offset points within 45° of the direction (cos² ≥ ½, compared in integers).</summary>
    private static bool Facing(Vec2 offset, Vec2 direction)
    {
        long dot = offset.Dot(direction);
        return dot > 0 && 2 * dot * dot >= offset.LengthSquared * direction.LengthSquared;
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
