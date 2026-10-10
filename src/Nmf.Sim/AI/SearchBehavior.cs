using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.AI;

/// <summary>
/// A search party combing an area (spec 2026-10-10-alertness-design §5): together to the point of a 20 m grid nearest
/// the middle, then on to the nearest one not yet looked at, a man every 3 m abreast; round again when all are done.
/// Once one of them is fighting or under fire the party is handed over (to the commander) and the search gives no more
/// orders. Like a patrol it runs before every step, gives only orders and draws no random numbers.
/// </summary>
public sealed class SearchBehavior
{
    public const int GridCm = 2_000, AbreastCm = 300;

    private readonly IReadOnlyList<UnitId> _men;
    private readonly List<Vec2> _points = [];
    private readonly HashSet<int> _visited = [];
    private readonly Action<IReadOnlyList<Unit>> _handOver;
    private int? _current;
    /// <summary>On their way somewhere (a grid point or a track).</summary>
    private bool _going;
    /// <summary>The tick of the freshest track they have followed: they only ever go on to fresher.</summary>
    private long _trail = long.MinValue;

    /// <summary>Enemy tracks are looked for this far round each man.</summary>
    public const int TrackLookCm = 2_500;

    /// <param name="handOver">Called once with the party's men still fit, when the search ends in a fight.</param>
    public SearchBehavior(IReadOnlyList<UnitId> men, MapZone zone, Action<IReadOnlyList<Unit>> handOver)
    {
        _men = men;
        _handOver = handOver;
        for (int y = zone.Min.Y + GridCm / 2; y < zone.Max.Y; y += GridCm)
            for (int x = zone.Min.X + GridCm / 2; x < zone.Max.X; x += GridCm)
                _points.Add(new Vec2(x, y));
        if (_points.Count == 0)
            _points.Add(new Vec2((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2));
        Middle = new Vec2((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2);
    }

    private Vec2 Middle { get; }

    /// <summary>Over: the party was handed over, or is gone.</summary>
    public bool Done { get; private set; }

    /// <summary>Call once before every <see cref="Simulation.Step"/>.</summary>
    public void Tick(Simulation sim)
    {
        if (Done)
            return;
        var party = _men.Select(sim.FindUnit).OfType<Unit>().Where(u => !u.IsOutOfAction && u.MoraleState != MoraleState.Broken).ToList();
        if (party.Count == 0)
        {
            Done = true;
            return;
        }
        if (party.Any(u => InAFight(sim, u)))
        {
            Done = true;
            _handOver(party);
            return;
        }
        if (_going && party.Any(u => u.MoveTarget is not null))
            return; // still on their way
        if (_current is { } reached)
            _visited.Add(reached);
        bool first = !_going; // their very first leg goes from the middle of the zone, every later one from where they are
        _current = null;
        _going = true;
        // A trail in the snow: on to its freshest bit any of them sees (spec 2026-10-10-winter-design §3), else the grid.
        if (FreshestTrack(sim, party) is { } track)
        {
            _trail = track.Tick;
            Send(sim, party, track.At);
            return;
        }
        if (_visited.Count == _points.Count)
            _visited.Clear(); // all looked at: round again
        var from = first ? Middle : Centre(party);
        int next = Enumerable.Range(0, _points.Count).Where(i => !_visited.Contains(i))
            .OrderBy(i => (_points[i] - from).LengthSquared).ThenBy(i => i).First();
        _current = next;
        Send(sim, party, _points[next]);
    }

    private static void Send(Simulation sim, List<Unit> party, Vec2 to)
    {
        for (int i = 0; i < party.Count; i++)
        {
            int offset = (2 * i - (party.Count - 1)) * AbreastCm / 2;
            var spot = new Vec2(Math.Clamp(to.X + offset, 50, sim.Map.WidthCm - 50), to.Y);
            sim.Submit(party[i].Side, new MoveOrder(party[i].Id, spot, MoveMode.Auto));
        }
    }

    /// <summary>The freshest enemy track one of them sees within reach, fresher than any they have followed.</summary>
    private (Vec2 At, long Tick)? FreshestTrack(Simulation sim, List<Unit> party)
    {
        if (sim.Winter is null)
            return null;
        (Vec2 At, long Tick)? best = null;
        foreach (var man in party)
        {
            int eye = VisionRules.EyeHeightAbsCm(sim.Map, man);
            foreach (var seen in TrackSystem.FreshAround(sim, man.Position, man.Side, TrackLookCm, sim.Tick, _trail, take: 3))
                if (LineOfSight.Clarity(sim.Map, man.Position, eye, seen.At, sim.Map.CellAt(seen.At).GroundHeightCm + 10) > 0)
                {
                    if (best is null || seen.Tick > best.Value.Tick)
                        best = seen;
                    break;
                }
        }
        return best;
    }

    /// <summary>Fighting: in combat (with alertness), or under fire or the enemy in sight (without).</summary>
    private static bool InAFight(Simulation sim, Unit man)
    {
        if (man.Suppression > 0 || man.Wound > WoundLevel.None)
            return true;
        if (sim.AlertnessOn(man.Side))
            return man.Alertness == Alertness.Combat;
        return sim.Knowledge(man.Side).Contacts.Any(c => c.Level == ContactLevel.Visible && sim.FindUnit(c.Target) is { IsOutOfAction: false });
    }

    private static Vec2 Centre(List<Unit> men) =>
        new((int)(men.Sum(m => (long)m.Position.X) / men.Count), (int)(men.Sum(m => (long)m.Position.Y) / men.Count));
}
