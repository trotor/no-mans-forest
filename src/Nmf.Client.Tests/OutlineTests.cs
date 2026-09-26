using Nmf.Client;

namespace Nmf.Client.Tests;

public class OutlineTests
{
    [Fact]
    public void StrongOutlines_AreWiderOpaqueRimmedAndRinged()
    {
        var normal = UnitOutlines.Of(OutlineStrength.Normal);
        var strong = UnitOutlines.Of(OutlineStrength.Strong);
        Assert.True(strong.WidthScale > normal.WidthScale);
        Assert.True(strong.Alpha > normal.Alpha);
        Assert.True(strong.DarkRim && !normal.DarkRim);
        Assert.True(strong.GroundRing && !normal.GroundRing);
        Assert.Equal(OutlineStrength.Strong, UnitOutlines.Next(OutlineStrength.Normal));
        Assert.Equal(OutlineStrength.Normal, UnitOutlines.Next(OutlineStrength.Strong));
        Assert.Equal("Selkeät reunat: päällä (O)", UnitOutlines.Toast(OutlineStrength.Strong, "fi"));
        Assert.Equal("Clear outlines: off (O)", UnitOutlines.Toast(OutlineStrength.Normal, "en"));
    }
}
