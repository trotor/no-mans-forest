using Nmf.Client.Art;

namespace Nmf.Client.Tests.Art;

public class SpriteSheetTests
{
    private const string Json = """
        {
          "cellSize": 64, "pixelsPerMetre": 32,
          "directions": ["N","NE","E","SE","S","SW","W","NW"],
          "animations": {
            "idle": {"row": 0, "frames": 1, "strideCm": 0},
            "walk": {"row": 8, "frames": 6, "strideCm": 120},
            "run": {"row": 16, "frames": 6, "strideCm": 180},
            "crouch": {"row": 24, "frames": 1, "strideCm": 0},
            "prone": {"row": 32, "frames": 1, "strideCm": 0},
            "crawl": {"row": 40, "frames": 4, "strideCm": 60}
          }
        }
        """;

    [Fact]
    public void Parse_ReadsContract()
    {
        var sheet = SpriteSheet.Parse(Json);
        Assert.Equal(64, sheet.CellSize);
        Assert.Equal(32, sheet.PixelsPerMetre);
        Assert.Equal(8, sheet.Directions.Count);
        Assert.Equal(new AnimationInfo(8, 6, 120), sheet.Animations["walk"]);
    }

    [Fact]
    public void FrameRect_UsesRowPlusDirectionAndColumn()
    {
        var sheet = SpriteSheet.Parse(Json);
        Assert.Equal((128, (8 + 2) * 64, 64), sheet.FrameRect("walk", 2, 2));
    }

    [Fact]
    public void FrameRect_WrapsFrameIndex()
    {
        var sheet = SpriteSheet.Parse(Json);
        Assert.Equal(sheet.FrameRect("crawl", 0, 1), sheet.FrameRect("crawl", 0, 5));
    }

    [Fact]
    public void FrameRect_BadInputs_Throw()
    {
        var sheet = SpriteSheet.Parse(Json);
        Assert.Throws<ArgumentException>(() => sheet.FrameRect("dance", 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.FrameRect("idle", 8, 0));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"cellSize": 64}""")]
    [InlineData("""{"cellSize": 64, "pixelsPerMetre": 32, "directions": ["N"], "animations": {}}""")]
    public void Parse_Malformed_ThrowsFormatException(string json)
    {
        Assert.Throws<FormatException>(() => SpriteSheet.Parse(json));
    }

    [Fact]
    public void Parse_MissingAnimation_NamesIt()
    {
        var ex = Assert.Throws<FormatException>(() => SpriteSheet.Parse(Json.Replace("\"crawl\"", "\"slide\"")));
        Assert.Contains("crawl", ex.Message);
    }

    [Fact]
    public void Parse_ZeroFrames_Throws()
    {
        Assert.Throws<FormatException>(() => SpriteSheet.Parse(Json.Replace("\"frames\": 6, \"strideCm\": 180", "\"frames\": 0, \"strideCm\": 180")));
    }

    [Fact]
    public void RequiredSize_CoversWidestAnimationAndLastRow()
    {
        Assert.Equal((384, 3072), SpriteSheet.Parse(Json).RequiredSize());
    }

    [Fact]
    public void Parse_NegativeRow_Throws()
    {
        Assert.Throws<FormatException>(() => SpriteSheet.Parse(Json.Replace("\"row\": 8,", "\"row\": -8,")));
    }

    [Fact]
    public void ArtSize_TooSmallImage_ThrowsNamingFile()
    {
        var ex = Assert.Throws<FormatException>(() => ArtSize.Require("soviet.png", 256, 1024, 384, 3072));
        Assert.Contains("soviet.png", ex.Message);
        Assert.Contains("384x3072", ex.Message);
        ArtSize.Require("ok.png", 384, 3072, 384, 3072);
    }
}
