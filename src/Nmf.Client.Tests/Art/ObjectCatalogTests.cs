using Nmf.Client.Art;

namespace Nmf.Client.Tests.Art;

public class ObjectCatalogTests
{
    [Fact]
    public void Parse_ReadsSizesAndCounts()
    {
        var catalog = ObjectCatalog.Parse("""{"objects": {"spruce": {"size": 96, "count": 3}, "rock": {"size": 32, "count": 4}}}""");
        Assert.Equal(new ObjectInfo(96, 3), catalog.Objects["spruce"]);
        Assert.Equal(new ObjectInfo(32, 4), catalog.Objects["rock"]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"objects": {"rock": {"size": 0, "count": 4}}}""")]
    [InlineData("""{"objects": {"rock": {"size": 32}}}""")]
    public void Parse_Malformed_ThrowsFormatException(string json)
    {
        Assert.Throws<FormatException>(() => ObjectCatalog.Parse(json));
    }
}
