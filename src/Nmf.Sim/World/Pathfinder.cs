using Nmf.Sim.Core;

namespace Nmf.Sim.World;

/// <summary>Deterministic 8-directional A* over map cells.</summary>
public static class Pathfinder
{
    public const int MaxExpandedNodes = 250_000;

    private const int StraightCost = 100;
    private const int DiagonalCost = 141;

    private static readonly (int Dx, int Dy)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>
    /// Waypoints from <paramref name="start"/> (excluded) to <paramref name="target"/> (last element),
    /// or null if the target is outside the map, impassable or unreachable.
    /// </summary>
    public static List<Vec2>? FindPath(GridMap map, Vec2 start, Vec2 target)
    {
        if (!map.Contains(start) || !map.Contains(target))
            return null;
        var startCell = start.ToCell();
        var goalCell = target.ToCell();
        if (!map[goalCell].IsPassable)
            return null;
        if (startCell == goalCell)
            return [target];

        int width = map.Width;
        int count = width * map.Height;
        var cost = new int[count];
        Array.Fill(cost, int.MaxValue);
        var parent = new int[count];
        Array.Fill(parent, -1);
        var closed = new bool[count];
        var open = new PriorityQueue<int, (int F, int H, int Seq)>();

        int sequence = 0;
        int startIndex = startCell.Y * width + startCell.X;
        int goalIndex = goalCell.Y * width + goalCell.X;
        cost[startIndex] = 0;
        int startH = Heuristic(startCell, goalCell);
        open.Enqueue(startIndex, (startH, startH, sequence++));

        int expanded = 0;
        while (open.TryDequeue(out int current, out _))
        {
            if (closed[current])
                continue;
            if (current == goalIndex)
                return BuildWaypoints(parent, current, width, target);
            closed[current] = true;
            if (++expanded > MaxExpandedNodes)
                return null;

            int cx = current % width, cy = current / width;
            foreach (var (dx, dy) in Directions)
            {
                var next = new CellCoord(cx + dx, cy + dy);
                if (!map.InBounds(next) || !map[next].IsPassable)
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                // No corner cutting: both orthogonal neighbours of a diagonal step must be passable.
                if (diagonal && (!map[new CellCoord(cx + dx, cy)].IsPassable || !map[new CellCoord(cx, cy + dy)].IsPassable))
                    continue;

                int nextIndex = next.Y * width + next.X;
                if (closed[nextIndex])
                    continue;
                int step = (diagonal ? DiagonalCost : StraightCost) * map[next].MoveCostPct / 100;
                int tentative = cost[current] + step;
                if (tentative >= cost[nextIndex])
                    continue;

                cost[nextIndex] = tentative;
                parent[nextIndex] = current;
                int h = Heuristic(next, goalCell);
                open.Enqueue(nextIndex, (tentative + h, h, sequence++));
            }
        }
        return null;
    }

    /// <summary>Octile distance at the cheapest cost; admissible because every step costs at least its base cost.</summary>
    private static int Heuristic(CellCoord a, CellCoord b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        int diagonal = Math.Min(dx, dy);
        return diagonal * DiagonalCost + (Math.Max(dx, dy) - diagonal) * StraightCost;
    }

    private static List<Vec2> BuildWaypoints(int[] parent, int goalIndex, int width, Vec2 target)
    {
        var cells = new List<CellCoord>();
        for (int i = goalIndex; i != -1; i = parent[i])
            cells.Add(new CellCoord(i % width, i / width));
        cells.Reverse(); // cells[0] is the start cell

        var waypoints = new List<Vec2>();
        for (int i = 1; i < cells.Count - 1; i++)
        {
            var prev = cells[i - 1];
            var cur = cells[i];
            var next = cells[i + 1];
            bool straight = cur.X - prev.X == next.X - cur.X && cur.Y - prev.Y == next.Y - cur.Y;
            if (!straight)
                waypoints.Add(cur.CenterCm);
        }
        waypoints.Add(target);
        return waypoints;
    }
}
