using Nmf.Sim.Core;

namespace Nmf.Sim.World;

/// <summary>Deterministic 8-directional A* over map cells.</summary>
public static class Pathfinder
{
    /// <summary>Enough to cross a 1 km × 1 km map around obstacles.</summary>
    public const int MaxExpandedNodes = 1_000_000;

    // Search buffers reused between calls (per thread); a generation stamp marks which entries belong to this search.
    [ThreadStatic] private static int[]? _cost;
    [ThreadStatic] private static int[]? _parent;
    [ThreadStatic] private static int[]? _seen;
    [ThreadStatic] private static int[]? _closed;
    [ThreadStatic] private static int _generation;
    [ThreadStatic] private static PriorityQueue<int, (int F, int H, int Seq)>? _open;

    /// <summary>Lowest F, then lowest H, then first queued — written out to avoid the generic tuple comparer.</summary>
    private sealed class PriorityOrder : IComparer<(int F, int H, int Seq)>
    {
        public static readonly PriorityOrder Instance = new();
        public int Compare((int F, int H, int Seq) a, (int F, int H, int Seq) b) =>
            a.F != b.F ? (a.F < b.F ? -1 : 1) : a.H != b.H ? (a.H < b.H ? -1 : 1) : a.Seq.CompareTo(b.Seq);
    }

    private const int StraightCost = 100;
    private const int DiagonalCost = 141;

    private static readonly (int Dx, int Dy)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>
    /// Waypoints from <paramref name="start"/> (excluded) to <paramref name="target"/> (last element),
    /// or null if the target is outside the map, impassable or unreachable.
    /// </summary>
    public static List<Vec2>? FindPath(GridMap map, Vec2 start, Vec2 target, int maxExpandedNodes = MaxExpandedNodes)
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
        if (_cost is null || _cost.Length < count)
        {
            _cost = new int[count];
            _parent = new int[count];
            _seen = new int[count];
            _closed = new int[count];
            _generation = 0;
        }
        if (++_generation == int.MaxValue)
        {
            Array.Clear(_seen!);
            Array.Clear(_closed!);
            _generation = 1;
        }
        int gen = _generation;
        var cost = _cost;
        var parent = _parent!;
        var seen = _seen!;
        var closed = _closed!;
        var open = _open ??= new PriorityQueue<int, (int F, int H, int Seq)>(PriorityOrder.Instance);
        open.Clear();

        int sequence = 0;
        int startIndex = startCell.Y * width + startCell.X;
        int goalIndex = goalCell.Y * width + goalCell.X;
        var cells = map.Cells;
        int height = map.Height;

        // On a big map, a target (or start) walled into a small pocket would make A* flood the whole map: find that out cheaply first.
        if (count > EnclosureCheckMinCells && maxExpandedNodes > EnclosureFloodLimit
            && (IsSmallPocketWithout(cells, width, height, goalIndex, startIndex) || IsSmallPocketWithout(cells, width, height, startIndex, goalIndex)))
            return null;

        cost[startIndex] = 0;
        parent[startIndex] = -1;
        seen[startIndex] = gen;
        int startH = Heuristic(startCell.X, startCell.Y, goalCell.X, goalCell.Y);
        open.Enqueue(startIndex, (startH, startH, sequence++));

