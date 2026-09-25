using Nmf.Sim.World;
using Nmf.Sim.Vision;
using Nmf.Sim.Units;
using Nmf.Sim.Orders;
using Nmf.Content.Tiled;
using Nmf.Content.Weapons;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Scenarios;

namespace Nmf.Content.Tests;

/// <summary>The real skirmish map and weapons fight for two minutes without errors, deterministically.</summary>
public class SkirmishFightTests
{
    [Fact]
    public void TwoMinuteFight_IsDeterministicAndHasShots()
    {
        var root = CoreContentTests.RepoRoot();
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", "skirmish.tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));

        (ulong Hash, int Shots) Run()
        {
            var scenario = SkirmishScenario.Create(map, 1942, weapons);
            int shots = 0;
            for (int i = 0; i < 2400; i++)
            {
                scenario.Tick();
                shots += scenario.Sim.Step().Count(e => e is ShotFired);
            }
            return (StateHash.Compute(scenario.Sim), shots);
        }

        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a.Shots > 0);
    }

    [Fact]
    public void ThreeMinuteFightWithGrenadesAndAnAdvance_IsDeterministic()
    {
        var root = CoreContentTests.RepoRoot();
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", "skirmish.tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));

        (ulong Hash, int Shots, int Grenades) Run()
        {
            var scenario = SkirmishScenario.Create(map, 1942, weapons, grenades);
            int shots = 0, thrown = 0;
            for (int i = 0; i < 3600; i++)
            {
                if (i == 40)
                {
                    // Like a player would: send the Finns forward at the Soviet position.
                    var redLeader = scenario.Sim.Units.First(u => u.Side == Nmf.Sim.Units.Side.Red);
                    foreach (var blue in scenario.Sim.Units.Where(u => u.Side == Nmf.Sim.Units.Side.Blue))
                        scenario.Sim.Submit(Nmf.Sim.Units.Side.Blue, new Nmf.Sim.Orders.MoveOrder(blue.Id, redLeader.Position, Nmf.Sim.Units.MoveMode.Auto));
                }
                scenario.Tick();
                foreach (var e in scenario.Sim.Step())
                {
                    if (e is ShotFired) shots++;
                    if (e is GrenadeThrown) thrown++;
                }
            }
            return (StateHash.Compute(scenario.Sim), shots, thrown);
        }

        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a.Shots > 0);
        var loaded = SkirmishScenario.Create(map, 1942, weapons, grenades).Sim.Units;
        Assert.All(loaded, u => Assert.Equal(Nmf.Sim.Combat.CombatRules.GrenadesPerSoldier, u.Grenades));
    }

    [Fact]
    public void WholeSquadAssaultFromCloseIn_IsDeterministicAndSparesOwnMen()
    {
        var root = CoreContentTests.RepoRoot();
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", "skirmish.tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));
        const Side Blue = Side.Blue;

        (ulong Hash, int CloseCombat, int OwnMenInOwnBlast) Run()
        {
            var scenario = SkirmishScenario.Create(map, 1942, weapons, grenades);
            var sim = scenario.Sim;
            var target = sim.Units.First(u => u.Side == Side.Red);
            // The Finns have crept up to 25 m south of the Soviet leader.
            var blues = sim.Units.Where(u => u.Side == Blue).ToList();
            for (int i = 0; i < blues.Count; i++)
                blues[i].Position = PassableNear(map, target.Position + new Vec2((i - 1) * 300, 2500));
            int closeCombat = 0, ownMenInOwnBlast = 0;
            bool ordered = false;
            for (int i = 0; i < 1200; i++)
            {
                if (!ordered && sim.Knowledge(Blue).LevelOf(target.Id) == ContactLevel.Visible)
                {
                    foreach (var blue in blues.Where(b => !b.IsOutOfAction))
                        sim.Submit(Blue, new AssaultOrder(blue.Id, target.Id));
                    ordered = true;
                }
                var live = sim.Grenades.ToDictionary(g => g.Id);
                scenario.Tick();
                foreach (var e in sim.Step())
                {
                    if (e is GrenadeThrown { } thrown && sim.FindUnit(thrown.Thrower)!.Side == Blue || e is MeleeStarted)
                        closeCombat++;
                    if (e is GrenadeExploded boom && live.TryGetValue(boom.Grenade, out var g) && g.Side == Blue)
                    {
                        long lethalSq = (long)g.Def.LethalRadiusCm * g.Def.LethalRadiusCm;
                        ownMenInOwnBlast += blues.Count(b => b.IsAlive && (b.Position - boom.At).LengthSquared < lethalSq);
                    }
                }
            }
            Assert.True(ordered, "the Finns never saw the Soviet leader");
            return (StateHash.Compute(sim), closeCombat, ownMenInOwnBlast);
        }

        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a.CloseCombat > 0, "no Finnish grenade or hand-to-hand fight in the assault");
        Assert.Equal(0, a.OwnMenInOwnBlast);
    }

    private static Vec2 PassableNear(GridMap map, Vec2 point)
    {
        var c = point.ToCell();
        for (int r = 0; r < 10; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var cell = new CellCoord(c.X + dx, c.Y + dy);
                    if (map.InBounds(cell) && map[cell].IsPassable)
                        return cell.CenterCm;
                }
        throw new InvalidOperationException($"no passable cell near {point}");
    }

    [Fact]
    public void LootingOnTheRealMap_IsDeterministic()
    {
        var root = CoreContentTests.RepoRoot();
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", "skirmish.tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));

        (ulong Hash, int Ordered, int Auto) Run()
        {
            var scenario = SkirmishScenario.Create(map, 1942, weapons, grenades);
            var sim = scenario.Sim;
            var blues = sim.Units.Where(u => u.Side == Side.Blue).ToList();
            // Everyone holds fire so the looting is not decided by a firefight.
            foreach (var unit in sim.Units)
                sim.Submit(unit.Side, new SetFirePolicyOrder(unit.Id, Nmf.Sim.Combat.FirePolicy.HoldFire));
            // A Finn falls at the start line; one comrade is sent to search him, a rifleman short of ammo goes by himself.
            Nmf.Sim.Combat.Damage.SetWound(sim, blues[3], Nmf.Sim.Combat.WoundLevel.Dead, 0, []);
            blues[2].Magazines = Nmf.Sim.Combat.CombatRules.LowOnMagazines;
            sim.Submit(Side.Blue, new LootOrder(blues[0].Id, blues[3].Id));
            int ordered = 0, auto = 0;
            for (int i = 0; i < 1200; i++)
            {
                scenario.Tick();
                foreach (var e in sim.Step().OfType<UnitLooted>())
                {
                    if (e.Looter == blues[0].Id) ordered++;
                    if (e.Looter == blues[2].Id) auto++;
                }
            }
            return (StateHash.Compute(sim), ordered, auto);
        }

        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a.Ordered + a.Auto >= 1, "nobody searched the fallen Finn");
    }
}
