using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.Mission;

/// <summary>
/// A mission's "when … then …" script (spec 2026-10-10-events-design): run after every step, it acts only through
/// orders, the tracker and the stranded flag, so a replay of the order log plays it the same. It also lets a stranded
/// man join when one of his side reaches him.
/// </summary>
public sealed class MissionEvents
{
    public const int EvaluateTicks = 10;
    /// <summary>One of his side this close finds a stranded man.</summary>
    public const int FoundCm = 500;

    private readonly MissionSpec _spec;
    private readonly Scenario _scenario;
    private readonly MissionTracker _tracker;
    private readonly Side _player;
    private readonly HashSet<string> _fired = [];
    /// <summary>Events under way: the next action and the tick it may run.</summary>
    private readonly List<(EventSpec Event, int Next, long At)> _running = [];

    public MissionEvents(MissionSpec spec, Scenario scenario, MissionTracker tracker, Side player = Side.Blue)
    {
        _spec = spec;
        _scenario = scenario;
        _tracker = tracker;
        _player = player;
        var map = scenario.Sim.Map;
        foreach (var e in spec.Events)
        {
            if (e.When.Phone is { } phone && scenario.Sim.FieldPhones.All(p => p.Id != phone))
                throw new ArgumentException($"event '{e.Id}': phone '{phone}' is not in the mission");
            if (e.When.ZoneName is { } zone && spec.ZoneNamed(map, zone) is null)
                throw new ArgumentException($"event '{e.Id}': zone '{zone}' is not on the map nor among the mission's zones");
            foreach (var a in e.Do.Where(a => a.Kind == EventActionKind.Search))
                if (spec.ZoneNamed(map, a.To!) is null)
                    throw new ArgumentException($"event '{e.Id}': search '{a.To}', which is not a zone");
            foreach (var a in e.Do.Where(a => a.Kind == EventActionKind.Send))
                if (Destination(a.To!) is null)
                    throw new ArgumentException($"event '{e.Id}': send to '{a.To}', which is neither a place nor a zone");
        }
    }

    /// <summary>Call once after every step (and after the tracker's update); the events it raises.</summary>
    public IReadOnlyList<SimEvent> Tick(Simulation sim)
    {
        var events = new List<SimEvent>();
        if (_tracker.Result is not null)
        {
            _running.Clear(); // over: whatever was still to come is dropped
            return events;
        }
        if (sim.Tick % EvaluateTicks == 0)
        {
            FindStranded(sim, events);
            foreach (var e in _spec.Events)
                if (!_fired.Contains(e.Id) && Holds(sim, e.When))
                {
                    _fired.Add(e.Id);
                    _running.Add((e, 0, sim.Tick));
                }
        }
        for (int i = 0; i < _running.Count && _tracker.Result is null; i++)
        {
            var (e, next, at) = _running[i];
            while (next < e.Do.Count && sim.Tick >= at && _tracker.Result is null)
            {
                var action = e.Do[next++];
                if (action.Kind == EventActionKind.Wait)
                    at = sim.Tick + action.Ticks;
                else
                    Do(sim, action, events);
            }
            _running[i] = (e, next, at);
        }
        _running.RemoveAll(r => r.Next >= r.Event.Do.Count);
        if (_tracker.Result is not null)
            _running.Clear();
        return events;
    }

