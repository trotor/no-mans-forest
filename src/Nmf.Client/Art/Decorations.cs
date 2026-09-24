using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Art;

public enum DecorationKind
{
    Spruce,
    Birch,
    Rock,
    Bush,
}

public readonly record struct Decoration(DecorationKind Kind, int Variant, Vec2 PositionCm);

/// <summary>Deterministic placement of trees (one per 2 x 2 m of forest, so crowns overlap), rocks and bushes (presentation only).</summary>
public static class Decorations
{
    private const int TreeBlockCells = 2;
    private const int BushMaxHeightCm = 150;

    public static IReadOnlyList<Decoration> Place(GridMap map, IReadOnlyDictionary<DecorationKind, int> variantCounts)
    {
        var result = new List<Decoration>();
        int forestId = IndexOf(map.TerrainNames, "forest");

        for (int by = 0; by * TreeBlockCells < map.Height; by++)
        {
            for (int bx = 0; bx * TreeBlockCells < map.Width; bx++)
            {
                uint h = Hash(bx, by, 1);
                var cell = new CellCoord(bx * TreeBlockCells + (int)(h % TreeBlockCells), by * TreeBlockCells + (int)(h / 7 % TreeBlockCells));
                if (!map.InBounds(cell))
                    continue;
                var data = map[cell];
                if (data.TerrainId != forestId || !data.IsPassable)
                    continue;
                var kind = h / 101 % 10 < 7 ? DecorationKind.Spruce : DecorationKind.Birch;
                var jitter = new Vec2((int)(h / 1009 % 81) - 40, (int)(h / 83 % 81) - 40);
                result.Add(new Decoration(kind, Variant(h, kind, variantCounts), cell.CenterCm + jitter));
            }
        }

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var data = map[new CellCoord(x, y)];
                if (data.ObstacleHeightCm <= 0 || data.TerrainId == forestId && data.IsPassable)
                    continue;
                uint h = Hash(x, y, 2);
                var jitter = new Vec2((int)(h % 13) - 6, (int)(h / 13 % 13) - 6);
                if (!data.IsPassable)
                    result.Add(new Decoration(DecorationKind.Rock, Variant(h, DecorationKind.Rock, variantCounts), new CellCoord(x, y).CenterCm + jitter));
                else if (data.ObstacleHeightCm <= BushMaxHeightCm)
                    result.Add(new Decoration(DecorationKind.Bush, Variant(h, DecorationKind.Bush, variantCounts), new CellCoord(x, y).CenterCm + jitter));
            }
        }

        return result.OrderBy(d => d.PositionCm.Y).ThenBy(d => d.PositionCm.X).ToList();
    }

    private static int Variant(uint hash, DecorationKind kind, IReadOnlyDictionary<DecorationKind, int> counts) =>
        (int)(hash / 31 % (uint)Math.Max(1, counts.TryGetValue(kind, out var c) ? c : 1));

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
            if (names[i] == name)
                return i;
        return -1;
    }

    private static uint Hash(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)salt * 83492791u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return h;
        }
    }
}
