using Nmf.Sim;

namespace Nmf.Sim.Tests;

public class SimConstantsTests
{
    [Fact]
    public void TickRateAndCellSize_MatchSpec()
    {
        Assert.Equal(20, SimConstants.TicksPerSecond);
        Assert.Equal(100, SimConstants.CentimetersPerCell);
    }
}
