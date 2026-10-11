using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Art;

public enum DecorationKind
{
    Spruce,
    Birch,
    Rock,
    Bush,
    Pine,
    Stump,
    Log,
    Fern,
    Moss,
    Tuft,
    Flowers,
    Sedge,
    Cotton,
    Pool,
    Puddle,
    /// <summary>A dead standing tree, grey and bare (keloa).</summary>
    Snag,
    /// <summary>A juniper on the dry heath, dark and low.</summary>
    Juniper,
}

/// <summary>
/// One thing drawn on the ground. <paramref name="ScalePct"/>, <paramref name="Flip"/> and <paramref name="Shade"/> make
/// no two trees alike, <paramref name="Hue"/> gives a stand its tone (− bluer, + yellower); <paramref name="AngleDeg"/>
/// and <paramref name="LengthPct"/> lay a fallen tree along its line.
/// </summary>
public readonly record struct Decoration(DecorationKind Kind, int Variant, Vec2 PositionCm, int ScalePct = 100, bool Flip = false,
    int Shade = 0, float AngleDeg = 0, int LengthPct = 100, int Hue = 0);

/// <summary>
/// Deterministic placement of what is drawn on the map (presentation only; spec 2026-09-26-rich-terrain-design): trees
/// one per 2 × 2 m of forest — pines on the high ground, birches by the bogs and openings, spruce elsewhere — each of its
/// own size, facing and shade; rocks and bushes on their cells; fallen trees along their lines; and a sprinkling of
/// detail on the ground in patches: ferns and moss in the forest, grass tufts and flowers in the meadows, sedge, cotton
/// grass and pools on the bogs, puddles on the tracks.
/// </summary>
public static class Decorations
{
    private const int TreeBlockCells = 2;
    private const int BushMaxHeightCm = 150;
    /// <summary>Lower than this is a fallen tree or the like, drawn from its own line, not as a bush.</summary>
    private const int BushMinHeightCm = 60;
    private const int OpeningRadiusCells = 2;
    private const int LogSpriteLengthCm = 300;
    private const int SimCellCm = 100;

    public static bool IsTree(DecorationKind kind) => kind is DecorationKind.Spruce or DecorationKind.Birch or DecorationKind.Pine or DecorationKind.Snag;