        int expanded = 0;
        while (open.TryDequeue(out int current, out _))
        {
            if (closed[current] == gen)
                continue;
            if (current == goalIndex)
                return BuildWaypoints(parent, current, width, target);
            closed[current] = gen;
            if (++expanded > maxExpandedNodes)
                return null;

            int cx = current % width, cy = current / width;
            int currentCost = cost[current];
            for (int d = 0; d < Directions.Length; d++)
            {
                var (dx, dy) = Directions[d];
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;
                int nextIndex = ny * width + nx;
                ref readonly var next = ref cells[nextIndex];
                if (!next.IsPassable || closed[nextIndex] == gen)
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                // No corner cutting: both orthogonal neighbours of a diagonal step must be passable.
                if (diagonal && (!cells[cy * width + nx].IsPassable || !cells[ny * width + cx].IsPassable))
                    continue;

                int step = (diagonal ? DiagonalCost : StraightCost) * next.MoveCostPct / 100;
                int tentative = currentCost + step;
                if (seen[nextIndex] == gen && tentative >= cost[nextIndex])
                    continue;

                seen[nextIndex] = gen;
                cost[nextIndex] = tentative;
                parent[nextIndex] = current;
                int h = Heuristic(nx, ny, goalCell.X, goalCell.Y);
                open.Enqueue(nextIndex, (tentative + h, h, sequence++));
            }
        }
        return null;
    }

    /// <summary>
    /// Whether a path exists, answered from connected regions of the map (A*'s moves) instead of a search.
    /// The regions are cached per map and rebuilt when its passability changes.
    /// </summary>
    public static bool Reachable(GridMap map, Vec2 from, Vec2 to)
    {
        if (!map.Contains(from) || !map.Contains(to))
            return false;
        var a = from.ToCell();
        var b = to.ToCell();
        if (!map[b].IsPassable)
            return false;
        if (a == b)
            return true;
        var labels = Regions.For(map);
        int la = labels[a.Y * map.Width + a.X], lb = labels[b.Y * map.Width + b.X];
        return la != 0 && la == lb;
    }

    /// <summary>Region labels per map (0 = impassable), with a fingerprint of the passable cells to notice edits.</summary>
    private static class Regions
    {
        private sealed class Entry
        {
            public ulong Fingerprint;
            public int[] Labels = [];
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GridMap, Entry> Cache = new();

        public static int[] For(GridMap map)
        {
            var cells = map.Cells;
            ulong fingerprint = 14695981039346656037UL;
            for (int i = 0; i < cells.Length; i++)
                if (!cells[i].IsPassable)
                    fingerprint = (fingerprint ^ (ulong)i) * 1099511628211UL;
            lock (Cache)
            {
                var entry = Cache.GetOrCreateValue(map);
                if (entry.Labels.Length != cells.Length || entry.Fingerprint != fingerprint)
                {
                    entry.Labels = Label(cells, map.Width, map.Height);
                    entry.Fingerprint = fingerprint;
                }
                return entry.Labels;
            }
        }

        private static int[] Label(ReadOnlySpan<CellData> cells, int width, int height)
        {
            var labels = new int[cells.Length];
            var queue = new Queue<int>();
            int next = 0;
            for (int start = 0; start < cells.Length; start++)
            {
                if (labels[start] != 0 || !cells[start].IsPassable)
                    continue;
                labels[start] = ++next;
                queue.Enqueue(start);
                while (queue.TryDequeue(out int current))
                {
                    int cx = current % width, cy = current / width;
                    foreach (var (dx, dy) in Directions)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                            continue;
                        int n = ny * width + nx;
                        if (labels[n] != 0 || !cells[n].IsPassable)
                            continue;
                        if (dx != 0 && dy != 0 && (!cells[cy * width + nx].IsPassable || !cells[ny * width + cx].IsPassable))
                            continue;
                        labels[n] = next;
                        queue.Enqueue(n);
                    }
                }
            }
            return labels;
        }
    }

    private const int EnclosureCheckMinCells = 200_000;
    private const int EnclosureFloodLimit = 20_000;
    [ThreadStatic] private static int[]? _flood;
    [ThreadStatic] private static int _floodGeneration;
    [ThreadStatic] private static Queue<int>? _floodQueue;

    /// <summary>
    /// Flood-fills from <paramref name="from"/> with A*'s moves, at most <see cref="EnclosureFloodLimit"/> cells:
    /// true when the whole reachable pocket is smaller than that and does not contain <paramref name="other"/>.
    /// </summary>
    private static bool IsSmallPocketWithout(ReadOnlySpan<CellData> cells, int width, int height, int from, int other)
    {
        if (_flood is null || _flood.Length < cells.Length)
        {
            _flood = new int[cells.Length];
            _floodGeneration = 0;
        }
        if (++_floodGeneration == int.MaxValue)
        {
            Array.Clear(_flood);
            _floodGeneration = 1;
        }
        int gen = _floodGeneration;
        var flood = _flood;
        var queue = _floodQueue ??= new Queue<int>();
        queue.Clear();
        queue.Enqueue(from);
        flood[from] = gen;
        int visited = 0;
        while (queue.TryDequeue(out int current))
        {
            if (current == other || ++visited > EnclosureFloodLimit)
                return false;
            int cx = current % width, cy = current / width;
            foreach (var (dx, dy) in Directions)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;
                int nextIndex = ny * width + nx;
                if (flood[nextIndex] == gen || !cells[nextIndex].IsPassable)
                    continue;
                if (dx != 0 && dy != 0 && (!cells[cy * width + nx].IsPassable || !cells[ny * width + cx].IsPassable))
                    continue;
                flood[nextIndex] = gen;
                queue.Enqueue(nextIndex);
            }
        }
        return true;
    }

    /// <summary>Weighted octile distance (see <see cref="HeuristicWeightPct"/>).</summary>
    private static int Heuristic(int ax, int ay, int bx, int by)
    {
        int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
        int diagonal = Math.Min(dx, dy);
        return (diagonal * DiagonalCost + (Math.Max(dx, dy) - diagonal) * StraightCost) * HeuristicWeightPct / 100;
    }

    /// <summary>
    /// The octile distance is weighted up to about the cost of forest: far fewer cells searched on big wooded maps,
    /// for paths at most this much longer than the shortest (in practice a few percent).
    /// </summary>
    private const int HeuristicWeightPct = 125;

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
