using Nmf.Client.Art;

namespace Nmf.Client.Tests.Art;

public class FacingTests
{
    [Theory]
    [InlineData(0, -10, 0)]
    [InlineData(10, -10, 1)]
    [InlineData(10, 0, 2)]
    [InlineData(10, 10, 3)]
    [InlineData(0, 10, 4)]
    [InlineData(-10, 10, 5)]
    [InlineData(-10, 0, 6)]
    [InlineData(-10, -10, 7)]
    [InlineData(10, -3, 2)]
    public void FromDelta_MapsToEightDirections(double dx, double dy, int expected)
    {
        Assert.Equal(expected, Facing.FromDelta(dx, dy));
    }
}
