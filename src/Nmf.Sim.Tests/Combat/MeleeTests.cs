using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class MeleeTests
{
    private static Simulation NewSim(ulong seed = 4) => new(new GridMap(60, 30, ["none"]), seed);

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var all = new List<SimEvent>();
        for (int i = 0; i < n; i++) all.AddRange(sim.Step());
        return all;
    }

    [Fact]
    public void EnemiesSideBySide_FightUntilOneFalls()
    {
        var sim = NewSim();
        var a = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7, TestWeapons.Smg());
        var b = sim.SpawnUnit(Side.Red, new Vec2(1150, 1000), 7, TestWeapons.Rifle());
        var first = sim.Step();
        Assert.Contains<SimEvent>(new MeleeStarted(0, a.Id, b.Id), first);
        Assert.Equal(CombatAction.Melee, a.Action);
        Assert.Equal(b.Id, a.MeleeOpponent);
        Assert.Equal(a.Id, b.MeleeOpponent);

        var events = StepN(sim, CombatRules.MeleeTicks);
        var ended = Assert.Single(events.OfType<MeleeEnded>());
        var loser = sim.FindUnit(ended.Loser)!;
        Assert.NotEqual(WoundLevel.None, loser.Wound);
        Assert.DoesNotContain(events, e => e is ShotFired);
        Assert.Equal(new Vec2(1000, 1000), a.Position);
    }

    [Fact]
    public void TwoAgainstOne_EachManFightsOnlyOneFightAtATime()
    {
        var sim = NewSim();
        var a = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        var b = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1150), 7);
        var red = sim.SpawnUnit(Side.Red, new Vec2(1100, 1070), 7);
        var events = sim.Step();
        Assert.Single(events.OfType<MeleeStarted>());
        Assert.Equal(CombatAction.Melee, red.Action);
        Assert.Single(new[] { a, b }, u => u.Action == CombatAction.Melee);
    }

    [Fact]
    public void BrokenSoldierNextToAnEnemy_Surrenders()
    {
        var sim = NewSim();
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        var mate = sim.SpawnUnit(Side.Blue, new Vec2(1500, 1000), 7);
        blue.MoraleState = MoraleState.Broken;
        sim.SpawnUnit(Side.Red, new Vec2(1150, 1000), 7);
        var events = sim.Step();
        Assert.Contains<SimEvent>(new UnitCaptured(0, blue.Id), events);
        Assert.True(blue.IsCaptured);
        Assert.True(blue.IsOutOfAction);
        Assert.True(blue.IsAlive);
        Assert.Equal(Stance.Crouching, blue.Stance);
        Assert.True(mate.Morale < CombatRules.BaseMorale);
    }

    [Fact]
    public void FighterShotMidFight_ReleasesHisOpponent()
    {
        var sim = NewSim();
        var a = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        var b = sim.SpawnUnit(Side.Red, new Vec2(1150, 1000), 7);
        sim.Step();
        Damage.SetWound(sim, b, WoundLevel.Dead, sim.Tick, []);
        sim.Step();
        Assert.Equal(CombatAction.None, a.Action);
        Assert.Null(a.MeleeOpponent);
    }

    [Fact]
    public void Melee_IsDeterministic()
    {
        ulong Run()
        {
            var sim = NewSim(8);
            sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7, TestWeapons.Rifle());
            sim.SpawnUnit(Side.Red, new Vec2(1150, 1000), 7, TestWeapons.Smg());
            StepN(sim, 200);
            return StateHash.Compute(sim);
        }
        Assert.Equal(Run(), Run());
    }
}
