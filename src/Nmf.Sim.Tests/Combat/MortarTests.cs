using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

/// <summary>Spec 2026-10-10-mortar-design: the 81 mm mortar by radio.</summary>
public class MortarTests
{
    private const int DelayMin = 60 * SimConstants.TicksPerSecond, DelayMax = 120 * SimConstants.TicksPerSecond;

    /// <summary>A 300 m open field: the radioman at (150, 280) m, an enemy at (150, 100) m; the target is the enemy.</summary>
    private static (Simulation Sim, MortarSupport Support, Unit Radio, Unit Enemy) Setup(int rounds = 8, int callSuccessPct = 100, ulong seed = 1)
    {
        var sim = new Simulation(new GridMap(300, 300, ["none"]), seed);
        var radio = sim.SpawnUnit(Side.Blue, new Vec2(15_050, 28_050), 8, TestWeapons.Rifle());
        var enemy = sim.SpawnUnit(Side.Red, new Vec2(15_050, 10_050), 8, TestWeapons.Rifle());
        foreach (var u in sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        var support = sim.AddMortarSupport(new MortarSupport(Side.Blue, radio.Id, rounds, DelayMin, DelayMax, callSuccessPct));
        return (sim, support, radio, enemy);
    }

    private static List<SimEvent> Run(Simulation sim, int ticks, Func<bool>? until = null)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < ticks && until?.Invoke() != true; i++)
            events.AddRange(sim.Step());
        return events;
    }

    private static int DistCm(Vec2 a, Vec2 b) => (int)IntMath.Isqrt((a - b).LengthSquared);

    [Fact]
    public void Call_TakesTenSeconds_ThenTheWait()
    {
        var (sim, support, radio, enemy) = Setup();
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        Run(sim, MortarSupport.CallTicks - 5);
        Assert.Equal(MortarPhase.Calling, support.Phase);
        var events = Run(sim, 10);
        Assert.Equal(MortarPhase.Waiting, support.Phase);
        Assert.Single(events.OfType<FireMissionCalled>());
    }

    [Fact]
    public void Call_Fails_TheMissionIsDropped()
    {
        var (sim, support, radio, enemy) = Setup(callSuccessPct: 0);
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        var events = Run(sim, MortarSupport.CallTicks + 5);
        Assert.Single(events.OfType<FireMissionFailed>());
        Assert.Equal(MortarPhase.Idle, support.Phase);
        Assert.Equal(8, support.Rounds);
    }

    [Fact]
    public void FirstShell_AfterTheDelay()
    {
        var (sim, _, radio, enemy) = Setup();
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        var first = Run(sim, 4_000).OfType<ShellExploded>().First();
        Assert.InRange(first.Tick - MortarSupport.CallTicks, DelayMin, DelayMax); // from the call getting through to the first burst
    }

    [Fact]
    public void Ranging_Seen_TheErrorHalves_ThenFireForEffect()
    {
        var (sim, support, radio, enemy) = Setup();
        var target = enemy.Position;
        enemy.Position = new Vec2(1_050, 1_050); // out of harm's way: only the shells are looked at
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, target, 4));
        var events = Run(sim, 6_000, () => support.Phase == MortarPhase.Idle && sim.Tick > 300);
        var ranging = events.OfType<ShellIncoming>().Where(s => s.Ranging).Select(s => DistCm(s.At, target)).ToList();
        Assert.InRange(ranging[0], 4_000, 6_000);
        for (int i = 1; i < ranging.Count; i++)
            Assert.InRange(ranging[i], ranging[i - 1] / 2 - 2, ranging[i - 1] / 2 + 2);
        Assert.True(ranging[^1] < 1_500 * 2, "ranging ended before the error came down");
        Assert.Equal(4, events.OfType<ShellIncoming>().Count(s => !s.Ranging));
        Assert.Contains(events, e => e is FireMissionPhaseChanged { Phase: MortarPhase.Effect });
    }

    [Fact]
    public void Ranging_Unseen_TheErrorStays_FourRoundsAtMost()
    {
        var (sim, support, radio, enemy) = Setup();
        var target = enemy.Position;
        // A tall thicket all round the target: nobody sees where the shells land.
        for (int y = 30; y < 170; y++)
            for (int x = 80; x < 220; x++)
            {
                ref var cell = ref sim.Map[new CellCoord(x, y)];
                cell.ObstacleHeightCm = 1_500;
                cell.ConcealmentPerM = 255;
            }
        enemy.Position = new Vec2(1_050, 1_050);
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, target, 4));
        var events = Run(sim, 6_000, () => support.Phase == MortarPhase.Idle && sim.Tick > 300);
        var ranging = events.OfType<ShellIncoming>().Where(s => s.Ranging).Select(s => DistCm(s.At, target)).ToList();
        Assert.Equal(MortarSupport.MaxRangingRounds, ranging.Count);
        Assert.All(ranging, d => Assert.InRange(d, ranging[0] - 2, ranging[0] + 2));
    }

    [Fact]
    public void Effect_TheRoundsAsked_ThreeSecondsApart()
    {
        var (sim, support, radio, enemy) = Setup();
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        var effect = Run(sim, 6_000, () => support.Phase == MortarPhase.Idle && sim.Tick > 300)
            .OfType<ShellExploded>().Where(s => !s.Ranging).Select(s => s.Tick).ToList();
        Assert.Equal(4, effect.Count);
        for (int i = 1; i < effect.Count; i++)
            Assert.Equal(MortarSupport.EffectIntervalTicks, effect[i] - effect[i - 1]);
    }

    [Fact]
    public void OutOfRounds_Ends_AndSaysSo()
    {
        var (sim, support, radio, enemy) = Setup(rounds: 3);
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        var events = Run(sim, 6_000, () => support.Phase == MortarPhase.Idle && sim.Tick > 300);
        Assert.Equal(3, events.OfType<ShellExploded>().Count());
        Assert.True(Assert.Single(events.OfType<FireMissionEnded>()).OutOfRounds);
        Assert.Equal(0, support.Rounds);
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        Assert.Contains(sim.Step(), e => e is OrderRejected);
    }

    [Fact]
    public void RadioDown_WhileWaiting_TheMissionIsLost_NoShells()
    {
        var (sim, support, radio, enemy) = Setup();
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        Run(sim, MortarSupport.CallTicks + 20);
        Assert.Equal(MortarPhase.Waiting, support.Phase);
        radio.Wound = WoundLevel.Dead;
        var events = Run(sim, 6_000);
        Assert.Empty(events.OfType<ShellIncoming>());
        Assert.True(Assert.Single(events.OfType<FireMissionEnded>()).RadioLost);
        Assert.Equal(MortarPhase.Idle, support.Phase);
        Assert.False(support.Available(sim));
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        Assert.Contains(sim.Step(), e => e is OrderRejected);
    }

    [Fact]
    public void RadioDown_WhileRanging_OnlyTheShellInTheAirLands()
    {
        var (sim, support, radio, enemy) = Setup();
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        Run(sim, 4_000, () => support.InFlight > 0);
        Assert.Equal(MortarPhase.Ranging, support.Phase);
        radio.Wound = WoundLevel.Dead;
        var events = Run(sim, 4_000);
        Assert.Single(events.OfType<ShellExploded>());
        Assert.Empty(events.OfType<ShellIncoming>());
        Assert.True(Assert.Single(events.OfType<FireMissionEnded>()).RadioLost);
    }

    [Fact]
    public void Rejected_WithoutSupport_OffTheMap_OrByAnotherMan()
    {
        var sim = new Simulation(new GridMap(100, 100, ["none"]), 1);
        var man = sim.SpawnUnit(Side.Blue, new Vec2(5_050, 5_050), 8, TestWeapons.Rifle());
        sim.Submit(Side.Blue, new FireMissionOrder(man.Id, new Vec2(5_050, 1_050), 4));
        Assert.Contains(sim.Step(), e => e is OrderRejected);

        var (sim2, _, radio, _) = Setup();
        sim2.Submit(Side.Blue, new FireMissionOrder(radio.Id, new Vec2(-500, 1_000), 4));
        Assert.Contains(sim2.Step(), e => e is OrderRejected);
        var other = sim2.SpawnUnit(Side.Blue, new Vec2(14_050, 28_050), 8, TestWeapons.Rifle());
        sim2.Submit(Side.Blue, new FireMissionOrder(other.Id, new Vec2(15_050, 10_050), 4));
        Assert.Contains(sim2.Step(), e => e is OrderRejected);
    }

    [Fact]
    public void NewOrder_ReplacesOnlyBeforeFiring()
    {
        var (sim, support, radio, enemy) = Setup();
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        Run(sim, 50);
        var elsewhere = enemy.Position + new Vec2(3_000, 0);
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, elsewhere, 4));
        sim.Step();
        Assert.Equal(elsewhere, support.Target);
        Run(sim, 6_000, () => support.Phase == MortarPhase.Effect);
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        Assert.Contains(sim.Step(), e => e is OrderRejected);
        Assert.Equal(elsewhere, support.Target);
    }

    [Fact]
    public void AShell_Suppresses_AndCanHurt_OurOwnToo()
    {
        var (sim, support, radio, enemy) = Setup();
        var own = sim.SpawnUnit(Side.Blue, enemy.Position + new Vec2(600, 0), 8, TestWeapons.Rifle()); // danger close
        sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
        Run(sim, 6_000, () => support.Phase == MortarPhase.Idle && sim.Tick > 300);
        Assert.True(enemy.Suppression > 0 || enemy.Wound > WoundLevel.None);
        Assert.True(own.Suppression > 0 || own.Wound > WoundLevel.None, "our own man next to the target came through untouched");
    }

    [Fact]
    public void SameSeed_SameMission()
    {
        static ulong Hash()
        {
            var (sim, support, radio, enemy) = Setup(seed: 7);
            sim.Submit(Side.Blue, new FireMissionOrder(radio.Id, enemy.Position, 4));
            Run(sim, 3_500);
            return StateHash.Compute(sim);
        }
        Assert.Equal(Hash(), Hash());
    }
}
