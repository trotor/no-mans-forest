using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client;

/// <summary>"The men do their best" with a move order: take cover next to the chosen spot and never give up on an unreachable one.</summary>
public static class MovePlanner
{
    public const int CoverSearchCells = 3;
    public const int ReachableSearchCells = 8;
    private const int CoverScorePerCell = 40;
    private const int CoverObstacleMinHeightCm = 50;

    /// <summary>
    /// The spot itself if it is at least as good as anything nearby; otherwise the free passable cell within a few metres
    /// that sits beside the best cover (a rock, a tree trunk), each metre away costing a little.
    /// </summary>
    public static Vec2 SeekCover(GridMap map, Vec2 spot, ISet<CellCoord> taken)
    {
        var origin = spot.ToCell();
        Vec2 best = spot;
        int bestScore = map.InBounds(origin) && map[origin].IsPassable ? CoverScore(map, origin) : int.MinValue;
        for (int r = 1; r <= CoverSearchCells; r++)
        {
            foreach (var cell in Ring(origin, r))
            {
                if (!map.InBounds(cell) || !map[cell].IsPassable || taken.Contains(cell))
                    continue;
                int score = CoverScore(map, cell) - r * CoverScorePerCell;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell.CenterCm;
                }
            }
        }
        return best;
    }

    /// <summary>The spot if a path reaches it, else the nearest cell around it that can be reached, else null.</summary>
    public static Vec2? Reachable(GridMap map, Vec2 from, Vec2 spot)
    {
        // Region lookups, not searches: on a 1 km map a search per candidate cell would freeze the game.
        if (map.Contains(spot) && Pathfinder.Reachable(map, from, spot))
            return spot;
        var origin = spot.ToCell();
        for (int r = 1; r <= ReachableSearchCells; r++)
        {
            foreach (var cell in Ring(origin, r))
            {
                if (map.InBounds(cell) && map[cell].IsPassable && Pathfinder.Reachable(map, from, cell.CenterCm))
                    return cell.CenterCm;
            }
        }
        return null;
    }

    /// <summary>Best cover offered by the neighbouring cells (only obstacles tall enough to hide behind count).</summary>
    public static int CoverScore(GridMap map, CellCoord cell)
    {
        int best = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var n = new CellCoord(cell.X + dx, cell.Y + dy);
                if ((dx != 0 || dy != 0) && map.InBounds(n) && map[n].ObstacleHeightCm >= CoverObstacleMinHeightCm)
                    best = Math.Max(best, map[n].Cover);
            }
        }
        return best;
    }

    /// <summary>Cells at Chebyshev distance r, nearest (Euclidean) first, in a fixed order.</summary>
    private static IEnumerable<CellCoord> Ring(CellCoord origin, int r)
    {
        var cells = new List<CellCoord>();
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r)
                    cells.Add(new CellCoord(origin.X + dx, origin.Y + dy));
        return cells.OrderBy(c => (c.X - origin.X) * (c.X - origin.X) + (c.Y - origin.Y) * (c.Y - origin.Y))
            .ThenBy(c => c.Y).ThenBy(c => c.X);
    }
}
