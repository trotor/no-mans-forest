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
    public void Suppression_FadesPerSecond_FasterWhenProneAndNearLeader()
    {
        var sim = NewSim();
        var standing = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        var prone = sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7);
        prone.Stance = Stance.Prone;
        standing.Suppression = 200; // below GoProneAt, so he stays on his feet
        prone.Suppression = 200;
        for (int i = 0; i < 20; i++) sim.Step();
        Assert.Equal(180, standing.Suppression); // 20 / s
        Assert.Equal(170, prone.Suppression);    // 30 / s

        sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7, null, isLeader: true);
        for (int i = 0; i < 20; i++) sim.Step();
        Assert.Equal(150, standing.Suppression); // 30 / s near the leader
    }

    [Fact]
    public void PinnedMan_StaysPinnedForSecondsAfterTheFireStops()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = CombatRules.MaxMorale;
        MoraleSystem.AddSuppression(sim, u, 450, sim.Tick, []);
        for (int i = 0; i < 3 * SimConstants.TicksPerSecond; i++) sim.Step();
        Assert.Equal(MoraleState.Pinned, u.MoraleState);
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

        for (int i = 0; i < 200 && u.MoraleState == MoraleState.Pinned; i++) sim.Step();
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

    [Fact]
    public void RallyingIntoPinned_StopsTheRetreatAndDropsProne()
    {
        var sim = NewSim();
        sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7, null, isLeader: true);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        sim.Submit(Side.Blue, new Nmf.Sim.Orders.MoveOrder(u.Id, new Vec2(5050, 1050), MoveMode.Run));
        sim.Step();
        u.MoraleState = MoraleState.Broken;
        u.Morale = CombatRules.MaxMorale;
        for (int i = 0; i < 400 && u.MoraleState == MoraleState.Broken; i++)
        {
            u.Suppression = 300; // stays pinned-level while he rallies
            sim.Step();
        }
        Assert.Equal(MoraleState.Pinned, u.MoraleState);
        Assert.Null(u.MoveTarget);
        Assert.Equal(Stance.Prone, u.TargetStance ?? u.Stance);
    }

    [Fact]
    public void Succession_PrefersASteadyManOverABrokenOne()
    {
        var sim = NewSim();
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, null, isLeader: true);
        var broken = sim.SpawnUnit(Side.Blue, new Vec2(5050, 1050), 7);
        var steady = sim.SpawnUnit(Side.Blue, new Vec2(5250, 1050), 7);
        broken.MoraleState = MoraleState.Broken;
        steady.Morale = CombatRules.MaxMorale;
        Damage.SetWound(sim, leader, WoundLevel.Dead, 0, []);
        Assert.True(steady.IsLeader);
        Assert.False(broken.IsLeader);
    }

    [Fact]
    public void PinnedLeader_GivesNoRallyBonus()
    {
        var sim = NewSim();
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7, null, isLeader: true);
        leader.MoraleState = MoraleState.Pinned;
        leader.Suppression = 600;
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.MoraleState = MoraleState.Broken;
        u.Morale = 250; // 250 + 200 would rally sometimes; 250 - 300 never does
        for (int i = 0; i < 400; i++) { leader.Suppression = 600; sim.Step(); }
        Assert.Equal(MoraleState.Broken, u.MoraleState);
    }

    [Fact]
    public void Suppression_MarksTheManAsUnderFire()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        Assert.False(MoraleSystem.UnderFire(u, sim.Tick));
        for (int i = 0; i < 10; i++) sim.Step();
        MoraleSystem.AddSuppression(sim, u, 30, sim.Tick, []);
        Assert.True(MoraleSystem.UnderFire(u, sim.Tick + CombatRules.UnderFireTicks));
        Assert.False(MoraleSystem.UnderFire(u, sim.Tick + CombatRules.UnderFireTicks + 1));
    }

    [Fact]
    public void Recovery_StopsAtTheMansOwnBase()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        Assert.Equal(CombatRules.BaseMorale, u.BaseMorale);
        u.BaseMorale = 900;
        u.Morale = 880;
        for (int i = 0; i < 400; i++) sim.Step();
        Assert.Equal(900, u.Morale);
    }

    [Fact]
    public void Successor_KeepsAHigherOwnBase()
    {
        var sim = NewSim();
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, null, isLeader: true);
        var hero = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7);
        hero.BaseMorale = 920;
        Damage.SetWound(sim, leader, WoundLevel.Dead, sim.Tick, []);
        Assert.True(hero.IsLeader);
        Assert.Equal(920, hero.BaseMorale);
    }

    [Fact]
    public void Nerve_RaisesThePinThreshold_AndTheRecoveryLine()
    {
        var sim = NewSim();
        var ordinary = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        var hero = sim.SpawnUnit(Side.Blue, new Vec2(3050, 1050), 7);
        ordinary.Morale = hero.Morale = CombatRules.MaxMorale;
        hero.Nerve = 95;
        MoraleSystem.AddSuppression(sim, ordinary, 450, sim.Tick, []);
        MoraleSystem.AddSuppression(sim, hero, 450, sim.Tick, []);
        Assert.Equal(MoraleState.Pinned, ordinary.MoraleState);
        Assert.Equal(MoraleState.Steady, hero.MoraleState);
        Assert.Equal(580, MoraleSystem.PinnedAt(hero));
        Assert.Equal(362, MoraleSystem.UnpinBelow(hero));
        Assert.Equal(CombatRules.PinnedAt, MoraleSystem.PinnedAt(ordinary));
        var timid = sim.SpawnUnit(Side.Blue, new Vec2(5050, 1050), 7);
        timid.Nerve = 40;
        Assert.Equal(360, MoraleSystem.PinnedAt(timid));
    }

    [Fact]
    public void Nerve_MakesBreakingRarer()
    {
        int Breaks(int nerve)
        {
            int broken = 0;
            for (ulong seed = 0; seed < 400; seed++)
            {
                var sim = new Simulation(new GridMap(10, 10, ["none"]), seed);
                var u = sim.SpawnUnit(Side.Blue, new Vec2(550, 550), 7);
                u.Nerve = nerve;
                u.Morale = 600;
                u.Suppression = 600;
                MoraleSystem.Check(sim, u, 0, []);
                if (u.MoraleState == MoraleState.Broken) broken++;
            }
            return broken;
        }
        int ordinary = Breaks(50), hero = Breaks(95);
        // +180 on the check: at morale 600 under 600 suppression, about 36 % instead of 55 %.
        Assert.True(hero * 10 < ordinary * 8, $"hero broke {hero} times, ordinary {ordinary}");
    }

    [Fact]
    public void Nerve_KeepsAnIdleManOnHisFeetLonger()
    {
        var sim = NewSim();
        var hero = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        hero.Nerve = 95;
        hero.Suppression = 300; // over the usual 250 go-prone line, under his own 362
        hero.LastSuppressedTick = -1000; // no fresh fire reaction
        for (int i = 0; i < 10; i++) sim.Step();
        Assert.Null(hero.TargetStance);
        Assert.Equal(Stance.Standing, hero.Stance);
    }
}
