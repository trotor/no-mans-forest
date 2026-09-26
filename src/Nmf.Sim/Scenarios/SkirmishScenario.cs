using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Scenarios;

/// <summary>Builds the test skirmish from map points ("blue", "red") and paths ("patrol"); with weapons the first man of a side leads with an SMG, the second carries the LMG.</summary>
public static class SkirmishScenario
{
    public const int SoldierWalkSpeedCmPerTick = 8;
    public const string BluePointType = "blue";
    public const string RedPointType = "red";
    public const string PatrolPathType = "patrol";

    public const string FinnishLeaderWeapon = "suomi_kp31";
    public const string FinnishSupportWeapon = "lahti_saloranta";
    public const string FinnishRifle = "mosin_m39";
    public const string SovietLeaderWeapon = "ppsh41";
    public const string SovietSupportWeapon = "dp27";
    public const string SovietRifle = "mosin_9130";
    public const string FinnishGrenade = "m32";
    public const string SovietGrenade = "rgd33";

    /// <summary>Papers the Soviet squad leader carries; worth taking off him.</summary>
    public static readonly Item SovietOrders = new("soviet_orders", "Soviet orders");

    /// <summary>Nerve by spawn order (the leader first); men beyond the list have the default.</summary>
    public static readonly int[] FinnishNerve = [80, 70, 45, 60];
    public static readonly int[] SovietNerve = [75, 55, 40, 65, 50];

    /// <summary>Unarmed soldiers (movement and vision only).</summary>
    public static Scenario Create(GridMap map, ulong seed) => Create(map, seed, null);

    public static Scenario Create(GridMap map, ulong seed, IReadOnlyDictionary<string, WeaponDef>? weapons,
        IReadOnlyDictionary<string, GrenadeDef>? grenades = null)
    {
        var sim = new Simulation(map, seed);
        int blueIndex = 0, redIndex = 0;
        foreach (var point in map.Features.Points)
        {
            if (point.Type == BluePointType)
                Spawn(sim, Side.Blue, point.Position, blueIndex++, weapons, FinnishLeaderWeapon, FinnishSupportWeapon, FinnishRifle, Grenade(grenades, FinnishGrenade));
            else if (point.Type == RedPointType)
                Spawn(sim, Side.Red, point.Position, redIndex++, weapons, SovietLeaderWeapon, SovietSupportWeapon, SovietRifle, Grenade(grenades, SovietGrenade));
        }

        sim.Units.FirstOrDefault(u => u.Side == Side.Red)?.AddItem(SovietOrders);
        foreach (var (side, nerve) in new[] { (Side.Blue, FinnishNerve), (Side.Red, SovietNerve) })
        {
            int index = 0;
            foreach (var unit in sim.Units.Where(u => u.Side == side))
                unit.Nerve = index < nerve.Length ? nerve[index++] : CombatRules.DefaultNerve;
        }

        return new Scenario(sim, AssignPatrols(sim, map));
    }

    private static void Spawn(Simulation sim, Side side, Vec2 position, int index, IReadOnlyDictionary<string, WeaponDef>? weapons,
        string leaderWeapon, string supportWeapon, string rifle, GrenadeDef? grenade)
    {
        if (weapons is null)
        {
            sim.SpawnUnit(side, position, SoldierWalkSpeedCmPerTick);
            return;
        }
        string id = index == 0 ? leaderWeapon : index == 1 ? supportWeapon : rifle;
        if (!weapons.TryGetValue(id, out var weapon))
            throw new ArgumentException($"weapon '{id}' is not defined");
        sim.SpawnUnit(side, position, SoldierWalkSpeedCmPerTick, weapon, isLeader: index == 0, grenade: grenade);
    }

    private static GrenadeDef? Grenade(IReadOnlyDictionary<string, GrenadeDef>? grenades, string id)
    {
        if (grenades is null)
            return null;
        return grenades.TryGetValue(id, out var grenade) ? grenade : throw new ArgumentException($"grenade '{id}' is not defined");
    }

    /// <summary>Each "patrol" path on the map goes to the nearest red man not already patrolling.</summary>
    internal static IReadOnlyList<PatrolBehavior> AssignPatrols(Simulation sim, GridMap map)
    {
        var patrols = new List<PatrolBehavior>();
        var assigned = new HashSet<UnitId>();
        foreach (var path in map.Features.Paths.Where(p => p.Type == PatrolPathType))
        {
            var start = path.Points[0];
            var unit = sim.Units
                .Where(u => u.Side == Side.Red && !assigned.Contains(u.Id))
                .OrderBy(u => (u.Position - start).LengthSquared)
                .ThenBy(u => u.Id.Value)
                .FirstOrDefault();
            if (unit is null)
                break;
            assigned.Add(unit.Id);
            patrols.Add(new PatrolBehavior(unit.Id, path.Points));
        }
        return patrols;
    }
}
