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
    public void BulletStoppingInCoverRightInFrontOfHim_IsANearMiss()
    {
        var map = new GridMap(60, 20, ["none"]);
        map[new CellCoord(19, 10)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
        var (sim, shooter, target) = Setup(map);
        target.Stance = Stance.Prone;
        var shot = Ballistics.Trace(sim, shooter, target);
        Assert.Null(shot.Hit);
        var miss = Assert.Single(shot.NearMisses);
        Assert.Same(target, miss.Unit);
        Assert.True(miss.DistanceCm < CombatRules.NearMissRadiusCm);
    }

    [Fact]
    public void BulletStoppingFarShortOfHim_IsNoNearMiss()
    {
        var map = new GridMap(60, 20, ["none"]);
        for (int y = 0; y < 20; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var (sim, shooter, target) = Setup(map);
        Assert.Empty(Ballistics.Trace(sim, shooter, target).NearMisses);
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

    [Fact]
    public void EffectiveSpread_KeepsSubMilliradianPrecision()
    {
        Assert.Equal(6000, CombatRules.EffectiveSpreadMicroRad(6, Stance.Prone, 0)); // lying down: no better than standing
        Assert.Equal(4800, CombatRules.EffectiveSpreadMicroRad(6, Stance.Crouching, 0));
        Assert.Equal(12000, CombatRules.EffectiveSpreadMicroRad(6, Stance.Standing, 500));
    }

    [Fact]
    public void AimedAtMan_IsSuppressedByABulletStrikingFourMetresShort()
    {
        var map = new GridMap(60, 20, ["none"]);
        map[new CellCoord(16, 10)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
        var (sim, shooter, target) = Setup(map);
        target.Stance = Stance.Prone;
        var shot = Ballistics.Trace(sim, shooter, target);
        var miss = Assert.Single(shot.NearMisses);
        Assert.Same(target, miss.Unit);
        Assert.InRange(miss.DistanceCm, CombatRules.NearMissRadiusCm, CombatRules.AimedMissRadiusCm);
        Assert.Equal(CombatRules.AimedMissRadiusCm, miss.RadiusCm);
    }

    [Fact]
    public void Bystander_FourMetresFromTheFlight_IsNotSuppressed()
    {
        var (sim, shooter, target) = Setup();
        sim.SpawnUnit(Side.Red, new Vec2(1050, 1450), 7);
        Assert.Empty(Ballistics.Trace(sim, shooter, target).NearMisses);
    }

    [Fact]
    public void FiringOnTheMove_WidensTheSpread()
    {
        Assert.Equal(2 * CombatRules.EffectiveSpreadMicroRad(6, Stance.Standing, 0),
            CombatRules.EffectiveSpreadMicroRad(6, Stance.Standing, 0, CombatRules.WalkingFireSpreadPct));
        Assert.Equal(3 * CombatRules.EffectiveSpreadMicroRad(6, Stance.Standing, 0),
            CombatRules.EffectiveSpreadMicroRad(6, Stance.Standing, 0, CombatRules.RunningFireSpreadPct));
    }

    [Fact]
    public void MovingTarget_IsHarderToHit_RunningMostOfAll()
    {
        int Misses(MoveMode? mode, Stance stance = Stance.Standing)
        {
            int misses = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var (sim, shooter, target) = Setup(spread: 20, seed: (ulong)seed);
                target.Position = new Vec2(4050, 1050); // 40 m
                target.Stance = stance;
                if (mode is { } m)
                {
                    target.MoveTarget = new Vec2(4050, 1850);
                    target.MoveMode = m;
                }
                if (Ballistics.Trace(sim, shooter, target).Hit is null) misses++;
            }
            return misses;
        }
        int still = Misses(null), walking = Misses(MoveMode.Walk), running = Misses(MoveMode.Run);
        Assert.True(still < walking, $"still {still}, walking {walking}");
        Assert.True(walking < running, $"walking {walking}, running {running}");
        Assert.Equal(Misses(null, Stance.Prone), Misses(MoveMode.Walk, Stance.Prone)); // a crawling man is no harder
    }

    [Fact]
    public void TargetMovement_SpreadFactors()
    {
        var (_, _, target) = Setup();
        Assert.Equal(100, CombatRules.TargetMovingSpreadPct(target));
        target.MoveTarget = new Vec2(4050, 1850);
        target.MoveMode = MoveMode.Run;
        Assert.Equal(CombatRules.RunningTargetSpreadPct, CombatRules.TargetMovingSpreadPct(target));
        Assert.Equal(200, CombatRules.RunningTargetSpreadPct);
        target.MoveMode = MoveMode.Walk;
        Assert.Equal(CombatRules.WalkingTargetSpreadPct, CombatRules.TargetMovingSpreadPct(target));
        target.Stance = Stance.Prone;
        Assert.Equal(100, CombatRules.TargetMovingSpreadPct(target));
    }

    [Fact]
    public void AFallenTree_StopsRoundsLow_ButNotAtAStandingMansChest()
    {
        int Hits(Stance stance)
        {
            int hits = 0;
            for (ulong seed = 0; seed < 200; seed++)
            {
                var map = new GridMap(60, 20, ["none"]);
                map[new CellCoord(19, 10)] = new CellData(0, 1500, 8, 26, 0) with { LowCover = 230, LowCoverHeightCm = 50 };
                var (sim, shooter, target) = Setup(map, seed: seed);
                target.Stance = stance;
                if (Ballistics.Trace(sim, shooter, target).Hit == target) hits++;
            }
            return hits;
        }
        int prone = Hits(Stance.Prone), standing = Hits(Stance.Standing);
        Assert.True(prone < 60, $"prone behind the log hit {prone} times of 200");
        Assert.True(standing > 150, $"standing behind the log hit only {standing} times of 200");
    }

    [Fact]
    public void Marksmanship_ScalesTheSpread()
    {
        int Misses(int marksmanship)
        {
            int misses = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var (sim, shooter, target) = Setup(spread: 40, seed: (ulong)seed);
                target.Position = new Vec2(4050, 1050); // 40 m
                shooter.Marksmanship = marksmanship;
                if (Ballistics.Trace(sim, shooter, target).Hit is null) misses++;
            }
            return misses;
        }
        Assert.True(Misses(90) < Misses(50));
        Assert.True(Misses(50) < Misses(20));
    }
}
