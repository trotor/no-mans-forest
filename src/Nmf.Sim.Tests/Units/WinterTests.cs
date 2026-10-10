using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Units;

/// <summary>Spec 2026-10-10-winter-design §2: deep snow, skis and the ahkio.</summary>
public class WinterTests
{
    private const int Second = SimConstants.TicksPerSecond;

    /// <summary>A 300 m field: open ground, a road along y 50..55 m, forest from y 100 m down.</summary>
    private static Simulation Field(int? snowCm, ulong seed = 1)
    {
        var map = new GridMap(300, 300, ["none", "road", "forest"]);
        for (int y = 0; y < 300; y++)
            for (int x = 0; x < 300; x++)
            {
                ref var cell = ref map[new CellCoord(x, y)];
                if (y is >= 50 and < 55)
                    cell.TerrainId = 1;
                else if (y >= 100)
                    cell = cell with { TerrainId = 2, ExtraMoveCost = 30 };
            }
        var sim = new Simulation(map, seed);
        if (snowCm is { } cm)
            sim.SetWinter(cm);
        return sim;
    }

    /// <summary>How far (cm) a man walks east along row <paramref name="yM"/> in 10 s.</summary>
    private static long Walked(Simulation sim, int yM, MoveMode mode = MoveMode.Walk, bool skis = false, Action<Unit>? setup = null)
    {
        var man = sim.SpawnUnit(Side.Blue, new Vec2(1_050, yM * 100 + 50), 8, TestWeapons.Rifle());
        man.FirePolicy = FirePolicy.HoldFire;
        man.OnSkis = skis;
        setup?.Invoke(man);
        sim.Submit(Side.Blue, new MoveOrder(man.Id, new Vec2(29_050, yM * 100 + 50), mode));
        // Let him get to his stance first: the clock starts when he is on his way.
        for (int i = 0; i < 3 * Second && !man.IsMoving; i++)
            sim.Step();
        long start = man.Position.X;
        for (int i = 0; i < 10 * Second; i++)
            sim.Step();
        return man.Position.X - start;
    }

    [Fact]
    public void WinterState_FootFactor()
    {
        Assert.Equal(150, Field(40).Winter!.FootPct);
        Assert.Equal(250, Field(150).Winter!.FootPct); // capped
        Assert.Null(Field(null).Winter);
    }

    [Fact]
    public void Snow_SlowsAManOnFoot_ByItsFactor()
    {
        long summer = Walked(Field(null), 20);
        long winter = Walked(Field(40), 20);
        Assert.InRange(winter * 150, summer * 100 - 1_500, summer * 100 + 1_500); // two thirds the way
    }

    [Fact]
    public void Road_Trodden_NoSlower()
    {
        Assert.Equal(Walked(Field(null), 52), Walked(Field(40), 52));
    }

    [Fact]
    public void Skis_Faster_ButNotSneaking()
    {
        long summer = Walked(Field(null), 20);
        long skis = Walked(Field(40), 20, skis: true);
        Assert.InRange(skis * 70, summer * 100 - 1_500, summer * 100 + 1_500); // faster than summer on foot
        long forestSkis = Walked(Field(40), 150, skis: true);
        // The forest's 130 % times 85 % (against the open summer pace: summer forest steps are rounded down to whole cm).
        Assert.InRange(forestSkis * 130 * 85, summer * 10_000 - 150_000, summer * 10_000 + 150_000);
        Assert.Equal(Walked(Field(40), 20, MoveMode.Sneak), Walked(Field(40), 20, MoveMode.Sneak, skis: true)); // skis are no help creeping
    }

    [Fact]
    public void Skis_InSummer_NoHelp()
    {
        Assert.Equal(Walked(Field(null), 20), Walked(Field(null), 20, skis: true));
    }

    [Fact]
    public void Ahkio_FasterThanCarrying_OnlyInWinter()
    {
        static long Pulling(int? snow, bool ahkio)
        {
            var sim = Field(snow);
            var wounded = sim.SpawnUnit(Side.Blue, new Vec2(1_050, 2_150), 8, TestWeapons.Rifle());
            wounded.Wound = WoundLevel.Incapacitated;
            return Walked(sim, 20, setup: man =>
            {
                man.HasAhkio = ahkio;
                sim.Submit(Side.Blue, new CarryOrder(man.Id, wounded.Id));
                for (int i = 0; i < 10 * Second && man.Carrying is null; i++)
                    sim.Step();
                Assert.NotNull(man.Carrying);
            });
        }
        long carried = Pulling(40, ahkio: false);
        long sled = Pulling(40, ahkio: true);
        Assert.InRange(sled * CombatRules.CarryWalkPct, carried * CombatRules.AhkioWalkPct - 2_000, carried * CombatRules.AhkioWalkPct + 2_000);
        Assert.Equal(Pulling(null, ahkio: false), Pulling(null, ahkio: true)); // no sled without snow
    }

    [Fact]
    public void SameSeed_SameWinter()
    {
        static ulong Hash()
        {
            var sim = Field(40, seed: 3);
            Walked(sim, 20, skis: true);
            return StateHash.Compute(sim);
        }
        Assert.Equal(Hash(), Hash());
        var a = Field(40);
        var b = Field(30);
        Assert.NotEqual(StateHash.Compute(a), StateHash.Compute(b));
    }
}
