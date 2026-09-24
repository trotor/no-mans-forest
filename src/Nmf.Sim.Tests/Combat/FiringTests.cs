using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class FiringTests
{
    private static GridMap Open() => new(60, 20, ["none"]);

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var all = new List<SimEvent>();
        for (int i = 0; i < n; i++) all.AddRange(sim.Step());
        return all;
    }

    [Fact]
    public void ArmedSoldier_SpotsAndShootsAVisibleEnemy()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0));
        var red = sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        var events = StepN(sim, 60);
        Assert.Contains(events, e => e is ShotFired s && s.Shooter == blue.Id && s.Hit == red.Id);
        Assert.NotEqual(WoundLevel.None, red.Wound);
        Assert.True(blue.LastShotTick > 0);
    }

    [Fact]
    public void HoldFire_NeverShoots()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle());
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        sim.Submit(Side.Blue, new SetFirePolicyOrder(blue.Id, FirePolicy.HoldFire));
        Assert.DoesNotContain(StepN(sim, 120), e => e is ShotFired);
    }

    [Fact]
    public void ReturnFire_WaitsUntilTheEnemyShoots()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0, spread: 60));
        var red = sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7, TestWeapons.Rifle(lethality: 0, spread: 60, aim: 40));
        sim.Submit(Side.Blue, new SetFirePolicyOrder(blue.Id, FirePolicy.ReturnFire));
        var shots = StepN(sim, 300).OfType<ShotFired>().ToList();
        var firstRed = shots.FindIndex(s => s.Shooter == red.Id);
        var firstBlue = shots.FindIndex(s => s.Shooter == blue.Id);
        Assert.True(firstRed >= 0);
        Assert.True(firstBlue > firstRed);
    }

    [Fact]
    public void EmptyMagazine_Reloads()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0, spread: 200, magazine: 2, reload: 30));
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        bool sawReload = false;
        for (int i = 0; i < 200 && !sawReload; i++)
        {
            sim.Step();
            sawReload = blue.Action == CombatAction.Reloading;
        }
        Assert.True(sawReload);
        Assert.Equal(0, blue.Ammo);
        for (int i = 0; i < 31; i++) sim.Step();
        Assert.Equal(2, blue.Ammo);
    }

    [Fact]
    public void MoveOrder_CancelsAiming()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(aim: 100));
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        for (int i = 0; i < 60 && blue.Action != CombatAction.Aiming; i++) sim.Step();
        Assert.Equal(CombatAction.Aiming, blue.Action);
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(50, 1850)));
        sim.Step();
        Assert.Equal(CombatAction.None, blue.Action);
        Assert.Null(blue.Target);
    }

    [Fact]
    public void FireAtOrder_PrefersTheOrderedTarget()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0));
        sim.SpawnUnit(Side.Red, new Vec2(1050, 1450), 7);
        var far = sim.SpawnUnit(Side.Red, new Vec2(3050, 1050), 7);
        sim.Submit(Side.Blue, new FireAtOrder(blue.Id, far.Id));
        var first = StepN(sim, 120).OfType<ShotFired>().First(s => s.Shooter == blue.Id);
        Assert.Equal(far.Id, first.Hit);
    }

    [Fact]
    public void TargetGoingDown_StopsTheBurst()
    {
        var sim = new Simulation(Open(), 1);
        var smg = new WeaponDef("burst", "Burst", WeaponClass.Smg, 71, 2, 10, 2, 4, 20, 0, 12_000, 100, 50, 10_000).Validated();
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, smg);
        var red = sim.SpawnUnit(Side.Red, new Vec2(1050, 1050), 7);
        var events = StepN(sim, 80);
        int downAt = events.FindIndex(e => e is UnitWounded { Level: >= WoundLevel.Incapacitated } w && w.Unit == red.Id);
        Assert.True(downAt >= 0);
        Assert.DoesNotContain(events.Skip(downAt + 1), e => e is ShotFired s && s.Shooter == blue.Id && s.Hit == red.Id);
    }

    [Fact]
    public void EnemyBehindHill_IsNotShot()
    {
        var map = Open();
        for (int y = 0; y < 20; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var sim = new Simulation(map, 1);
        sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle());
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        Assert.DoesNotContain(StepN(sim, 200), e => e is ShotFired);
    }

    [Fact]
    public void Gunfire_IsHeardByHiddenEnemies()
    {
        var map = Open();
        for (int y = 0; y < 20; y++) map[new CellCoord(30, y)] = new CellData(0, 1500, 255, 0, 0);
        var sim = new Simulation(map, 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0));
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        var listener = sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7);
        StepN(sim, 80);
        Assert.NotEqual(ContactLevel.Unknown, sim.Knowledge(Side.Red).LevelOf(blue.Id));
        Assert.True(listener.IsAlive);
    }

    [Fact]
    public void FriendInTheLineOfFire_HoldsFire()
    {
        var sim = new Simulation(Open(), 1);
        sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle());
        sim.SpawnUnit(Side.Blue, new Vec2(1050, 1080), 7);
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        Assert.DoesNotContain(StepN(sim, 150), e => e is ShotFired);
    }
}
