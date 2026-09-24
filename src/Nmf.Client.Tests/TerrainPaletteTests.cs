using Nmf.Client;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class TerrainPaletteTests
{
    [Fact]
    public void ColorFor_UnknownTerrain_IsMagenta()
    {
        Assert.Equal(new Rgb(255, 0, 255), TerrainPalette.ColorFor("lava"));
        Assert.NotEqual(TerrainPalette.ColorFor("grass"), TerrainPalette.ColorFor("forest"));
    }

    [Fact]
    public void CellColor_HigherGround_IsLighter()
    {
        var low = new GridMap(3, 3, ["none", "grass"]);
        var high = new GridMap(3, 3, ["none", "grass"]);
        low[new CellCoord(1, 1)] = new CellData(0, 0, 0, 0, 1);
        high[new CellCoord(1, 1)] = new CellData(300, 0, 0, 0, 1);
        var a = TerrainPalette.CellColor(low, new CellCoord(1, 1));
        var b = TerrainPalette.CellColor(high, new CellCoord(1, 1));
        Assert.True(b.G > a.G);
    }

    [Fact]
    public void CellColor_IsDeterministic_AndRockIsGrey()
    {
        var map = new GridMap(3, 3, ["none", "grass"]);
        map[new CellCoord(2, 2)] = new CellData(0, 120, 255, 230, 1, CellData.Impassable);
        var rock = TerrainPalette.CellColor(map, new CellCoord(2, 2));
        Assert.Equal(rock, TerrainPalette.CellColor(map, new CellCoord(2, 2)));
        Assert.True(Math.Abs(rock.R - rock.G) < 8 && Math.Abs(rock.G - rock.B) < 8);
    }
}
