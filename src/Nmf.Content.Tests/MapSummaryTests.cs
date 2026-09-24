using System.Globalization;
using Nmf.Content.Tiled;

namespace Nmf.Content.Tests;

public class MapSummaryTests
{
    private static readonly string ValidPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.tmx");

    private const string Expected =
        "Size: 4 x 3 cells (4 m x 3 m)\n" +
        "Terrain:\n" +
        "  grass: 5 (41.7 %)\n" +
        "  forest: 4 (33.3 %)\n" +
        "  swamp: 3 (25.0 %)\n" +
        "Ground height: 0 .. 300 cm\n" +
        "Zones (1): start_zone\n" +
        "Points (1): spawn_a\n" +
        "Paths (1): path_north\n";

    [Fact]
    public void Describe_ValidMap()
    {
        Assert.Equal(Expected, MapSummary.Describe(TmxMapLoader.Load(ValidPath)));
    }

    [Fact]
    public void Describe_UnderFinnishCulture_IsIdentical()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            Assert.Equal(Expected, MapSummary.Describe(TmxMapLoader.Load(ValidPath)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Describe_MapWithoutObjects_ShowsDashes()
    {
        using var dir = new TempMapDir();
        var map = TmxMapLoader.Load(dir.WriteMap(TmxText.Map(2, 1, "1,1")));
        var text = MapSummary.Describe(map);
        Assert.Contains("Zones (0): -\n", text);
        Assert.Contains("Paths (0): -\n", text);
    }
}
