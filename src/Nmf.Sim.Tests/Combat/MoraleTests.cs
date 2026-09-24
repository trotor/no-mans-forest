using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class MoraleTests
{
    private static Simulation NewSim() => new(new GridMap(60, 20, ["none"]), 7);

    [Fact]
    public void Suppression_DecaysFasterWhenProneAndNearLeader()
    {
        var sim = NewSim();
        var standing = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        var prone = sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7);
        prone.Stance = Stance.Prone;
        standing.Suppression = 100;
        prone.Suppression = 100;
        sim.Step();
        Assert.Equal(97, standing.Suppression);
        Assert.Equal(95, prone.Suppression);

        sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7, null, isLeader: true);
        sim.Step();
        Assert.Equal(92, standing.Suppression);
    }

    [Fact]
    public void HeavySuppression_PinsAndDropsProne_ThenUnpinsWhenItFades()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = CombatRules.MaxMorale;
        var events = new List<SimEvent>();
        MoraleSystem.AddSuppression(sim, u, 450, sim.Tick, events);
        Assert.Equal(MoraleState.Pinned, u.MoraleState);
        Assert.Equal(Stance.Prone, u.TargetStance);
        Assert.Contains<SimEvent>(new MoraleChanged(0, u.Id, MoraleState.Pinned), events);

        for (int i = 0; i < 60 && u.MoraleState == MoraleState.Pinned; i++) sim.Step();
        Assert.Equal(MoraleState.Steady, u.MoraleState);
        Assert.True(u.Suppression < CombatRules.UnpinBelow);
    }

    [Fact]
    public void MoraleCheck_WithNoMorale_Breaks()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = 0;
        var events = new List<SimEvent>();
        MoraleSystem.Check(sim, u, 0, events);
        Assert.Equal(MoraleState.Broken, u.MoraleState);
        Assert.Contains<SimEvent>(new MoraleChanged(0, u.Id, MoraleState.Broken), events);
    }

    [Fact]
    public void MoraleCheck_WithFullMoraleAndNoPressure_NeverBreaks()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = CombatRules.MaxMorale;
        for (int i = 0; i < 200; i++) MoraleSystem.Check(sim, u, 0, []);
        Assert.Equal(MoraleState.Steady, u.MoraleState);
    }

    [Fact]
    public void CrossingTheCheckThreshold_TriggersAMoraleCheck()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = 0;
        MoraleSystem.AddSuppression(sim, u, 850, 0, []);
        Assert.Equal(MoraleState.Broken, u.MoraleState);
    }

    [Fact]
    public void LeaderDeath_PassesCommandAndCostsEveryoneMorale()
    {
        var sim = NewSim();
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, null, isLeader: true);
        var second = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7);
        var far = sim.SpawnUnit(Side.Blue, new Vec2(5050, 1050), 7);
        var events = new List<SimEvent>();

        Damage.SetWound(sim, leader, WoundLevel.Dead, 0, events);

        Assert.False(leader.IsLeader);
        Assert.True(second.IsLeader);
        Assert.Equal(CombatRules.ActingLeaderQualityPct, second.LeaderQualityPct);
        Assert.Contains<SimEvent>(new LeaderChanged(0, Side.Blue, second.Id), events);
        Assert.Equal(CombatRules.BaseMorale - CombatRules.CasualtyMoraleLoss - CombatRules.LeaderLossMoraleLoss, second.Morale);
        Assert.Equal(CombatRules.BaseMorale - CombatRules.LeaderLossMoraleLoss, far.Morale);
    }

    [Fact]
    public void LeaderDeath_WithEveryoneElseDown_LeavesNoLeaderWithoutCrashing()
    {
        var sim = NewSim();
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, null, isLeader: true);
        var other = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7);
        Damage.SetWound(sim, other, WoundLevel.Incapacitated, 0, []);
        Damage.SetWound(sim, leader, WoundLevel.Dead, 0, []);
        Assert.DoesNotContain(sim.Units, u => u.IsLeader);
        sim.Step();
    }

    [Fact]
    public void BrokenSoldier_RalliesQuicklyNearHisLeader()
    {
        var sim = NewSim();
        sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7, null, isLeader: true);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.MoraleState = MoraleState.Broken;
        u.Morale = 900;
        var events = new List<SimEvent>();
        events.AddRange(sim.Step());
        Assert.Equal(MoraleState.Steady, u.MoraleState);
        Assert.Equal(CombatRules.MaxMorale, u.Morale);
        Assert.Contains<SimEvent>(new MoraleChanged(0, u.Id, MoraleState.Steady), events);
    }

    [Fact]
    public void BrokenSoldier_WithLowMoraleAndNoLeader_StaysBroken()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.MoraleState = MoraleState.Broken;
        u.Morale = 200;
        for (int i = 0; i < 400; i++) sim.Step();
        Assert.Equal(MoraleState.Broken, u.MoraleState);
    }

    [Fact]
    public void Morale_RecoversSlowlyWhenCalm()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = 500;
        for (int i = 0; i < 40; i++) sim.Step(); // intervals at ticks 0 and 20
        Assert.Equal(510, u.Morale);
    }
}
