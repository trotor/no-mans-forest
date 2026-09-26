using System.Globalization;
using Nmf.Content.Tiled;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Content.Tests;

public class TmxMapLoaderTests
{
    private static readonly string ValidPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.tmx");

    private static MapLoadException LoadFails(string xml)
    {
        using var dir = new TempMapDir();
        return Assert.Throws<MapLoadException>(() => TmxMapLoader.Load(dir.WriteMap(xml)));
    }

    private static GridMap LoadText(string xml)
    {
        using var dir = new TempMapDir();
        return TmxMapLoader.Load(dir.WriteMap(xml));
    }

    [Fact]
    public void Load_ValidMap_ReadsSizeAndTerrainNames()
    {
        var map = TmxMapLoader.Load(ValidPath);
        Assert.Equal(4, map.Width);
        Assert.Equal(3, map.Height);
        Assert.Equal(new[] { "none", "grass", "forest", "swamp" }, map.TerrainNames);
    }

    [Fact]
    public void Load_ValidMap_CombinesTerrainHeightAndObstacles()
    {
        var map = TmxMapLoader.Load(ValidPath);

        // grass, height 100
        Assert.Equal(new CellData(100, 0, 13, 0, 1), map[new CellCoord(1, 0)]);
        // forest, height 200
        Assert.Equal(new CellData(200, 1500, 102, 26, 2), map[new CellCoord(2, 0)]);
        // forest + rock: the rock is lower than the trees, so it is low cover up to its own height (the forest's cover above);
        // concealment takes the max; height 300
        var rockInForest = map[new CellCoord(3, 0)];
        Assert.Equal(new CellData(300, 1500, 255, 26, 2) with { LowCover = 230, LowCoverHeightCm = rockInForest.LowCoverHeightCm }, rockInForest);
        Assert.True(rockInForest.LowCoverHeightCm is > 0 and < 1500);
        // grass + bush
        Assert.Equal(new CellData(0, 80, 153, 0, 1), map[new CellCoord(1, 1)]);
        // swamp, empty height cell
        Assert.Equal(new CellData(0, 0, 5, 0, 3), map[new CellCoord(0, 2)]);
    }

    [Fact]
    public void Load_UnderFinnishCulture_ParsesNumbersInvariantly()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            var map = TmxMapLoader.Load(ValidPath);
            Assert.Equal(102, map[new CellCoord(2, 0)].ConcealmentPerM);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Load_FlippedTile_IsTreatedAsBaseTile()
    {
        // 2147483649 = gid 1 with the horizontal-flip flag (bit 31) set.
        var map = LoadText(TmxText.Map(2, 1, "2147483649,2"));
        Assert.Equal("grass", map.TerrainNames[map[new CellCoord(0, 0)].TerrainId]);
    }

    [Fact]
    public void Load_EmbeddedTileset_Works()
    {
        var extra = """
            <tileset firstgid="20" name="extra" tilecount="1">
             <tile id="0"><properties><property name="terrain" value="road"/></properties></tile>
            </tileset>
            """;
        var map = LoadText(TmxText.Map(2, 1, "20,1", extra));
        Assert.Equal("road", map.TerrainNames[map[new CellCoord(0, 0)].TerrainId]);
    }

    [Fact]
    public void Load_MissingFile_ThrowsWithPath()
    {
        var ex = Assert.Throws<MapLoadException>(() => TmxMapLoader.Load("does/not/exist.tmx"));
        Assert.Contains("exist.tmx", ex.Message);
    }

    [Fact]
    public void Load_MalformedXml_Throws()
    {
        LoadFails("<map width=");
    }

    [Fact]
    public void Load_XmlEncodedLayer_ExplainsTheSupportedFormats()
    {
        var xml = TmxText.Map(2, 1, "").Replace("<data encoding=\"csv\"></data>", "<data><tile gid=\"1\"/><tile gid=\"1\"/></data>");
        var ex = LoadFails(xml);
        Assert.Contains("CSV", ex.Message);
        Assert.Contains("Base64", ex.Message);
    }

    [Fact]
    public void Load_Base64Garbage_Throws()
    {
        var xml = TmxText.Map(2, 1, "1,1").Replace("encoding=\"csv\"", "encoding=\"base64\" compression=\"zlib\"");
        Assert.Contains("invalid base64", LoadFails(xml).Message);
    }

