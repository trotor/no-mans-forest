using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.World;

/// <summary>Spec 2026-09-26-foxholes-squad-control-design §1: a foxhole is a cell dug a metre deep, the spoil round it.</summary>
public class FoxholeTests
{
    private static readonly CellCoord Hole = new(40, 20);

    private static GridMap Map(bool dig = true)
    {
        var map = new GridMap(60, 40, ["none"]);
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 60; x++)
                map[new CellCoord(x, y)].GroundHeightCm = 1000;
        if (dig)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    map[new CellCoord(Hole.X + dx, Hole.Y + dy)].GroundHeightCm = 1025;
            map[Hole].GroundHeightCm = 900;
        }
        return map;
    }

    [Fact]
    public void AFoxhole_IsKnownByItsDepth_AndIsTheBestCover()
    {
        var map = Map();
        Assert.Equal(125, CoverFinder.PitDepthCm(map, Hole));
        Assert.Equal(0, CoverFinder.PitDepthCm(map, new CellCoord(10, 10)));
        Assert.Equal(CoverFinder.PitCover, CoverFinder.CoveredAt(map, Hole, new CellCoord(5, 20).CenterCm));
        Assert.Equal(CoverFinder.PitCover, CoverFinder.CoveredAt(map, Hole, null));
    }

    [Fact]
    public void StandingInAFoxhole_HeIsHitFarLessOften()
    {
        int Hits(bool dig)
        {
            int hits = 0;
            for (ulong seed = 0; seed < 200; seed++)
            {
                var sim = new Simulation(Map(dig), seed);
                var shooter = sim.SpawnUnit(Side.Blue, new CellCoord(5, 20).CenterCm, 8, TestWeapons.Rifle(spread: 10));
                var target = sim.SpawnUnit(Side.Red, Hole.CenterCm, 8);
                if (Ballistics.Trace(sim, shooter, target).Hit == target) hits++;
            }
            return hits;
        }
        int open = Hits(false), dug = Hits(true);
        Assert.True(dug * 3 < open, $"in the open {open}, in the foxhole {dug}");
    }

    [Fact]
    public void Crouching_HeIsOutOfSight_Standing_HeSeesOverTheRim()
    {
        var map = Map();
        var from = new CellCoord(5, 20).CenterCm;
        int eye = 1000 + StanceRules.EyeHeightCm(Stance.Standing);
        Assert.Equal(0, LineOfSight.Clarity(map, from, eye, Hole.CenterCm, 900 + StanceRules.HeightCm(Stance.Crouching)));
        Assert.True(LineOfSight.Clarity(map, Hole.CenterCm, 900 + StanceRules.EyeHeightCm(Stance.Standing), from, 1000 + 100) > 0);
    }

    [Fact]
    public void UnderFire_HeStaysInHisFoxhole_AndAManNearbyRunsIntoAnEmptyOne()
    {
        var sim = new Simulation(Map(), 1);
        var shooter = sim.SpawnUnit(Side.Blue, new CellCoord(5, 20).CenterCm, 8, TestWeapons.Rifle(spread: 60, lethality: 0));
        var dug = sim.SpawnUnit(Side.Red, Hole.CenterCm, 8);
        var empty = new CellCoord(40, 28);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                sim.Map[new CellCoord(empty.X + dx, empty.Y + dy)].GroundHeightCm = 1025;
        sim.Map[empty].GroundHeightCm = 900;
        var near = sim.SpawnUnit(Side.Red, new CellCoord(43, 28).CenterCm, 8);
        sim.Submit(Side.Blue, new FireAtOrder(shooter.Id, near.Id));
        for (int i = 0; i < 200; i++) sim.Step();
        Assert.Equal(Hole, dug.Position.ToCell());
        Assert.Equal(empty, near.Position.ToCell());
    }

    [Fact]
    public void UnderFire_HeKeepsFiringOverTheRim_AndDucksOnlyWhenPinned()
    {
        var sim = new Simulation(Map(), 1);
        var man = sim.SpawnUnit(Side.Red, Hole.CenterCm, 8, TestWeapons.Rifle());
        man.Suppression = MoraleSystem.GoProneAt(man) + 10; // heavy fire, but he holds
        for (int i = 0; i < 10; i++) sim.Step();
        Assert.Equal(Stance.Standing, man.Stance);
        Assert.Null(man.TargetStance);
    }

    [Fact]
    public void WhenTheFireDiesDown_HeStandsUpToFire_EvenAVeteranOrAGunner()
    {
        foreach (var experience in new[] { 50, 90 })
        {
            var sim = new Simulation(Map(), 1);
            var enemy = sim.SpawnUnit(Side.Blue, new CellCoord(5, 20).CenterCm, 8);
            var man = sim.SpawnUnit(Side.Red, Hole.CenterCm, 8, TestWeapons.Rifle());
            man.Experience = experience;
            man.Stance = Stance.Prone; // ducked at the bottom under a burst
            var known = sim.Knowledge(Side.Red).GetOrAdd(enemy.Id);
            known.Level = ContactLevel.LastKnown;
            known.Position = enemy.Position;
            for (int i = 0; i < 80; i++)
            {
                known.LastUpdateTick = sim.Tick;
                sim.Step();
            }
            Assert.Equal(Stance.Standing, man.Stance);
        }
    }
}
