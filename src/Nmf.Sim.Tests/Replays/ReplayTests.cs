using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Replays;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Replays;

public class ReplayTests
{
    private static Simulation NewSim(ulong seed = 7)
    {
        var sim = new Simulation(new GridMap(40, 40, ["none"]), seed);
        sim.SpawnUnit(Side.Blue, new Vec2(100, 100), 7);
        sim.SpawnUnit(Side.Blue, new Vec2(300, 100), 7);
        sim.SpawnUnit(Side.Red, new Vec2(3500, 3500), 5);
        return sim;
    }

    /// <summary>Plays 600 ticks with pseudo-random orders from a separate "player" RNG.</summary>
    private static Simulation PlayRandomSession()
    {
        var sim = NewSim();
        var player = new Rng(999);
        for (int t = 0; t < 600; t++)
        {
            if (player.Chance(50))
            {
                var unit = sim.Units[player.NextInt(sim.Units.Count)];
                var target = new Vec2(player.NextInt(4000), player.NextInt(4000));
                sim.Submit(unit.Side, new MoveOrder(unit.Id, target));
            }
            sim.Step();
        }
        return sim;
    }

    [Fact]
    public void StateHash_IsStableForSameState()
    {
        Assert.Equal(StateHash.Compute(NewSim()), StateHash.Compute(NewSim()));
    }

    [Fact]
    public void StateHash_ChangesWhenUnitMoves()
    {
        var sim = NewSim();
        var before = StateHash.Compute(sim);
        sim.Submit(Side.Blue, new MoveOrder(sim.Units[0].Id, new Vec2(500, 500)));
        sim.Step();
        Assert.NotEqual(before, StateHash.Compute(sim));
    }

    [Fact]
    public void StateHash_ChangesWhenRngIsDrawn()
    {
        var sim = NewSim();
        var before = StateHash.Compute(sim);
        sim.Rng.NextUInt();
        Assert.NotEqual(before, StateHash.Compute(sim));
    }

    [Fact]
    public void Replay_ReproducesIdenticalFinalState()
    {
        var original = PlayRandomSession();
        var replay = ReplayRunner.Capture(original);
        Assert.NotEmpty(replay.Orders);

        var rerun = NewSim();
        ReplayRunner.Run(rerun, replay);

        Assert.Equal(original.Tick, rerun.Tick);
        Assert.Equal(StateHash.Compute(original), StateHash.Compute(rerun));
    }

    [Fact]
    public void Replay_WithChangedOrder_DivergesFromOriginal()
    {
        var original = PlayRandomSession();
        var replay = ReplayRunner.Capture(original);
        var orders = replay.Orders.ToList();
        // Change the last order: an earlier change could be erased when the unit later arrives at the same target.
        orders[^1] = orders[^1] with { Order = new MoveOrder(orders[^1].Order.Unit, new Vec2(1, 1)) };

        var rerun = NewSim();
        ReplayRunner.Run(rerun, replay with { Orders = orders });

        Assert.NotEqual(StateHash.Compute(original), StateHash.Compute(rerun));
    }

    [Fact]
    public void Run_OnAlreadySteppedSim_Throws()
    {
        var sim = NewSim();
        sim.Step();
        Assert.Throws<InvalidOperationException>(() => ReplayRunner.Run(sim, new Replay(7, 10, [])));
    }

    [Fact]
    public void Run_WithDifferentSeed_Throws()
    {
        Assert.Throws<ArgumentException>(() => ReplayRunner.Run(NewSim(seed: 8), new Replay(7, 10, [])));
    }

    [Fact]
    public void Run_WithUnsortedOrders_Throws()
    {
        var order = new MoveOrder(new UnitId(1), new Vec2(10, 10));
        var replay = new Replay(7, 10, [new LoggedOrder(5, Side.Blue, order), new LoggedOrder(2, Side.Blue, order)]);
        Assert.Throws<ArgumentException>(() => ReplayRunner.Run(NewSim(), replay));
    }

    [Fact]
    public void Run_WithOrderAtOrAfterEndTick_Throws()
    {
        var order = new MoveOrder(new UnitId(1), new Vec2(10, 10));
        var replay = new Replay(7, 10, [new LoggedOrder(10, Side.Blue, order)]);
        Assert.Throws<ArgumentException>(() => ReplayRunner.Run(NewSim(), replay));
    }

    [Fact]
    public void StateHash_ChangesWhenStanceChanges()
    {
        var sim = NewSim();
        sim.Step();
        var before = StateHash.Compute(sim);
        var other = NewSim();
        other.Submit(Side.Blue, new SetStanceOrder(other.Units[0].Id, Stance.Prone));
        other.Step();
        Assert.NotEqual(before, StateHash.Compute(other));
    }
}
