using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

/// <summary>Spec 2026-09-26-squads-area-fire-design §3: fire at a place where the enemy is believed to be.</summary>
public class AreaFireTests
{
    private static readonly Vec2 Point = new(4050, 1050); // 40 m east of the shooter

    /// <summary>A Finn on open ground; a Soviet hidden in a thicket round <see cref="Point"/>.</summary>
    private static (Simulation Sim, Unit Shooter, Unit Hidden) Setup(int magazines = 4)
    {
        var map = new GridMap(80, 40, ["none"]);
        for (int y = 0; y < 40; y++)
            for (int x = 36; x < 46; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 255, 26, 0);
        var sim = new Simulation(map, 3);
        var shooter = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 8, TestWeapons.Rifle(spread: 4, lethality: 0));
        shooter.Magazines = magazines;
        var hidden = sim.SpawnUnit(Side.Red, Point + new Vec2(300, 0), 8);
        return (sim, shooter, hidden);
    }

    private static List<SimEvent> Run(Simulation sim, int ticks)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < ticks; i++)
            events.AddRange(sim.Step());
        return events;
    }

    [Fact]
    public void Ordered_HeFiresAtThePlace_AndKeepsHisHeadsDown()
    {
        var (sim, shooter, hidden) = Setup();
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        var events = Run(sim, 200);
        Assert.True(events.OfType<ShotFired>().Count(s => s.Shooter == shooter.Id) >= 3);
        Assert.True(hidden.Suppression > 0 || hidden.LastSuppressedTick > 0, "the man hidden by the place was not suppressed");
        Assert.Equal(Point, shooter.AreaTarget);
    }

    [Fact]
    public void AMan15MetresAway_IsNotBothered()
    {
        var (sim, shooter, _) = Setup();
        var away = sim.SpawnUnit(Side.Red, Point + new Vec2(0, 1500), 8); // 15 m to the side of the place and the line
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        Run(sim, 200);
        Assert.Equal(long.MinValue / 2, away.LastSuppressedTick);
    }

    [Fact]
    public void HoldFire_DoesNotStopAnOrderedAreaFire()
    {
        var (sim, shooter, _) = Setup();
        sim.Submit(Side.Blue, new SetFirePolicyOrder(shooter.Id, FirePolicy.HoldFire));
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        Assert.NotEmpty(Run(sim, 200).OfType<ShotFired>());
    }

    [Fact]
    public void HeKeepsHisLastMagazine_ForHimself()
    {
        var (sim, shooter, _) = Setup(magazines: 1);
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        Run(sim, 600);
        Assert.Null(shooter.AreaTarget);
        Assert.Equal(0, shooter.Magazines);
        Assert.Equal(shooter.Weapon!.MagazineSize, shooter.Ammo);
    }

    [Fact]
    public void Refused_OutOfRange_OrWithNoSpareMagazine()
    {
        var (sim, shooter, _) = Setup(magazines: 0);
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        Assert.Contains(sim.Step(), e => e is OrderRejected { Reason: "no spare magazines" });
        var (sim2, shooter2, _) = Setup();
        shooter2.Weapon = new WeaponDef("pistol", "Pistol", WeaponClass.Rifle, 8, 4, 1, 0, 2, 10, 20, 2000, 50, 40, 10_000); // 20 m
        sim2.Submit(Side.Blue, new AreaFireOrder(shooter2.Id, Point));
        Assert.Contains(sim2.Step(), e => e is OrderRejected { Reason: "out of range" });
    }

    [Fact]
    public void AnotherOrder_EndsIt_ButLyingDownDoesNot()
    {
        var (sim, shooter, _) = Setup();
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        sim.Submit(Side.Blue, new SetStanceOrder(shooter.Id, Stance.Prone));
        sim.Step();
        Assert.Equal(Point, shooter.AreaTarget);
        sim.Submit(Side.Blue, new MoveOrder(shooter.Id, new Vec2(1050, 1050)));
        sim.Step();
        Assert.Null(shooter.AreaTarget);
    }

    [Fact]
    public void AnEnemyCloseInSight_IsShotFirst()
    {
        var (sim, shooter, _) = Setup();
        var close = sim.SpawnUnit(Side.Red, new Vec2(2050, 1850), 8); // 20 m, in the open
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        bool shotAtHim = false;
        for (int i = 0; i < 120 && !shotAtHim; i++)
        {
            sim.Step();
            shotAtHim = shooter.Target == close.Id;
        }
        Assert.True(shotAtHim, "he never turned on the close enemy");
        Assert.Equal(Point, shooter.AreaTarget); // and back to it afterwards
    }

    [Fact]
    public void AFriendInTheWay_HoldsHisFire()
    {
        var (sim, shooter, _) = Setup();
        sim.SpawnUnit(Side.Blue, new Vec2(2050, 1050), 8); // right on the line
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        Assert.Empty(Run(sim, 200).OfType<ShotFired>());
    }

    [Fact]
    public void WithAnEmptyGun_HeReloadsFirst_AndNeverFiresRoundsHeHasNot()
    {
        var (sim, shooter, _) = Setup(magazines: 3);
        shooter.Ammo = 0; // a reload cut short
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        for (int i = 0; i < 300; i++)
        {
            sim.Step();
            Assert.True(shooter.Ammo >= 0, $"ammo {shooter.Ammo}");
        }
        Assert.True(shooter.Magazines < 3);
    }

    [Fact]
    public void AFriendAtOrJustBeyondThePlace_HoldsHisFire()
    {
        foreach (var beyond in new[] { 50, 800 })
        {
            var (sim, shooter, _) = Setup();
            sim.SpawnUnit(Side.Blue, Point + new Vec2(beyond, 0), 8);
            sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
            Assert.Empty(Run(sim, 200).OfType<ShotFired>());
        }
    }

    [Fact]
    public void RoundsStoppedByABankInFront_DoNotBotherMenFarBehindIt()
    {
        var (sim, shooter, hidden) = Setup();
        for (int y = 0; y < 40; y++)
            sim.Map[new CellCoord(3, y)].GroundHeightCm = 1000; // a bank 3 m in front of him
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        Assert.NotEmpty(Run(sim, 200).OfType<ShotFired>());
        Assert.Equal(long.MinValue / 2, hidden.LastSuppressedTick);
    }

    [Fact]
    public void APlaceOffTheMap_IsRefused()
    {
        var (sim, shooter, _) = Setup();
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, new Vec2(4050, -500)));
        Assert.Contains(sim.Step(), e => e is OrderRejected { Reason: "target outside map" });
    }

    [Fact]
    public void ABrokenMan_ForgetsHisAreaFire()
    {
        var (sim, shooter, _) = Setup();
        sim.Submit(Side.Blue, new AreaFireOrder(shooter.Id, Point));
        sim.Step();
        shooter.Morale = 0;
        MoraleSystem.Check(sim, shooter, sim.Tick, []);
        Run(sim, 10);
        Assert.Null(shooter.AreaTarget);
    }
}
