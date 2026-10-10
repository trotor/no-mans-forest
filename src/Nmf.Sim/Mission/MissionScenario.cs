using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Mission;

/// <summary>
/// Builds a scenario from a mission: the rosters on the map's "blue" and "red" points in order (or where a man is placed
/// himself), with their attributes, items and starting state.
/// </summary>
public static class MissionScenario
{
    public static Scenario Create(GridMap map, MissionSpec spec, IReadOnlyDictionary<string, WeaponDef> weapons,
        IReadOnlyDictionary<string, GrenadeDef> grenades, ulong seed, IReadOnlyList<Tactics.TacticDef>? tactics = null)
    {
        var sim = new Simulation(map, seed);
        Spawn(sim, map, spec, Side.Blue, SkirmishScenario.BluePointType, spec.Player, weapons, grenades);
        Spawn(sim, map, spec, Side.Red, SkirmishScenario.RedPointType, spec.Enemy, weapons, grenades);
        if (spec.EnemyAi.Calm)
        {
            // Each of his men awake only to what he sees and hears (spec 2026-10-10-alertness-design).
            sim.EnableAlertness(Side.Red);
            sim.SetFlares(Side.Red, spec.EnemyAi.Flares);
        }
        foreach (var phone in spec.Phones)
            sim.AddFieldPhone(new FieldPhone(phone.Id, Side.Red, phone.At, phone.Line));
        var patrols = spec.Patrols ? SkirmishScenario.AssignPatrols(sim, map) : [];
        // A squad a mission event releases, or sends to search, is held back until then (spec 2026-10-10-events-design §1,
        // 2026-10-10-alertness-design §5).
        var held = spec.Events.SelectMany(e => e.Do).Where(a => a.Kind is EventActionKind.Release or EventActionKind.Search).Select(a => a.Squad!).ToHashSet();
        var commanded = sim.Units.Where(u => u.Side == Side.Red && !held.Contains(SquadName(spec, sim, u) ?? ""));
        var commanders = spec.EnemyAi.Any
            ? new[] { new AI.EnemyCommander(Side.Red, spec.EnemyAi, commanded, patrols.Select(p => p.Unit), tactics) }
            : [];
        if (spec.Support?.Mortar is { } mortar)
        {
            // The radioman by name: the rosters are spawned in order, the player's first.
            int index = spec.Player.ToList().FindIndex(s => s.Name == mortar.Radio);
            if (index < 0)
                throw new ArgumentException($"mission '{spec.Id}': the radioman '{mortar.Radio}' is not in the player's forces");
            sim.AddMortarSupport(new Combat.MortarSupport(Side.Blue, sim.Units[index].Id, mortar.He,
                mortar.DelayMinS * SimConstants.TicksPerSecond, mortar.DelayMaxS * SimConstants.TicksPerSecond));
        }
        return new Scenario(sim, patrols, commanders);
    }

    /// <summary>The mission squad a spawned man belongs to: the rosters are spawned in order, the player's first.</summary>
    public static string? SquadName(MissionSpec spec, Simulation sim, Unit unit)
    {
        int index = sim.Units.ToList().IndexOf(unit);
        var man = index < 0 ? null : index < spec.Player.Count ? spec.Player[index]
            : index - spec.Player.Count < spec.Enemy.Count ? spec.Enemy[index - spec.Player.Count] : null;
        return man is null ? null : man.Squad ?? spec.SquadNames.Keys.FirstOrDefault();
    }

    private static void Spawn(Simulation sim, GridMap map, MissionSpec spec, Side side, string pointType, IReadOnlyList<SoldierSpec> roster,
        IReadOnlyDictionary<string, WeaponDef> weapons, IReadOnlyDictionary<string, GrenadeDef> grenades)
    {
        var points = map.Features.Points.Where(p => p.Type == pointType).ToList();
        int needPoints = roster.Count(m => m.At is null);
        if (needPoints > points.Count)
            throw new ArgumentException($"mission '{spec.Id}': {needPoints} {side} soldiers but the map has only {points.Count} '{pointType}' points");
        int point = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            var man = roster[i];
            Vec2 position;
            if (man.At is { } at)
            {
                var cell = new CellCoord(at.X, at.Y);
                if (!map.InBounds(cell))
                    throw new ArgumentException($"mission '{spec.Id}': {man.Name} is placed at ({at.X}, {at.Y}), off the {map.Width} x {map.Height} map");
                position = cell.CenterCm;
            }
            else
                position = points[point++].Position;
            var weapon = weapons.TryGetValue(man.Weapon, out var w) ? w : throw new ArgumentException($"mission '{spec.Id}': weapon '{man.Weapon}' is not defined");
            GrenadeDef? grenade = man.Grenade is null ? null
                : grenades.TryGetValue(man.Grenade, out var g) ? g : throw new ArgumentException($"mission '{spec.Id}': grenade '{man.Grenade}' is not defined");
            var unit = sim.SpawnUnit(side, position, SkirmishScenario.SoldierWalkSpeedCmPerTick, weapon, man.Leader, grenade);
            unit.Name = man.Name;
            unit.Nerve = man.Nerve;
            unit.Marksmanship = man.Marksmanship;
            unit.Experience = man.Experience;
            unit.Squad = spec.SquadIndex(man);
            if (man.Morale is { } morale)
            {
                unit.BaseMorale = morale;
                unit.Morale = morale;
            }
            if (man.Leader)
                unit.LeaderQualityPct = man.Leadership;
            foreach (var itemId in man.Items ?? [])
                unit.AddItem(new Item(itemId, spec.Items.TryGetValue(itemId, out var name) ? name.En : itemId));
            unit.Wound = man.State switch
            {
                SoldierState.Wounded => WoundLevel.Light,
                SoldierState.Incapacitated => WoundLevel.Incapacitated,
                SoldierState.Dead => WoundLevel.Dead,
                _ => WoundLevel.None,
            };
            if (unit.IsOutOfAction)
                unit.Stance = Stance.Prone; // the fallen lie on the ground, as Damage lays down a man who goes down
            if (man.Searched)
                unit.MarkSearchedBy(side == Side.Blue ? Side.Red : Side.Blue);
            if (man.Stranded)
            {
                // Cut off and waiting to be found: he gives nobody away by firing.
                unit.Stranded = true;
                unit.FirePolicy = FirePolicy.HoldFire;
            }
        }
    }
}
