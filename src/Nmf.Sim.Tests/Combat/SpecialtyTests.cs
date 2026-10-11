using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

/// <summary>Spec 2026-10-11-campaign-design §9b: the marksman, the scout and the sapper.</summary>
public class SpecialtyTests
{
    private const int Second = SimConstants.TicksPerSecond;

    private static GrenadeDef Grenade() => new GrenadeDef("test_grenade", "Test grenade", 80, 3_000, 20, 1_000, 400, 600, 45).Validated();

    private static Unit Man(Simulation sim, Specialty specialty, WeaponDef? weapon = null, GrenadeDef? grenade = null, Side side = Side.Blue, int x = 50)
    {
        var unit = sim.SpawnUnit(side, new Vec2(x, 1050), 7, weapon ?? TestWeapons.Rifle(), false, grenade);
        sim.SetSpecialty(unit, specialty);
        return unit;
    }

    [Fact]
    public void Marksman_RifleSpread_Is70Percent_OtherwiseUnchanged()
    {
        var sim = new Simulation(new GridMap(20, 20, ["none"]), 1);
        Assert.Equal(70, CombatRules.SpecialtySpreadPct(Man(sim, Specialty.Marksman)));
        Assert.Equal(100, CombatRules.SpecialtySpreadPct(Man(sim, Specialty.Marksman, TestWeapons.Smg())));
        Assert.Equal(100, CombatRules.SpecialtySpreadPct(Man(sim, Specialty.None)));
        Assert.Equal(100, CombatRules.SpecialtySpreadPct(Man(sim, Specialty.Scout)));
    }

    [Fact]
    public void Marksman_HitsMoreOften_AtLongRange()
    {
        int Hits(Specialty specialty)
        {
            var sim = new Simulation(new GridMap(260, 20, ["none"]), 5);
            var shooter = Man(sim, specialty, TestWeapons.Rifle(spread: 3));
            var target = sim.SpawnUnit(Side.Red, new Vec2(25_050, 1050), 7);
            int hits = 0;
            for (int i = 0; i < 400; i++)
                if (Ballistics.Trace(sim, shooter, target).Hit == target)
                    hits++;
            return hits;
        }
        int plain = Hits(Specialty.None), marksman = Hits(Specialty.Marksman);
        Assert.True(marksman > plain * 11 / 10, $"marksman {marksman} hits, plain {plain}");
    }

    [Fact]
    public void Scout_SeesSooner()
    {
        var sim = new Simulation(new GridMap(20, 20, ["none"]), 1);
        Assert.Equal(120, VisionRules.ObserverPct(Man(sim, Specialty.Scout)));
        Assert.Equal(100, VisionRules.ObserverPct(Man(sim, Specialty.Sapper)));
    }

    [Theory]
    [InlineData(MoveMode.Walk, 140)]
    [InlineData(MoveMode.Run, 210)]
    [InlineData(MoveMode.Sneak, 105)]
    [InlineData(MoveMode.Crawl, 100)]
    public void Scout_Moving_IsHarderToMakeOut_NeverBelowStandingStill(MoveMode mode, int pct)
    {
        var sim = new Simulation(new GridMap(20, 20, ["none"]), 1);
        var scout = Man(sim, Specialty.Scout);
        scout.IsMoving = true;
        scout.MoveMode = mode;
        Assert.Equal(pct, VisionRules.VisibilityPct(scout, 1_000));
        scout.LastShotTick = 1_000;
        Assert.Equal(CombatRules.FiringVisibilityPct, VisionRules.VisibilityPct(scout, 1_000)); // firing gives him away all the same
    }

    [Fact]
    public void Scout_MovesQuieter()
    {
        var sim = new Simulation(new GridMap(20, 20, ["none"]), 1);
        var scout = Man(sim, Specialty.Scout);
        var plain = Man(sim, Specialty.None);
        foreach (var mode in new[] { MoveMode.Walk, MoveMode.Run, MoveMode.Sneak, MoveMode.Crawl })
        {
            scout.MoveMode = plain.MoveMode = mode;
            Assert.Equal(VisionRules.NoiseRadiusCm(mode), VisionRules.NoiseRadiusCm(plain));
            Assert.Equal(VisionRules.NoiseRadiusCm(mode) * 70 / 100, VisionRules.NoiseRadiusCm(scout));
        }
    }

