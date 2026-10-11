using Nmf.Client.Art;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Art;

public class DecorationsTests
{
    private static readonly Dictionary<DecorationKind, int> Counts = Enum.GetValues<DecorationKind>().ToDictionary(k => k, _ => 3);

    private static GridMap ForestMap()
    {
        var map = new GridMap(30, 30, ["none", "grass", "forest"]);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 30; x++)
                map[new CellCoord(x, y)] = new CellData(0, x < 15 ? (short)1500 : (short)0, 8, 26, x < 15 ? (ushort)2 : (ushort)1);
        map[new CellCoord(20, 20)] = new CellData(0, 120, 255, 230, 1, CellData.Impassable);
        map[new CellCoord(22, 20)] = new CellData(0, 80, 153, 0, 1, 50);
        return map;
    }

    [Fact]
    public void Place_PutsTreesOnlyInForest_AboutOnePerTwoByTwoBlock()
    {
        var trees = Decorations.Place(ForestMap(), Counts).Where(d => Decorations.IsTree(d.Kind)).ToList();
        Assert.All(trees, t => Assert.True(t.PositionCm.X < 1500 + 40));
        Assert.InRange(trees.Count, 70, 170); // about one tree per 2 x 2 block: glades and sparse heath fewer, young thickets more
        Assert.Contains(trees, t => t.Kind == DecorationKind.Spruce);
        Assert.Contains(trees, t => t.Kind == DecorationKind.Birch);
    }

    [Fact]
    public void Place_PutsRockAndBushOnObstacleCells()
    {
        var all = Decorations.Place(ForestMap(), Counts);
        var rock = Assert.Single(all, d => d.Kind == DecorationKind.Rock);
        var bush = Assert.Single(all, d => d.Kind == DecorationKind.Bush);
        Assert.Equal(new CellCoord(20, 20), rock.PositionCm.ToCell());
        Assert.Equal(new CellCoord(22, 20), bush.PositionCm.ToCell());
    }

    [Fact]
    public void Place_IsDeterministicSortedAndVariantsInRange()
    {
        var a = Decorations.Place(ForestMap(), Counts);
        var b = Decorations.Place(ForestMap(), Counts);
        Assert.Equal(a, b);
        Assert.Equal(a.OrderBy(d => d.PositionCm.Y).ThenBy(d => d.PositionCm.X), a);
        Assert.All(a, d => Assert.InRange(d.Variant, 0, Counts[d.Kind] - 1));
    }

    private static GridMap BigForest(Func<int, int, short>? ground = null, Func<int, int, ushort>? terrain = null)
    {
        var map = new GridMap(120, 120, ["none", "grass", "forest", "swamp"]);
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 120; x++)
            {
                ushort id = terrain?.Invoke(x, y) ?? 2;
                map[new CellCoord(x, y)] = new CellData(ground?.Invoke(x, y) ?? 0, id == 2 ? (short)1500 : (short)0, 8, 26, id);
            }
        return map;
    }

    [Fact]
    public void Pines_GrowOnTheHighGround_BirchesByTheOpenings()
    {
        var hill = Decorations.Place(BigForest(ground: (x, _) => (short)(x < 60 ? 3000 : 0)), Counts);
        int pinesHigh = hill.Count(d => d.Kind == DecorationKind.Pine && d.PositionCm.X < 6000);
        int pinesLow = hill.Count(d => d.Kind == DecorationKind.Pine && d.PositionCm.X >= 6000);
        Assert.True(pinesHigh > pinesLow * 2, $"high {pinesHigh}, low {pinesLow}");

        var bog = Decorations.Place(BigForest(terrain: (x, _) => (ushort)(x < 60 ? 2 : 3)), Counts);
        var trees = bog.Where(d => Decorations.IsTree(d.Kind)).ToList();
        double BirchShare(IEnumerable<Decoration> t) => t.Count(d => d.Kind == DecorationKind.Birch) / (double)Math.Max(1, t.Count());
        double edge = BirchShare(trees.Where(d => d.PositionCm.X >= 5700));
        double deep = BirchShare(trees.Where(d => d.PositionCm.X < 3000));
        Assert.True(edge > deep + 0.2, $"birches by the bog {edge:P0}, deep in the forest {deep:P0}");
    }

    [Fact]
    public void NoTwoTreesAlike_SizeMirrorAndShadeVary()
    {
        var trees = Decorations.Place(BigForest(), Counts).Where(d => Decorations.IsTree(d.Kind)).ToList();
        Assert.All(trees, t => Assert.InRange(t.ScalePct, 45, 150));
        Assert.All(trees, t => Assert.InRange(t.Shade, -10, 10));
        Assert.All(trees, t => Assert.InRange(t.Hue, -12, 12));
        Assert.True(trees.Select(t => t.Hue).Distinct().Count() >= 6);
        Assert.Contains(trees, t => t.Flip);
        Assert.Contains(trees, t => !t.Flip);
        Assert.True(trees.Select(t => t.Shade).Distinct().Count() >= 8);
        Assert.True(trees.Select(t => t.ScalePct).Distinct().Count() >= 20);
    }

    [Fact]
    public void TheGround_HasItsOwnDetails_ByTerrain()
    {
        var map = BigForest(terrain: (x, _) => (ushort)(x < 40 ? 2 : x < 80 ? 1 : 3));
        var all = Decorations.Place(map, Counts);
        Assert.Contains(all, d => d.Kind is DecorationKind.Fern or DecorationKind.Moss && d.PositionCm.X < 4000);
        Assert.Contains(all, d => d.Kind == DecorationKind.Tuft && d.PositionCm.X is >= 4000 and < 8000);
        Assert.Contains(all, d => d.Kind == DecorationKind.Flowers && d.PositionCm.X is >= 4000 and < 8000);
        Assert.Contains(all, d => d.Kind == DecorationKind.Sedge && d.PositionCm.X >= 8000);
        Assert.DoesNotContain(all, d => d.Kind == DecorationKind.Tuft && d.PositionCm.X < 4000);
        int details = all.Count(d => !Decorations.IsTree(d.Kind));
        Assert.InRange(details, 800, 3000); // a sprinkling, not a carpet: 14 400 cells
    }

    [Fact]
    public void AFallenTree_LiesAlongItsLine()
    {
        var map = new GridMap(30, 30, ["none", "forest"], new MapFeatures([], [],
            [new MapPath("log_1", "log", [new CellCoord(10, 10).CenterCm, new CellCoord(14, 10).CenterCm])]));
        var log = Assert.Single(Decorations.Place(map, Counts), d => d.Kind == DecorationKind.Log);
        Assert.Equal(new CellCoord(12, 10).CenterCm, log.PositionCm);
        Assert.Equal(0, log.AngleDeg, 1);
        Assert.InRange(log.LengthPct, 163, 170); // 4 m between the end cells' centres, 5 m of trunk, a 3 m sprite
    }

    [Fact]
    public void Winter_NoGroundPlants_TreesAndRocksStay()
    {
        var summer = Decorations.Place(ForestMap(), Counts);
        var winter = Decorations.Place(ForestMap(), Counts, winter: true);
        Assert.Contains(summer, d => d.Kind is DecorationKind.Fern or DecorationKind.Tuft or DecorationKind.Flowers or DecorationKind.Moss);
        Assert.DoesNotContain(winter, d => d.Kind is DecorationKind.Fern or DecorationKind.Tuft or DecorationKind.Flowers or DecorationKind.Moss
            or DecorationKind.Sedge or DecorationKind.Cotton or DecorationKind.Pool or DecorationKind.Puddle);
        Assert.Equal(summer.Count(d => Decorations.IsTree(d.Kind) || d.Kind is DecorationKind.Rock or DecorationKind.Bush),
            winter.Count(d => Decorations.IsTree(d.Kind) || d.Kind is DecorationKind.Rock or DecorationKind.Bush));
    }

    /// <summary>The trees of one window of the forest (cells).</summary>
    private static List<Decoration> Window(IEnumerable<Decoration> trees, int x0, int y0, int size) =>
        trees.Where(t => t.PositionCm.X / 100 >= x0 && t.PositionCm.X / 100 < x0 + size && t.PositionCm.Y / 100 >= y0 && t.PositionCm.Y / 100 < y0 + size).ToList();

    [Fact]
    public void TheForest_GrowsInStands_NotEvenlyMixed()
    {
        // On flat even ground the kinds still keep together: some stands nearly all spruce, others with much pine or birch.
        var trees = Decorations.Place(BigForest(), Counts).Where(d => Decorations.IsTree(d.Kind)).ToList();
        var shares = new List<double>();
        for (int y = 0; y < 120; y += 20)
            for (int x = 0; x < 120; x += 20)
            {
                var w = Window(trees, x, y, 20);
                shares.Add(w.Count(t => t.Kind == DecorationKind.Spruce) / (double)Math.Max(1, w.Count));
            }
        Assert.True(shares.Max() - shares.Min() > 0.4, $"spruce share by stand {shares.Min():P0}..{shares.Max():P0}");
    }

    [Fact]
    public void TheForest_HasGlades_YoungThickets_AndOldGiants()
    {
        var trees = Decorations.Place(BigForest(), Counts).Where(d => Decorations.IsTree(d.Kind)).ToList();
        int blocks = 60 * 60;
        var withTree = trees.Select(t => (t.PositionCm.X / 200, t.PositionCm.Y / 200)).ToHashSet();
        Assert.True(blocks - withTree.Count > blocks * 3 / 100, $"{blocks - withTree.Count} blocks of {blocks} without a tree");
        Assert.True(trees.Count(t => t.ScalePct < 70) > trees.Count / 20, "young trees");
        Assert.Contains(trees, t => t.ScalePct > 130);
        // Neighbours in a stand share its tone: within a 6 m window the hue varies less than across the forest.
        int Spread(List<Decoration> w) => w.Count == 0 ? 0 : w.Max(t => t.Hue) - w.Min(t => t.Hue);
        Assert.True(Spread(Window(trees, 30, 30, 6)) < Spread(trees));
    }

    [Fact]
    public void DeadSnags_AndJunipers_Here_AndThere()
    {
        var all = Decorations.Place(BigForest(ground: (x, _) => (short)(x < 60 ? 3000 : 0)), Counts);
        int trees = all.Count(d => Decorations.IsTree(d.Kind));
        int snags = all.Count(d => d.Kind == DecorationKind.Snag);
        Assert.InRange(snags, trees / 200 + 1, trees / 25);
        Assert.True(Decorations.IsTree(DecorationKind.Snag));
        Assert.Contains(all, d => d.Kind == DecorationKind.Juniper);
        Assert.False(Decorations.IsTree(DecorationKind.Juniper));
        var winter = Decorations.Place(BigForest(ground: (x, _) => (short)(x < 60 ? 3000 : 0)), Counts, winter: true);
        Assert.Equal(all.Count(d => d.Kind == DecorationKind.Juniper), winter.Count(d => d.Kind == DecorationKind.Juniper)); // evergreen, above the snow
    }
}
