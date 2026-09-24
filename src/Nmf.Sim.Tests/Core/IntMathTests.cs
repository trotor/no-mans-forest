using Nmf.Sim.Core;

namespace Nmf.Sim.Tests.Core;

public class IntMathTests
{
    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, 1L)]
    [InlineData(2L, 1L)]
    [InlineData(3L, 1L)]
    [InlineData(4L, 2L)]
    [InlineData(15L, 3L)]
    [InlineData(16L, 4L)]
    [InlineData(17L, 4L)]
    [InlineData(250_000L, 500L)]
    [InlineData(4_000_000_000_000_000_000L, 2_000_000_000L)]
    [InlineData(3_999_999_999_999_999_999L, 1_999_999_999L)]
    public void Isqrt_ReturnsFloorOfSquareRoot(long n, long expected)
    {
        Assert.Equal(expected, IntMath.Isqrt(n));
    }

    [Fact]
    public void Isqrt_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntMath.Isqrt(-1));
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(99, 100, 0)]
    [InlineData(100, 100, 1)]
    [InlineData(-1, 100, -1)]
    [InlineData(-100, 100, -1)]
    [InlineData(-101, 100, -2)]
    public void FloorDiv_RoundsTowardNegativeInfinity(int a, int b, int expected)
    {
        Assert.Equal(expected, IntMath.FloorDiv(a, b));
    }

    [Fact]
    public void FloorDiv_NonPositiveDivisor_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntMath.FloorDiv(5, 0));
    }
}
