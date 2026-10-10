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
    private readonly HashSet<string> _visible = [];
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
            if (!objective.Hidden)
                _visible.Add(objective.Id);
            foreach (var zoneName in new[] { objective.Zone, objective.Area })
                if (zoneName is not null)
                    _zones[zoneName] = spec.ZoneNamed(map, zoneName)
                                       ?? throw new ArgumentException($"objective '{objective.Id}': zone '{zoneName}' is not on the map nor among the mission's zones");
        }
    }

    public MissionSpec Spec => _spec;

    /// <summary>Null while the mission runs; true when accomplished, false when failed.</summary>
    public bool? Result { get; private set; }

    public bool IsDone(string objectiveId) => _done[objectiveId];

    /// <summary>Shown to the player and counted for the result: not hidden, or revealed (spec 2026-10-10-events-design §5).</summary>
    public bool IsVisible(string objectiveId) => _visible.Contains(objectiveId);

    public int DoneCount => _done.Count(kv => kv.Value && _visible.Contains(kv.Key));

    public int VisibleCount => _visible.Count;

    /// <summary>A hidden objective shown from now on; null when it already was.</summary>
    public SimEvent? Reveal(string objectiveId, long tick) => _visible.Add(objectiveId) ? new ObjectiveRevealed(tick, objectiveId) : null;

    /// <summary>A mission event ends it (spec 2026-10-10-events-design §4); null when it had already ended.</summary>
    public SimEvent? End(bool success, long tick)
    {
        if (Result is not null)
            return null;
        Result = success;
        return new MissionEnded(tick, success);
    }

    public IReadOnlyList<SimEvent> Update(Simulation sim)
    {
        var events = new List<SimEvent>();
        if (Result is not null)
            return events;
        // Those who do the work: the side's men in action, not one still waiting to be found.
        var own = sim.Units.Where(u => u.Side == _player && !u.IsOutOfAction && !u.Stranded).ToList();
        bool rescueLost = false;

        // Pick-ups first, so a reach-zone objective can require one in the same step.
        foreach (var objective in _spec.Objectives.OrderBy(o => o.Type == ObjectiveType.PickUp ? 0 : 1))
        {
            // A place once reached or found stays so; a pick-up can be lost again (its carrier falls), a rescue too.
            if (objective.Type is ObjectiveType.ReachZone or ObjectiveType.Find && _done[objective.Id])
                continue;
            var rescued = objective.Type == ObjectiveType.Rescue ? sim.Units.FirstOrDefault(u => u.Side == _player && u.Name == objective.Unit) : null;
            if (objective.Type == ObjectiveType.Rescue && rescued is not { IsAlive: true } && _visible.Contains(objective.Id))
                rescueLost = true; // hidden, it counts for nothing yet: revealed later, it fails then
            bool requires = (objective.Requires ?? []).All(r => _done[r]);
            bool done = objective.Type switch
            {
                ObjectiveType.PickUp => own.Any(u => Carries(u, objective.Item)),
                ObjectiveType.ReachZone => requires && own.Any(u => Inside(_zones[objective.Zone!], u.Position)
                                                                    && (objective.Carrying is null || Carries(u, objective.Carrying))),
                ObjectiveType.Find => _spec.Places.TryGetValue(objective.Place!, out var place)
                                      && own.Any(u => (u.Position - place).LengthSquared <= (long)objective.RadiusCm * objective.RadiusCm),
                ObjectiveType.Rescue => requires && rescued is { IsAlive: true } && Inside(_zones[objective.Zone!], rescued.Position),
                _ => false,
            };
            if (done != _done[objective.Id])
            {
                _done[objective.Id] = done;
                events.Add(new ObjectiveChanged(sim.Tick, objective.Id, done));
            }
        }

        if (rescueLost)
            Result = false; // the man to bring back is dead: nothing left to do here
        else if (_visible.All(id => _done[id]))
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
