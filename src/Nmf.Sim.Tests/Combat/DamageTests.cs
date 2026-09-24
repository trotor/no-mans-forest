using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class DamageTests
{
    private static (Simulation Sim, Unit Unit) Setup()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 5);
        return (sim, sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, TestWeapons.Rifle()));
    }

    [Fact]
    public void ZeroLethality_GivesLightWoundsThatEscalateWithEachHit()
    {
        var (sim, unit) = Setup();
        var weapon = TestWeapons.Rifle(lethality: 0);
        var events = new List<SimEvent>();
        var levels = new List<WoundLevel>();
        for (int i = 0; i < 4; i++)
        {
            Damage.ApplyHit(sim, unit, weapon, sim.Tick, events);
            levels.Add(unit.Wound);
        }
        Assert.Equal(new[] { WoundLevel.Light, WoundLevel.Serious, WoundLevel.Incapacitated, WoundLevel.Dead }, levels);
        Assert.Contains<SimEvent>(new UnitWounded(0, unit.Id, WoundLevel.Dead), events);
    }

    [Fact]
    public void FullLethality_NeverGivesLightWounds()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 9);
        var weapon = TestWeapons.Rifle(lethality: 100);
        for (int i = 0; i < 60; i++)
        {
            var u = sim.SpawnUnit(Side.Blue, new Vec2(50 + i * 50, 50), 7);
            Damage.ApplyHit(sim, u, weapon, 0, []);
            Assert.NotEqual(WoundLevel.Light, u.Wound);
        }
    }

    [Fact]
    public void GoingDown_StopsEverythingAndDropsProne()
    {
        var (sim, unit) = Setup();
        sim.Submit(Side.Blue, new MoveOrder(unit.Id, new Vec2(5050, 1050)));
        sim.Step();
        unit.Action = CombatAction.Aiming;
        unit.Target = new UnitId(99);
        Damage.SetWound(sim, unit, WoundLevel.Incapacitated, sim.Tick, []);

        Assert.Null(unit.MoveTarget);
        Assert.Empty(unit.Path);
        Assert.Equal(Stance.Prone, unit.Stance);
        Assert.Null(unit.TargetStance);
        Assert.Equal(CombatAction.None, unit.Action);
        Assert.Null(unit.Target);
        var at = unit.Position;
        for (int i = 0; i < 40; i++) sim.Step();
        Assert.Equal(at, unit.Position);
    }

    [Fact]
    public void SeriousWound_BleedsUntilDown()
    {
        var (sim, unit) = Setup();
        Damage.SetWound(sim, unit, WoundLevel.Serious, 0, []);
        var events = new List<SimEvent>();
        for (int i = 0; i <= CombatRules.SeriousBleedTicks; i++) events.AddRange(sim.Step());
        Assert.Equal(WoundLevel.Incapacitated, unit.Wound);
        Assert.Contains(events, e => e is UnitWounded { Level: WoundLevel.Incapacitated });
    }

    [Fact]
    public void Wound_CostsMorale()
    {
        var (sim, unit) = Setup();
        Damage.SetWound(sim, unit, WoundLevel.Light, 0, []);
        Assert.Equal(CombatRules.BaseMorale - CombatRules.WoundMoraleLoss, unit.Morale);
    }

    [Fact]
    public void WoundedSoldier_MovesSlower()
    {
        var (sim, unit) = Setup();
        Damage.SetWound(sim, unit, WoundLevel.Serious, 0, []);
        unit.MoraleState = MoraleState.Steady;
        sim.Submit(Side.Blue, new MoveOrder(unit.Id, new Vec2(1090, 1050)));
        sim.Step();
        Assert.Equal(new Vec2(1053, 1050), unit.Position); // 7 cm/tick at 50 %
    }
}
