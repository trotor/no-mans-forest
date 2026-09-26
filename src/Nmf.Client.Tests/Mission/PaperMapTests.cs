using Nmf.Client.Mission;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Mission;

public class PaperMapTests
{
    private static GridMap Map(int w, int h, Func<int, int, short> height, params string[] extraTerrains) =>
        Fill(new GridMap(w, h, ["none", "forest", "water", "swamp", "road", .. extraTerrains]), height);

    private static GridMap Fill(GridMap map, Func<int, int, short> height)
    {
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map[new CellCoord(x, y)].GroundHeightCm = height(x, y);
        return map;
    }

    private static (int R, int G, int B) At(PaperMapImage image, int px, int py)
    {
        int i = (py * image.Width + px) * 4;
        return (image.Rgba[i], image.Rgba[i + 1], image.Rgba[i + 2]);
    }

    private static bool IsContour((int R, int G, int B) c) => c.R - c.B > 45 && c.G < 200;

    /// <summary>Hummocks of about ±40 cm, as the map generator makes.</summary>
    private static short Hummock(int x, int y) => (short)(40 * Math.Sin(x * 0.9) * Math.Cos(y * 1.1));

    [Fact]
    public void Region_AndScale_GiveTheImageSize()
    {
        var map = Map(300, 200, (_, _) => 0);
        var image = PaperMap.Render(map, new MapRegion(50, 40, 100, 60), 2);
        Assert.Equal((200, 120), (image.Width, image.Height));
        Assert.Equal(new MapRegion(50, 40, 100, 60), image.Region);
    }

    [Fact]
    public void Slope_HasEvenlySpacedContours()
    {
        // Rising 20 cm per metre east: a 5 m contour every 25 m, 8 of them across 200 m.
        var map = Map(200, 60, (x, y) => (short)(x * 20 + Hummock(x, y)));
        var image = PaperMap.Render(map, new MapRegion(0, 0, 200, 60), 1, paperGrain: false);
        int lines = 0;
        bool inside = false;
        for (int px = 0; px < 200; px++)
        {
            bool c = IsContour(At(image, px, 30));
            if (c && !inside) lines++;
            inside = c;
        }
        Assert.InRange(lines, 7, 9);
    }

    [Fact]
    public void FlatGroundWithHummocks_HasNoContours()
    {
        var map = Map(120, 120, (x, y) => (short)(1200 + Hummock(x, y)));
        var image = PaperMap.Render(map, new MapRegion(0, 0, 120, 120), 1, paperGrain: false);
        int contour = 0;
        for (int py = 0; py < 120; py++)
            for (int px = 0; px < 120; px++)
                if (IsContour(At(image, px, py))) contour++;
        Assert.Equal(0, contour);
    }

    [Fact]
    public void Edges_AreSmooth_NotStairs()
    {
        var map = Map(100, 100, (_, _) => 0);
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
                if (Math.Abs(x - y) <= 1) map[new CellCoord(x, y)].TerrainId = 4; // a diagonal road
        var image = PaperMap.Render(map, new MapRegion(0, 0, 100, 100), 2, paperGrain: false);
        var shades = new HashSet<(int, int, int)>();
        for (int px = 80; px < 120; px++)
            shades.Add(At(image, px, 100));
        Assert.True(shades.Count >= 5, $"only {shades.Count} shades across the road edge");
    }

    [Fact]
    public void Water_Road_AndBogLines()
    {
        var map = Map(120, 120, (_, _) => 0);
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 120; x++)
            {
                ref var cell = ref map[new CellCoord(x, y)];
                if (x < 40) cell.TerrainId = 2;                 // lake
                else if (x < 80) cell.TerrainId = 3;            // bog
                else if (x is >= 98 and <= 102) cell.TerrainId = 4; // road
                else cell.TerrainId = 1;                        // forest
            }
        var image = PaperMap.Render(map, new MapRegion(0, 0, 120, 120), 2, paperGrain: false);
        var water = At(image, 30, 100);
        Assert.True(water.B > water.R + 40, $"water {water}");
        var road = At(image, 200, 100);
        Assert.True(road.R > road.B + 40, $"road {road}");
        var forest = At(image, 225, 100);
        Assert.True(forest.G >= forest.R && forest.G > forest.B, $"forest {forest}");
        bool blue = false;
        for (int py = 90; py < 110; py++)
            for (int px = 110; px < 130; px++)
                blue |= At(image, px, py) is var c && c.B > c.R + 50;
        Assert.True(blue, "no blue bog lines");
    }

    [Fact]
    public void MissionRegion_HoldsZonesAndRoutes_WithinTheMap()
    {
        var map = new GridMap(1000, 1000, ["none"]);
        var region = MissionMapFrame.Region(map, [new Vec2(20_000, 80_000), new Vec2(26_000, 83_000), new Vec2(47_000, 55_000)], 60);
        Assert.True(region.X <= 140 && region.Y <= 490 && region.X + region.Width >= 530 && region.Y + region.Height >= 890);
        Assert.Equal(region.Width, region.Height);
        var edge = MissionMapFrame.Region(map, [new Vec2(1_000, 1_000), new Vec2(5_000, 3_000)], 60);
        Assert.True(edge.X >= 0 && edge.Y >= 0);
    }

    [Fact]
    public void Hillshade_LitFromTheNorthWest()
    {
        // A cone: the north-west flank must come out brighter than the south-east one.
        var map = Map(120, 120, (x, y) => (short)Math.Max(0, 3000 - 60 * Math.Sqrt((x - 60) * (x - 60) + (y - 60) * (y - 60))));
        var image = PaperMap.Render(map, new MapRegion(0, 0, 120, 120), 1, paperGrain: false);
        int Brightness(int px, int py) { var c = At(image, px, py); return c.R + c.G + c.B; }
        Assert.True(Brightness(47, 47) > Brightness(73, 73), $"NW {Brightness(47, 47)} vs SE {Brightness(73, 73)}");
    }

    [Fact]
    public void MissionRegion_HasAMinimumSize()
    {
        var map = new GridMap(1000, 1000, ["none"]);
        var region = MissionMapFrame.Region(map, [new Vec2(50_000, 50_000)], 0);
        Assert.True(region.Width >= MissionMapFrame.MinSide && region.Height >= MissionMapFrame.MinSide);
    }
}
