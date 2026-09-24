using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class BallisticsTests
{
    private static readonly Vec2 ShooterPos = new(50, 1050);  // cell (0,10)
    private static readonly Vec2 TargetPos = new(2050, 1050); // cell (20,10), 20 m east

    private static (Simulation Sim, Unit Shooter, Unit Target) Setup(GridMap? map = null, int spread = 0, ulong seed = 3)
    {
        var sim = new Simulation(map ?? new GridMap(60, 20, ["none"]), seed);
        var shooter = sim.SpawnUnit(Side.Blue, ShooterPos, 7, TestWeapons.Rifle(spread: spread));
        var target = sim.SpawnUnit(Side.Red, TargetPos, 7);
        return (sim, shooter, target);
    }

    [Fact]
    public void ZeroSpread_HitsStandingTargetInOpen()
    {
        var (sim, shooter, target) = Setup();
        var shot = Ballistics.Trace(sim, shooter, target);
        Assert.Same(target, shot.Hit);
        Assert.Equal(TargetPos, shot.End);
    }

    [Fact]
    public void HillBetween_StopsTheBullet()
    {
        var map = new GridMap(60, 20, ["none"]);
        for (int y = 0; y < 20; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var (sim, shooter, target) = Setup(map);
        var shot = Ballistics.Trace(sim, shooter, target);
        Assert.Null(shot.Hit);
        Assert.Equal(1050, shot.End.X);
    }

    [Fact]
    public void RockWithFullCover_ProtectsProneTargetBehindIt()
    {
        var map = new GridMap(60, 20, ["none"]);
        map[new CellCoord(19, 10)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
        var (sim, shooter, target) = Setup(map);
        target.Stance = Stance.Prone;
        Assert.Null(Ballistics.Trace(sim, shooter, target).Hit);
    }

    [Fact]
    public void BushWithoutCover_DoesNotStopTheBullet()
    {
        var map = new GridMap(60, 20, ["none"]);
        map[new CellCoord(19, 10)] = new CellData(0, 80, 153, 0, 0);
        var (sim, shooter, target) = Setup(map);
        target.Stance = Stance.Prone;
        Assert.Same(target, Ballistics.Trace(sim, shooter, target).Hit);
    }

    [Fact]
    public void FriendInTheLine_TakesTheBullet()
    {
        var (sim, shooter, target) = Setup();
        var friend = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        Assert.Same(friend, Ballistics.Trace(sim, shooter, target).Hit);
    }

    [Fact]
    public void DeadMenInTheLine_AreIgnored()
    {
        var (sim, shooter, target) = Setup();
        var corpse = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        corpse.Wound = WoundLevel.Dead;
        Assert.Same(target, Ballistics.Trace(sim, shooter, target).Hit);
    }

    [Fact]
    public void NearMisses_ListOnlyEnemiesBesideTheFlight()
    {
        var (sim, shooter, target) = Setup();
        var enemyBeside = sim.SpawnUnit(Side.Red, new Vec2(1050, 1200), 7);
        sim.SpawnUnit(Side.Blue, new Vec2(1050, 900), 7);
        var shot = Ballistics.Trace(sim, shooter, target);
        var miss = Assert.Single(shot.NearMisses);
        Assert.Same(enemyBeside, miss.Unit);
        Assert.Equal(150, miss.DistanceCm);
    }

    [Fact]
    public void LargerSpread_MissesMoreOften()
    {
        int Hits(int spread)
        {
            var (sim, shooter, target) = Setup(spread: spread);
            int hits = 0;
            for (int i = 0; i < 300; i++)
                if (Ballistics.Trace(sim, shooter, target).Hit == target) hits++;
            return hits;
        }
        int tight = Hits(4), wide = Hits(80);
        Assert.Equal(300, tight);
        Assert.InRange(wide, 1, 150);
    }

    [Fact]
    public void SameSeed_GivesSameShots()
    {
        var (a, sa, ta) = Setup(spread: 40, seed: 11);
        var (b, sb, tb) = Setup(spread: 40, seed: 11);
        for (int i = 0; i < 50; i++)
            Assert.Equal(Ballistics.Trace(a, sa, ta).End, Ballistics.Trace(b, sb, tb).End);
    }
}