    [Fact]
    public void Scout_WalkingPast_IsHeardFromCloser()
    {
        // A walking man is heard out to 30 m; a scout to 21 m. Pass at 25 m behind a wall that blocks sight.
        bool Heard(Specialty specialty)
        {
            var sim = new Simulation(new GridMap(100, 100, ["none"]), 1);
            for (int x = 0; x < 100; x++)
            {
                ref var cell = ref sim.Map[new CellCoord(x, 40)];
                cell.ObstacleHeightCm = 1_500;
                cell.ConcealmentPerM = 255;
            }
            var listener = sim.SpawnUnit(Side.Red, new Vec2(5_050, 2_550), 7, TestWeapons.Rifle());
            var walker = sim.SpawnUnit(Side.Blue, new Vec2(1_050, 5_050), 7, TestWeapons.Rifle());
            sim.SetSpecialty(walker, specialty);
            foreach (var u in sim.Units)
                u.FirePolicy = FirePolicy.HoldFire;
            sim.Submit(Side.Blue, new MoveOrder(walker.Id, new Vec2(9_050, 5_050), MoveMode.Walk));
            for (int i = 0; i < 60 * Second; i++)
            {
                sim.Step();
                if (sim.Knowledge(Side.Red).LevelOf(walker.Id) != ContactLevel.Unknown)
                    return true;
            }
            return false;
        }
        Assert.True(Heard(Specialty.None));
        Assert.False(Heard(Specialty.Scout));
    }

    [Fact]
    public void Sapper_CarriesTwoMoreGrenades_WhenHeHasAny()
    {
        var sim = new Simulation(new GridMap(20, 20, ["none"]), 1);
        var sapper = Man(sim, Specialty.Sapper, grenade: Grenade());
        Assert.Equal(CombatRules.GrenadesPerSoldier + 2, sapper.Grenades);
        Assert.Equal(CombatRules.MaxGrenades + 2, CombatRules.MaxGrenadesFor(sapper));
        Assert.Equal(0, Man(sim, Specialty.Sapper).Grenades);
        Assert.Equal(CombatRules.MaxGrenades, CombatRules.MaxGrenadesFor(Man(sim, Specialty.Marksman, grenade: Grenade())));
    }

    [Fact]
    public void Sapper_Searching_FillsUpToHisLargerLoad()
    {
        var sim = new Simulation(new GridMap(20, 20, ["none"]), 1);
        var sapper = Man(sim, Specialty.Sapper, grenade: Grenade());
        var body = sim.SpawnUnit(Side.Red, new Vec2(150, 1050), 7, TestWeapons.Rifle(), false, Grenade());
        body.Grenades = 4;
        body.Wound = WoundLevel.Dead;
        LootSystem.Transfer(sapper, body, 1, []);
        Assert.Equal(CombatRules.MaxGrenades + 2, sapper.Grenades);
        Assert.Equal(2, body.Grenades);
    }

    [Fact]
    public void Sapper_ThrowsWithHalfTheScatter()
    {
        var sim = new Simulation(new GridMap(20, 20, ["none"]), 1);
        var def = Grenade();
        Assert.Equal(1_000 * def.ScatterPct / 100, GrenadeSystem.ScatterCm(Man(sim, Specialty.None, grenade: def), def, 1_000));
        Assert.Equal(1_000 * def.ScatterPct / 100 / 2, GrenadeSystem.ScatterCm(Man(sim, Specialty.Sapper, grenade: def), def, 1_000));
    }

    [Fact]
    public void Sapper_CutsTheLine_InFiveSeconds()
    {
        long CutTime(Specialty specialty)
        {
            var sim = new Simulation(new GridMap(100, 100, ["none"]), 1);
            var man = Man(sim, specialty, x: 5_050);
            man.FirePolicy = FirePolicy.HoldFire;
            sim.AddFieldPhone(new FieldPhone("hq", Side.Red, new Vec2(5_050, 9_050), [new Vec2(5_050, 9_050), new Vec2(5_050, 50)]));
            sim.Submit(Side.Blue, new CutLineOrder(man.Id, "hq", new Vec2(5_050, 1_050)));
            long start = sim.Tick;
            for (int i = 0; i < 30 * Second; i++)
                if (sim.Step().OfType<LineCut>().Any())
                    return sim.Tick - start;
            return -1;
        }
        long plain = CutTime(Specialty.None), sapper = CutTime(Specialty.Sapper);
        Assert.InRange(plain - sapper, 5 * Second - 2, 5 * Second + 2);
        Assert.Equal(5 * Second, PhoneSystem.CutTicksFor(new Simulation(new GridMap(20, 20, ["none"]), 1) is var s ? Man(s, Specialty.Sapper) : null!));
    }

    [Fact]
    public void Specialty_IsInTheStateHash()
    {
        var a = new Simulation(new GridMap(20, 20, ["none"]), 1);
        var b = new Simulation(new GridMap(20, 20, ["none"]), 1);
        Man(a, Specialty.Medic);
        Man(b, Specialty.Scout);
        Assert.NotEqual(StateHash.Compute(a), StateHash.Compute(b));
    }
}
