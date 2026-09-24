using Nmf.Sim.Core;

namespace Nmf.Sim.Tests.Core;

public class Vec2Tests
{
    [Fact]
    public void Operators_AddAndSubtract()
    {
        Assert.Equal(new Vec2(4, 6), new Vec2(1, 2) + new Vec2(3, 4));
        Assert.Equal(new Vec2(-2, -2), new Vec2(1, 2) - new Vec2(3, 4));
    }

    [Fact]
    public void Length_UsesIntegerSquareRoot()
    {
        Assert.Equal(500, new Vec2(300, 400).Length);
        Assert.Equal(250_000L, new Vec2(300, 400).LengthSquared);
        Assert.Equal(141, new Vec2(100, 100).Length);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(99, 199, 0, 1)]
    [InlineData(100, 200, 1, 2)]
    [InlineData(-1, 250, -1, 2)]
    public void ToCell_FloorsToOneMetreCells(int x, int y, int cx, int cy)
    {
        Assert.Equal(new CellCoord(cx, cy), new Vec2(x, y).ToCell());
    }

    [Fact]
    public void CellCenter_IsMiddleOfCell()
    {
        Assert.Equal(new Vec2(250, 350), new CellCoord(2, 3).CenterCm);
    }
}
