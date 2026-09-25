using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class AutoLootTests
{
    private static (Simulation Sim, Unit Man, Unit Body) Setup(WeaponDef? bodyWeapon = null)
    {
        var sim = new Simulation(new GridMap(80, 30, ["none"]), 1);
        var man = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, TestWeapons.Rifle());
        var body = sim.SpawnUnit(Side.Red, new Vec2(1650, 1050), 7, bodyWeapon ?? TestWeapons.Rifle());
        body.Wound = WoundLevel.Dead;
        man.Magazines = CombatRules.LowOnMagazines;
        return (sim, man, body);
    }

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var all = new List<SimEvent>();
        for (int i = 0; i < n; i++) all.AddRange(sim.Step());
        return all;
    }

    [Fact]
    public void LowOnAmmo_LootsNearbyBodyWithHisAmmo()
    {
        var (sim, man, body) = Setup();
        var events = StepN(sim, 200);
        Assert.Contains(events, e => e is UnitLooted l && l.Looter == man.Id && l.Body == body.Id && l.Magazines > 0);
        Assert.True(man.Magazines > CombatRules.LowOnMagazines);
    }

    [Fact]
    public void EnoughAmmo_DoesNotLoot()
    {
        var (sim, man, _) = Setup();
        man.Magazines = CombatRules.LowOnMagazines + 1;
        StepN(sim, 30);
        Assert.Null(man.LootTarget);
    }

    [Fact]
    public void UnderFire_DoesNotLoot()
    {
        var (sim, man, _) = Setup();
        for (int i = 0; i < 12; i++)
        {
            MoraleSystem.AddSuppression(sim, man, 20, sim.Tick, []);
            man.Suppression = 0;
            sim.Step();
            sim.Step();
        }
        Assert.Null(man.LootTarget);
    }

    [Fact]
    public void EnemyInSight_DoesNotLoot()
    {
        var (sim, man, _) = Setup();
        man.Magazines = 4;
        sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7);
        StepN(sim, 25); // he has spotted the enemy
        man.Magazines = CombatRules.LowOnMagazines;
        StepN(sim, 30);
        Assert.Null(man.LootTarget);
    }

    [Fact]
    public void BodyWithNothingUseful_Ignored()
    {
        var (sim, man, _) = Setup(TestWeapons.Smg());
        StepN(sim, 30);
        Assert.Null(man.LootTarget);
    }

    [Fact]
    public void BodyTooFar_Ignored()
    {
        var (sim, man, body) = Setup();
        body.Position = new Vec2(1050 + CombatRules.AutoLootRangeCm + 200, 1050);
        StepN(sim, 30);
        Assert.Null(man.LootTarget);
    }

    [Fact]
    public void BodyAlreadyTargetedByAFriend_Ignored()
    {
        var (sim, man, body) = Setup();
        var friend = sim.SpawnUnit(Side.Blue, new Vec2(6050, 2050), 7, TestWeapons.Rifle());
        sim.Submit(Side.Blue, new LootOrder(friend.Id, body.Id));
        StepN(sim, 10);
        Assert.Equal(body.Id, friend.LootTarget);
        Assert.Null(man.LootTarget);
    }
}
