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
        if (_current is not null && party.Any(u => u.MoveTarget is not null))
            return; // still on their way
        if (_current is { } reached)
            _visited.Add(reached);
        if (_visited.Count == _points.Count)
            _visited.Clear(); // all looked at: round again
        var from = _current is null ? Middle : Centre(party);
        int next = Enumerable.Range(0, _points.Count).Where(i => !_visited.Contains(i))
            .OrderBy(i => (_points[i] - from).LengthSquared).ThenBy(i => i).First();
        _current = next;
        var to = _points[next];
        for (int i = 0; i < party.Count; i++)
        {
            int offset = (2 * i - (party.Count - 1)) * AbreastCm / 2;
            var spot = new Vec2(Math.Clamp(to.X + offset, 50, sim.Map.WidthCm - 50), to.Y);
            sim.Submit(party[i].Side, new MoveOrder(party[i].Id, spot, MoveMode.Auto));
        }
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