    /// <param name="winter">Under snow (spec 2026-10-10-winter-design §5): no ground plants, pools or puddles; stumps, rocks and trees stay.</param>
    public static IReadOnlyList<Decoration> Place(GridMap map, IReadOnlyDictionary<DecorationKind, int> variantCounts, bool winter = false)
    {
        var result = new List<Decoration>();
        int forestId = IndexOf(map.TerrainNames, "forest");
        int grassId = IndexOf(map.TerrainNames, "grass");
        int swampId = IndexOf(map.TerrainNames, "swamp");
        int roadId = IndexOf(map.TerrainNames, "road");
        var (lowest, highest) = HeightRange(map);

        for (int by = 0; by * TreeBlockCells < map.Height; by++)
        {
            for (int bx = 0; bx * TreeBlockCells < map.Width; bx++)
            {
                uint h = Hash(bx, by, 1);
                var cell = new CellCoord(bx * TreeBlockCells + (int)(h % TreeBlockCells), by * TreeBlockCells + (int)(h / 7 % TreeBlockCells));
                if (!GrowsHere(map, cell, forestId))
                    continue; // no tree grows out of a fallen trunk
                var stand = StandAt(map, cell, lowest, highest);
                uint look = Hash(bx, by, 7);
                if (stand.Glade || (stand.Heath && look % 100 < SparseHeathPct))
                    continue; // a small glade; the dry heath grows sparse
                var kind = TreeKind(map, cell, h, forestId, stand);
                if (!stand.Young && Hash(bx, by, 8) % 1000 < SnagPerMille)
                    kind = DecorationKind.Snag; // dead and grey, still standing
                result.Add(Tree(kind, cell, h, look, stand, variantCounts, young: stand.Young));
                // A young thicket: a sapling or two between the trees.
                uint s = Hash(bx, by, 9);
                if (stand.Young && s % 100 < SaplingPct)
                {
                    var sapling = new CellCoord(bx * TreeBlockCells + (int)(s / 100 % TreeBlockCells), by * TreeBlockCells + (int)(s / 300 % TreeBlockCells));
                    if (sapling != cell && GrowsHere(map, sapling, forestId))
                        result.Add(Tree(TreeKind(map, sapling, s, forestId, stand), sapling, s, Hash(bx, by, 10), stand, variantCounts, young: true, sapling: true));
                }
            }
        }

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var data = map[new CellCoord(x, y)];
                var centre = new CellCoord(x, y).CenterCm;
                if (data.ObstacleHeightCm > 0 && !(data.TerrainId == forestId && data.IsPassable))
                {
                    uint o = Hash(x, y, 2);
                    var jitter = new Vec2((int)(o % 13) - 6, (int)(o / 13 % 13) - 6);
                    if (!data.IsPassable)
                        result.Add(new Decoration(DecorationKind.Rock, Variant(o, DecorationKind.Rock, variantCounts), centre + jitter));
                    else if (data.ObstacleHeightCm is <= BushMaxHeightCm and >= BushMinHeightCm)
                        result.Add(new Decoration(DecorationKind.Bush, Variant(o, DecorationKind.Bush, variantCounts), centre + jitter));
                    continue;
                }
                if (!data.IsPassable || data.LowCoverHeightCm > 0)
                    continue; // nothing grows through a boulder or a fallen trunk
                if (GroundDetail(x, y, data.TerrainId, forestId, grassId, swampId, roadId) is { } detail
                    && (!winter || detail is DecorationKind.Stump or DecorationKind.Juniper))
                {
                    uint d = Hash(x, y, 3);
                    var jitter = new Vec2((int)(d % 61) - 30, (int)(d / 61 % 61) - 30);
                    result.Add(new Decoration(detail, Variant(d, detail, variantCounts), centre + jitter,
                        ScalePct: 85 + (int)(d / 3721 % 31), Flip: (d >> 20 & 1) == 1));
                }
            }
        }

        foreach (var path in map.Features.Paths)
        {
            if (path.Type != "log" || path.Points.Count < 2)
                continue;
            var (a, b) = (path.Points[0], path.Points[^1]);
            var along = b - a;
            if (along.LengthSquared == 0)
                continue;
            uint l = Hash(a.X, a.Y, 4);
            result.Add(new Decoration(DecorationKind.Log, Variant(l, DecorationKind.Log, variantCounts),
                new Vec2((a.X + b.X) / 2, (a.Y + b.Y) / 2),
                AngleDeg: (float)(Math.Atan2(along.Y, along.X) * 180 / Math.PI),
                LengthPct: (int)((along.Length + SimCellCm) * 100 / LogSpriteLengthCm))); // the end cells are trunk too
        }

        return result.OrderBy(d => d.PositionCm.Y).ThenBy(d => d.PositionCm.X).ToList();
    }

    private const int SparseHeathPct = 30, SaplingPct = 45, SnagPerMille = 15;

    /// <summary>
    /// What kind of stand a cell is in (patches of noise a few dozen metres across): dark spruce, a dry pine heath (more
    /// so up high), a birch grove, or mixed; young (small trees, saplings between) or old (big ones); its own tone; and
    /// the odd small glade.
    /// </summary>
    private readonly record struct Stand(int High, bool Spruce, bool Heath, bool Grove, bool Young, bool Old, int Tone, bool Glade);

    private static Stand StandAt(GridMap map, CellCoord cell, int lowest, int highest)
    {
        int high = highest > lowest ? (map[cell].GroundHeightCm - lowest) * 100 / (highest - lowest) : 0;
        int kind = Patch(cell.X, cell.Y, 24, 21);
        int age = Patch(cell.X, cell.Y, 18, 22);
        return new Stand(
            high,
            Spruce: kind < 300,
            Heath: kind > 700 - high * 2,
            Grove: Patch(cell.X, cell.Y, 15, 24) > 760,
            Young: age < 330,
            Old: age > 700,
            Tone: (Patch(cell.X, cell.Y, 16, 25) - 500) * 9 / 500,
            Glade: Patch(cell.X, cell.Y, 7, 23) > 830);
    }

    private static bool GrowsHere(GridMap map, CellCoord cell, int forestId) =>
        map.InBounds(cell) && map[cell] is var data && data.TerrainId == forestId && data.IsPassable && data.LowCoverHeightCm == 0;

    /// <summary>One tree: its size by the stand's age (a sapling smaller still), its mirror, its shade and the stand's tone.</summary>
    private static Decoration Tree(DecorationKind kind, CellCoord cell, uint h, uint look, Stand stand, IReadOnlyDictionary<DecorationKind, int> variantCounts,
        bool young, bool sapling = false)
    {
        var jitter = new Vec2((int)(h / 1009 % 81) - 40, (int)(h / 83 % 81) - 40);
        int scale = sapling ? 45 + (int)(look % 21)
            : young ? 55 + (int)(look % 31)
            : stand.Old ? 100 + (int)(look % 51)
            : 75 + (int)(look % 51);
        int kindTone = kind switch { DecorationKind.Birch => 3, DecorationKind.Pine => 2, DecorationKind.Spruce => -2, _ => 0 };
        int hue = Math.Clamp(stand.Tone + kindTone + (int)((look >> 16) % 5) - 2, -12, 12);
        int shade = Math.Clamp((int)((look >> 10) % 15) - 7 + (young ? 2 : 0) - (stand.Old ? 2 : 0), -10, 10);
        return new Decoration(kind, Variant(h, kind, variantCounts), cell.CenterCm + jitter, ScalePct: scale, Flip: (look >> 8 & 1) == 1,
            Shade: shade, Hue: hue);
    }

    /// <summary>
    /// Pines take to the high dry ground and the heaths, birches to the edges of bogs and openings and to their groves,
    /// spruce holds the rest and its own dark stands.
    /// </summary>
    private static DecorationKind TreeKind(GridMap map, CellCoord cell, uint hash, int forestId, Stand stand)
    {
        int roll = (int)(hash / 101 % 100);
        if (NearOpening(map, cell, forestId))
            return roll < 55 ? DecorationKind.Birch : roll < 70 ? DecorationKind.Pine : DecorationKind.Spruce;
        if (stand.Grove)
            return roll < 65 ? DecorationKind.Birch : roll < 75 ? DecorationKind.Pine : DecorationKind.Spruce;
        int pines = 8 + stand.High / 2; // 8 % in the hollows, 58 % on the highest ground
        int birches = 15;
        if (stand.Heath)
            (pines, birches) = (Math.Min(85, pines + 45), 8);
        else if (stand.Spruce)
            (pines, birches) = (pines / 3, 5);
        return roll < pines ? DecorationKind.Pine : roll < pines + birches ? DecorationKind.Birch : DecorationKind.Spruce;
    }

    private static bool NearOpening(GridMap map, CellCoord cell, int forestId)
    {
        for (int dy = -OpeningRadiusCells; dy <= OpeningRadiusCells; dy++)
            for (int dx = -OpeningRadiusCells; dx <= OpeningRadiusCells; dx++)
            {
                var n = new CellCoord(cell.X + dx, cell.Y + dy);
                if (map.InBounds(n) && map[n].TerrainId != forestId)
                    return true;
            }
        return false;
    }

    /// <summary>What grows on the ground of a cell, if anything: in patches, so ferns crowd a hollow and flowers a meadow.</summary>
    private static DecorationKind? GroundDetail(int x, int y, int terrain, int forestId, int grassId, int swampId, int roadId)
    {
        int roll = (int)(Hash(x, y, 5) % 1000);
        if (terrain == forestId)
        {
            if (roll < 4)
                return DecorationKind.Stump;
            if (roll < 12 && Patch(x, y, 24, 21) > 650)
                return DecorationKind.Juniper; // on the dry heath
            if (Patch(x, y, 9, 11) > 620 && roll < 150)
                return DecorationKind.Fern;
            if (Patch(x, y, 7, 12) > 600 && roll < 120)
                return DecorationKind.Moss;
            return null;
        }
        if (terrain == grassId)
        {
            if (Patch(x, y, 11, 13) > 600 && roll < 160)
                return DecorationKind.Flowers;
            return roll < 90 ? DecorationKind.Tuft : null;
        }
        if (terrain == swampId)
        {
            if (roll < 8)
                return DecorationKind.Pool;
            if (Patch(x, y, 10, 14) > 620 && roll < 110)
                return DecorationKind.Cotton;
            return roll < 120 ? DecorationKind.Sedge : null;
        }
        if (terrain == roadId)
            return roll < 12 ? DecorationKind.Puddle : null;
        return null;
    }

    /// <summary>Smooth value noise 0..999 over cells, <paramref name="scale"/> cells across one patch.</summary>
    private static int Patch(int x, int y, int scale, int salt)
    {
        int gx = x / scale, gy = y / scale;
        int fx = x % scale * 1000 / scale, fy = y % scale * 1000 / scale;
        int Corner(int cx, int cy) => (int)(Hash(cx, cy, salt) % 1000);
        int top = Corner(gx, gy) * (1000 - fx) / 1000 + Corner(gx + 1, gy) * fx / 1000;
        int bottom = Corner(gx, gy + 1) * (1000 - fx) / 1000 + Corner(gx + 1, gy + 1) * fx / 1000;
        return top * (1000 - fy) / 1000 + bottom * fy / 1000;
    }

    private static (int Lowest, int Highest) HeightRange(GridMap map)
    {
        int lowest = int.MaxValue, highest = int.MinValue;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                int h = map[new CellCoord(x, y)].GroundHeightCm;
                lowest = Math.Min(lowest, h);
                highest = Math.Max(highest, h);
            }
        return (lowest, highest);
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