    [Fact]
    public void Load_InfiniteMap_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("infinite=\"0\"", "infinite=\"1\""));
        Assert.Contains("infinite", ex.Message);
    }

    [Fact]
    public void Load_NonOrthogonalMap_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("orthogonal", "isometric"));
        Assert.Contains("orthogonal", ex.Message);
    }

    [Fact]
    public void Load_NonSquareTiles_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("tileheight=\"16\"", "tileheight=\"8\""));
        Assert.Contains("square", ex.Message);
    }

    [Fact]
    public void Load_MissingTerrainLayer_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("name=\"terrain\"", "name=\"height\""));
        Assert.Contains("'terrain'", ex.Message);
    }

    [Fact]
    public void Load_UnknownLayerName_ListsExpectedNames()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1", TmxText.Layer("trees", 2, 1, "0,0")));
        Assert.Contains("unknown tile layer 'trees'", ex.Message);
        Assert.Contains("obstacles", ex.Message);
    }

    [Fact]
    public void Load_DuplicateLayer_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1", TmxText.Layer("terrain", 2, 1, "1,1")));
        Assert.Contains("duplicate tile layer 'terrain'", ex.Message);
    }

    [Fact]
    public void Load_LayerGroup_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1", "<group id=\"7\" name=\"g\"/>"));
        Assert.Contains("group", ex.Message);
    }

    [Fact]
    public void Load_EmptyTerrainCell_ReportsCoordinates()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,0"));
        Assert.Contains("(1,0)", ex.Message);
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void Load_TerrainTileWithoutTerrainProperty_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,4"));
        Assert.Contains("no 'terrain' property", ex.Message);
    }

    [Fact]
    public void Load_WrongTileCount_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 2, "1,1,1"));
        Assert.Contains("expected 4", ex.Message);
    }

    [Fact]
    public void Load_GidOutsideTilesets_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,99"));
        Assert.Contains("does not belong to any tileset", ex.Message);
    }

    [Fact]
    public void Load_FractionOutOfRange_Throws()
    {
        var extra = """
            <tileset firstgid="20" name="bad" tilecount="1">
             <tile id="0"><properties>
              <property name="terrain" value="x"/>
              <property name="concealment_per_m" type="float" value="1.5"/>
             </properties></tile>
            </tileset>
            """;
        var ex = LoadFails(TmxText.Map(2, 1, "20,1", extra));
        Assert.Contains("concealment_per_m", ex.Message);
        Assert.Contains("between 0 and 1", ex.Message);
    }

    [Fact]
    public void Load_HeightTileWithoutHeightProperty_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1", TmxText.Layer("height", 2, 1, "1,0")));
        Assert.Contains("'height_cm'", ex.Message);
    }

    [Fact]
    public void Load_MissingTilesetFile_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("heights.tsx", "missing.tsx"));
        Assert.Contains("tileset file not found: missing.tsx", ex.Message);
    }

    [Fact]
    public void Load_MoveCostAndImpassable_AreStoredAsExtraMoveCost()
    {
        var extra = """
            <tileset firstgid="20" name="move" tilecount="3">
             <tile id="0"><properties>
              <property name="terrain" value="mud"/>
              <property name="move_cost" type="float" value="2"/>
             </properties></tile>
             <tile id="1"><properties><property name="impassable" type="bool" value="true"/></properties></tile>
             <tile id="2"><properties><property name="move_cost" type="float" value="3.54"/></properties></tile>
            </tileset>
            """ + TmxText.Layer("obstacles", 3, 1, "0,21,22");
        var map = LoadText(TmxText.Map(3, 1, "20,20,20", extra));

        Assert.Equal(100, map[new CellCoord(0, 0)].ExtraMoveCost);
        Assert.False(map[new CellCoord(1, 0)].IsPassable);
        Assert.Equal(254, map[new CellCoord(2, 0)].ExtraMoveCost);
    }

    [Fact]
    public void Load_AnObstacleLowerThanTheGrowth_IsLowCover_NotCoverAllTheWayUp()
    {
        var extra = """
            <tileset firstgid="20" name="wood" tilecount="3">
             <tile id="0"><properties>
              <property name="terrain" value="forest"/>
              <property name="obstacle_height_cm" type="int" value="1500"/>
              <property name="cover" type="float" value="0.1"/>
             </properties></tile>
             <tile id="1"><properties>
              <property name="obstacle_height_cm" type="int" value="50"/>
              <property name="cover" type="float" value="0.7"/>
              <property name="move_cost" type="float" value="1.6"/>
             </properties></tile>
             <tile id="2"><properties>
              <property name="terrain" value="grass"/>
             </properties></tile>
            </tileset>
            """ + TmxText.Layer("obstacles", 3, 1, "21,0,21");
        var map = LoadText(TmxText.Map(3, 1, "20,20,22", extra));

        var inForest = map[new CellCoord(0, 0)];
        Assert.Equal(1500, inForest.ObstacleHeightCm); // the trees still stand over it
        Assert.Equal(26, inForest.Cover);                // 0.1: the forest's own cover up there
        Assert.Equal(179, inForest.LowCover);            // 0.7, but only below 50 cm
        Assert.Equal(50, inForest.LowCoverHeightCm);
        Assert.Equal(60, inForest.ExtraMoveCost);

        var onGrass = map[new CellCoord(2, 0)];
        Assert.Equal(50, onGrass.ObstacleHeightCm);      // nothing taller there: it is the obstacle
        Assert.Equal(179, onGrass.Cover);
        Assert.Equal(0, onGrass.LowCover);
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("4")]
    [InlineData("fast")]
    public void Load_MoveCostOutOfRange_Throws(string value)
    {
        var extra = $"""
            <tileset firstgid="20" name="bad" tilecount="1">
             <tile id="0"><properties>
              <property name="terrain" value="x"/>
              <property name="move_cost" type="float" value="{value}"/>
             </properties></tile>
            </tileset>
            """;
        var ex = LoadFails(TmxText.Map(1, 1, "20", extra));
        Assert.Contains("move_cost", ex.Message);
    }

    [Fact]
    public void Load_ImpassableNotBoolean_Throws()
    {
        var extra = """
            <tileset firstgid="20" name="bad" tilecount="1">
             <tile id="0"><properties>
              <property name="terrain" value="x"/>
              <property name="impassable" value="maybe"/>
             </properties></tile>
            </tileset>
            """;
        var ex = LoadFails(TmxText.Map(1, 1, "20", extra));
        Assert.Contains("impassable", ex.Message);
    }

    private static string Base64Map(uint[] gids, string? compression)
    {
        var bytes = new byte[gids.Length * 4];
        for (int i = 0; i < gids.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), gids[i]);
        if (compression is not null)
        {
            using var output = new MemoryStream();
            using (Stream z = compression == "zlib"
                       ? new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal)
                       : new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                z.Write(bytes);
            bytes = output.ToArray();
        }
        string attr = compression is null ? "" : $" compression=\"{compression}\"";
        return TmxText.Map(2, 1, "1,2").Replace("<data encoding=\"csv\">1,2</data>",
            $"<data encoding=\"base64\"{attr}>\n   {Convert.ToBase64String(bytes)}\n  </data>");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("zlib")]
    [InlineData("gzip")]
    public void Base64Layer_Loads(string? compression)
    {
        var map = LoadText(Base64Map([2, 2147483649], compression));
        Assert.Equal("forest", map.TerrainNames[map[new CellCoord(0, 0)].TerrainId]);
        Assert.Equal("grass", map.TerrainNames[map[new CellCoord(1, 0)].TerrainId]);
    }

    [Fact]
    public void Base64Layer_UnknownCompression_Throws()
    {
        var ex = LoadFails(Base64Map([1, 1], null).Replace("encoding=\"base64\"", "encoding=\"base64\" compression=\"zstd\""));
        Assert.Contains("zstd", ex.Message);
    }

    [Fact]
    public void Base64Layer_WrongLength_Throws()
    {
        var ex = LoadFails(Base64Map([1], null));
        Assert.Contains("expected 2", ex.Message);
    }

    [Fact]
    public void Load_MapProperties_AreKept_AndMissingOnesAreEmpty()
    {
        var xml = TmxText.Map(2, 1, "1,1").Replace("infinite=\"0\">", "infinite=\"0\">\n <properties><property name=\"source\" value=\"test data\"/></properties>");
        Assert.Equal("test data", LoadText(xml).Features.Properties["source"]);
        Assert.Empty(LoadText(TmxText.Map(2, 1, "1,1")).Features.Properties);
    }
}
