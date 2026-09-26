using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Mission;

/// <summary>
/// Follows a mission's objectives from the simulation state after every step (spec 2026-09-26-missions-design §4).
/// A pure function of the state, so deterministic; the result, once reached, is final.
/// </summary>
public sealed class MissionTracker
{
    private readonly MissionSpec _spec;
    private readonly Dictionary<string, MapZone> _zones = [];
    private readonly Dictionary<string, bool> _done = [];
    private readonly Side _player;

    public MissionTracker(MissionSpec spec, GridMap map, Side player = Side.Blue)
    {
        _spec = spec;
        _player = player;
        foreach (var arrow in spec.Plan)
            foreach (var point in arrow.Points)
                if (!map.Contains(point))
                    throw new ArgumentException($"mission '{spec.Id}': plan point {point} is off the {map.Width}x{map.Height} m map");
        foreach (var objective in spec.Objectives)
        {
            _done[objective.Id] = false;
            if (objective.Zone is { } zoneName)
                _zones[zoneName] = map.Features.Zones.FirstOrDefault(z => z.Name == zoneName)
                                   ?? throw new ArgumentException($"objective '{objective.Id}': zone '{zoneName}' is not on the map");
        }
    }

    public MissionSpec Spec => _spec;

    /// <summary>Null while the mission runs; true when accomplished, false when failed.</summary>
    public bool? Result { get; private set; }

    public bool IsDone(string objectiveId) => _done[objectiveId];

    public int DoneCount => _done.Values.Count(d => d);

    public IReadOnlyList<SimEvent> Update(Simulation sim)
    {
        var events = new List<SimEvent>();
        if (Result is not null)
            return events;
        var own = sim.Units.Where(u => u.Side == _player && !u.IsOutOfAction).ToList();

        // Pick-ups first, so a reach-zone objective can require one in the same step.
        foreach (var objective in _spec.Objectives.OrderBy(o => o.Type == ObjectiveType.PickUp ? 0 : 1))
        {
            // A place once reached stays reached; only a pick-up can be lost again (its carrier falls).
            if (objective.Type == ObjectiveType.ReachZone && _done[objective.Id])
                continue;
            bool done = objective.Type switch
            {
                ObjectiveType.PickUp => own.Any(u => Carries(u, objective.Item)),
                ObjectiveType.ReachZone => (objective.Requires ?? []).All(r => _done[r])
                                           && own.Any(u => Inside(_zones[objective.Zone!], u.Position)
                                                           && (objective.Carrying is null || Carries(u, objective.Carrying))),
                _ => false,
            };
            if (done != _done[objective.Id])
            {
                _done[objective.Id] = done;
                events.Add(new ObjectiveChanged(sim.Tick, objective.Id, done));
            }
        }

        if (_done.Values.All(d => d))
            Result = true;
        else if (own.Count == 0 && sim.Units.Any(u => u.Side == _player))
            Result = false;
        if (Result is { } success)
            events.Add(new MissionEnded(sim.Tick, success));
        return events;
    }

    private static bool Carries(Unit unit, string? item) => item is not null && unit.Items.Any(i => i.Id == item);

    private static bool Inside(MapZone zone, Vec2 p) => p.X >= zone.Min.X && p.Y >= zone.Min.Y && p.X < zone.Max.X && p.Y < zone.Max.Y;
}
