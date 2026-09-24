using Nmf.Content.Tiled;
using Nmf.Sim.Core;

namespace Nmf.Content.Tests;

public class TmxObjectTests
{
    private static readonly string ValidPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.tmx");

    private static MapLoadException LoadFails(string objectsXml)
    {
        using var dir = new TempMapDir();
        var xml = TmxText.Map(4, 3, "1,1,1,1,1,1,1,1,1,1,1,1", $"<objectgroup id=\"4\" name=\"ai\">{objectsXml}</objectgroup>");
        return Assert.Throws<MapLoadException>(() => TmxMapLoader.Load(dir.WriteMap(xml)));
    }

    [Fact]
    public void Load_ValidMap_ReadsZone()
    {
        var zone = Assert.Single(TmxMapLoader.Load(ValidPath).Features.Zones);
        Assert.Equal("start_zone", zone.Name);
        Assert.Equal("zone", zone.Type);
        Assert.Equal(new Vec2(0, 200), zone.Min);
        Assert.Equal(new Vec2(200, 300), zone.Max);
    }

    [Fact]
    public void Load_ValidMap_ReadsPoint()
    {
        var point = Assert.Single(TmxMapLoader.Load(ValidPath).Features.Points);
        Assert.Equal("spawn_a", point.Name);
        Assert.Equal("spawn", point.Type);
        Assert.Equal(new Vec2(50, 250), point.Position);
    }

    [Fact]
    public void Load_ValidMap_ReadsPathWithClassAttributeAsType()
    {
        var path = Assert.Single(TmxMapLoader.Load(ValidPath).Features.Paths);
        Assert.Equal("path_north", path.Name);
        Assert.Equal("patrol", path.Type);
        Assert.Equal(new[] { new Vec2(100, 50), new Vec2(300, 50), new Vec2(300, 100) }, path.Points);
    }

    [Fact]
    public void Load_UnnamedObject_Throws()
    {
        var ex = LoadFails("<object id=\"1\" x=\"0\" y=\"0\"><point/></object>");
        Assert.Contains("no name", ex.Message);
    }

    [Fact]
    public void Load_DuplicateNames_Throws()
    {
        var ex = LoadFails("""
            <object id="1" name="a" x="0" y="0"><point/></object>
            <object id="2" name="a" x="8" y="8"><point/></object>
            """);
        Assert.Contains("duplicate object name 'a'", ex.Message);
    }

    [Fact]
    public void Load_PointOutsideMap_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"far\" x=\"640\" y=\"0\"><point/></object>");
        Assert.Contains("outside the map", ex.Message);
    }

    [Fact]
    public void Load_ZoneOutsideMap_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"big\" x=\"32\" y=\"0\" width=\"64\" height=\"16\"/>");
        Assert.Contains("zone 'big' lies outside the map", ex.Message);
    }

    [Fact]
    public void Load_ZeroSizeZone_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"flat\" x=\"0\" y=\"0\"/>");
        Assert.Contains("zero size", ex.Message);
    }

    [Fact]
    public void Load_TileObject_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"tree\" gid=\"1\" x=\"0\" y=\"16\" width=\"16\" height=\"16\"/>");
        Assert.Contains("tile object", ex.Message);
    }

    [Fact]
    public void Load_Polygon_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"poly\" x=\"0\" y=\"0\"><polygon points=\"0,0 8,0 8,8\"/></object>");
        Assert.Contains("only rectangles, points and polylines", ex.Message);
    }

    [Fact]
    public void Load_SinglePointPath_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"stub\" x=\"0\" y=\"0\"><polyline points=\"0,0\"/></object>");
        Assert.Contains("at least two points", ex.Message);
    }
}
