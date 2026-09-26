using Nmf.Sim.Combat;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Mission;

/// <summary>Builds a scenario from a mission: the rosters on the map's "blue" and "red" points in order, with their attributes and items.</summary>
public static class MissionScenario
{
    public static Scenario Create(GridMap map, MissionSpec spec, IReadOnlyDictionary<string, WeaponDef> weapons,
        IReadOnlyDictionary<string, GrenadeDef> grenades, ulong seed)
    {
        var sim = new Simulation(map, seed);
        Spawn(sim, map, spec, Side.Blue, SkirmishScenario.BluePointType, spec.Player, weapons, grenades);
        Spawn(sim, map, spec, Side.Red, SkirmishScenario.RedPointType, spec.Enemy, weapons, grenades);
        return new Scenario(sim, SkirmishScenario.AssignPatrols(sim, map));
    }

    private static void Spawn(Simulation sim, GridMap map, MissionSpec spec, Side side, string pointType, IReadOnlyList<SoldierSpec> roster,
        IReadOnlyDictionary<string, WeaponDef> weapons, IReadOnlyDictionary<string, GrenadeDef> grenades)
    {
        var points = map.Features.Points.Where(p => p.Type == pointType).ToList();
        if (roster.Count > points.Count)
            throw new ArgumentException($"mission '{spec.Id}': {roster.Count} {side} soldiers but the map has only {points.Count} '{pointType}' points");
        for (int i = 0; i < roster.Count; i++)
        {
            var man = roster[i];
            var weapon = weapons.TryGetValue(man.Weapon, out var w) ? w : throw new ArgumentException($"mission '{spec.Id}': weapon '{man.Weapon}' is not defined");
            GrenadeDef? grenade = man.Grenade is null ? null
                : grenades.TryGetValue(man.Grenade, out var g) ? g : throw new ArgumentException($"mission '{spec.Id}': grenade '{man.Grenade}' is not defined");
            var unit = sim.SpawnUnit(side, points[i].Position, SkirmishScenario.SoldierWalkSpeedCmPerTick, weapon, man.Leader, grenade);
            unit.Name = man.Name;
            unit.Nerve = man.Nerve;
            unit.Marksmanship = man.Marksmanship;
            if (man.Morale is { } morale)
            {
                unit.BaseMorale = morale;
                unit.Morale = morale;
            }
            if (man.Leader)
                unit.LeaderQualityPct = man.Leadership;
            foreach (var itemId in man.Items ?? [])
                unit.AddItem(new Item(itemId, spec.Items.TryGetValue(itemId, out var name) ? name.En : itemId));
        }
    }
}
