using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Units;

/// <summary>Men moving at another man's pace (escorting the men who carry a wounded one): never more than 10 m ahead of him.</summary>
public class EscortTests
{
    private static Vec2 M(int x, int y) => new(x * 100 + 50, y * 100 + 50);

    private static long Left(Unit u) => u.MoveTarget is { } goal ? IntMath.Isqrt((goal - u.Position).LengthSquared) : 0;

    /// <summary>A slow man crawling 50 m east, a second walking beside him (at his pace, or not).</summary>
    private static (Simulation Sim, Unit Slow, Unit Escort) Field(bool paced)
    {
        var sim = new Simulation(new GridMap(120, 40, ["none"]), 1);
        var slow = sim.SpawnUnit(Side.Blue, M(10, 20), 8, TestWeapons.Rifle());
        var escort = sim.SpawnUnit(Side.Blue, M(10, 23), 8, TestWeapons.Rifle());
        foreach (var u in sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        sim.Submit(Side.Blue, new MoveOrder(slow.Id, M(60, 20), MoveMode.Crawl));
        sim.Submit(Side.Blue, new MoveOrder(escort.Id, M(60, 23), MoveMode.Walk, paced ? slow.Id : null));
        sim.Step();
        return (sim, slow, escort);
    }

    [Fact]
    public void Escort_KeepsNoMoreThanTenMetresAhead_AndArrivesWithHim()
    {
        var (sim, slow, escort) = Field(paced: true);
        Assert.Equal(slow.Id, escort.PaceWith);
        for (int i = 0; i < 12000 && slow.MoveTarget is not null; i++)
        {
            sim.Step();
            Assert.True(Left(escort) + Movement.EscortLeadCm + 50 >= Left(slow), $"tick {sim.Tick}: escort {Left(escort)} cm to go, the slow man {Left(slow)}");
        }
        Assert.Null(slow.MoveTarget);
        for (int i = 0; i < 400 && escort.MoveTarget is not null; i++)
            sim.Step();
        Assert.True((escort.Position - M(60, 23)).LengthSquared < 100L * 100); // there too, at the end
    }

    [Fact]
    public void WithoutPace_HeGoesOnAhead()
    {
        var (sim, slow, escort) = Field(paced: false);
        for (int i = 0; i < 400; i++)
            sim.Step();
        Assert.True(Left(slow) - Left(escort) > 2 * Movement.EscortLeadCm);
    }

    [Fact]
    public void AnotherOrder_LetsHimGo()
    {
        var (sim, slow, escort) = Field(paced: true);
        sim.Submit(Side.Blue, new MoveOrder(escort.Id, M(100, 30), MoveMode.Walk));
        sim.Step();
        Assert.Null(escort.PaceWith);
        sim.Submit(Side.Blue, new MoveOrder(escort.Id, M(60, 23), MoveMode.Walk, slow.Id));
        sim.Step();
        sim.Submit(Side.Blue, new StopOrder(escort.Id));
        sim.Step();
        Assert.Null(escort.PaceWith);
    }

    [Fact]
    public void TheManHeKeepsPaceWithFalls_HeGoesOn()
    {
        var (sim, slow, escort) = Field(paced: true);
        for (int i = 0; i < 200; i++)
            sim.Step();
        slow.Wound = Nmf.Sim.Combat.WoundLevel.Dead;
        for (int i = 0; i < 1500 && escort.MoveTarget is not null; i++)
            sim.Step();
        Assert.Null(escort.MoveTarget);
    }

    [Fact]
    public void PaceWith_IsInTheStateHash()
    {
        var (a, _, _) = Field(paced: true);
        var (b, _, _) = Field(paced: false);
        Assert.NotEqual(StateHash.Compute(a), StateHash.Compute(b));
    }
}