    private bool Holds(Simulation sim, EventCondition when)
    {
        if (when.AfterTicks is { } after && sim.Tick < after)
            return false;
        if (when.Objective is { } objective && !_tracker.IsDone(objective))
            return false;
        if (when.Event is { } other && !_fired.Contains(other))
            return false;
        // The call got through (spec 2026-10-10-alertness-design §4): the phone's own state, not an event.
        if (when.Phone is { } phone && sim.FieldPhones.FirstOrDefault(p => p.Id == phone) is not { Called: true })
            return false;
        if (when.ContactBy is { } seer && !sim.Knowledge(seer).Contacts.Any(c => c.Level == ContactLevel.Visible
                && sim.FindUnit(c.Target) is { } seen && seen.Side != seer))
            return false;
        if (when.ZoneName is { } zoneName && when.ZoneSide is { } side)
        {
            var zone = _spec.ZoneNamed(sim.Map, zoneName)!;
            if (!sim.Units.Any(u => u.Side == side && !u.IsOutOfAction && !u.Stranded
                                    && u.Position.X >= zone.Min.X && u.Position.Y >= zone.Min.Y && u.Position.X < zone.Max.X && u.Position.Y < zone.Max.Y))
                return false;
        }
        if (when.LossesSide is { } lossSide && sim.Units.Count(u => u.Side == lossSide && u.IsOutOfAction) < when.LossesAtLeast)
            return false;
        return true;
    }

    private void Do(Simulation sim, EventAction action, List<SimEvent> events)
    {
        switch (action.Kind)
        {
            case EventActionKind.Message:
                events.Add(new MissionMessage(sim.Tick, action.Text!));
                break;
            case EventActionKind.Send:
                var to = Destination(action.To!)!.Value;
                foreach (var man in Squad(sim, action.Squad!).Where(m => !m.IsOutOfAction && m.MoraleState != MoraleState.Broken))
                    sim.Submit(man.Side, new MoveOrder(man.Id, to, action.Mode));
                break;
            case EventActionKind.Release:
                foreach (var commander in _scenario.Commanders)
                    commander.Adopt(Squad(sim, action.Squad!).Where(m => m.Side == commander.Side && !m.IsOutOfAction));
                break;
            case EventActionKind.Search:
                // Out on the alert, combing the zone; in a fight they are the commander's (spec 2026-10-10-alertness-design §5).
                var party = Squad(sim, action.Squad!).Where(m => !m.IsOutOfAction && m.MoraleState != MoraleState.Broken).ToList();
                foreach (var man in party.Where(m => sim.AlertnessOn(m.Side)))
                    sim.Submit(man.Side, new AlertOrder(man.Id, Alertness.Alerted));
                var commanders = _scenario.Commanders;
                _scenario.AddSearch(new AI.SearchBehavior(party.Select(m => m.Id).ToList(), _spec.ZoneNamed(sim.Map, action.To!)!,
                    men => { foreach (var c in commanders) c.Adopt(men.Where(m => m.Side == c.Side)); }));
                break;
            case EventActionKind.Reveal:
                if (_tracker.Reveal(action.Objective!, sim.Tick) is { } revealed)
                    events.Add(revealed);
                break;
            case EventActionKind.End:
                if (_tracker.End(action.Success, sim.Tick) is { } ended)
                    events.Add(ended);
                break;
        }
    }

    /// <summary>A stranded man of the player's side joins once one of them reaches him.</summary>
    private void FindStranded(Simulation sim, List<SimEvent> events)
    {
        foreach (var lost in sim.Units.Where(u => u.Side == _player && u.Stranded && u.IsAlive).ToList())
        {
            if (!sim.Units.Any(u => u.Side == _player && !u.Stranded && !u.IsOutOfAction
                                    && (u.Position - lost.Position).LengthSquared <= (long)FoundCm * FoundCm))
                continue;
            // The flag is the mission's (who may be commanded, who does objective work), not the fight's; his fire comes back
            // by an order, so a replay of the order log fights the same.
            lost.Stranded = false;
            sim.Submit(_player, new SetFirePolicyOrder(lost.Id, FirePolicy.FireAtWill));
            events.Add(new StrandedFound(sim.Tick, lost.Id));
        }
    }

    private IEnumerable<Unit> Squad(Simulation sim, string squad) =>
        sim.Units.Where(u => MissionScenario.SquadName(_spec, sim, u) == squad);

    private Vec2? Destination(string name)
    {
        if (_spec.Places.TryGetValue(name, out var place))
            return place;
        return _spec.ZoneNamed(_scenario.Sim.Map, name) is { } zone ? new Vec2((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2) : null;
    }
}
