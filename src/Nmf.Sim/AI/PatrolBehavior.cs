using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.AI;

/// <summary>Walks a unit back and forth along a list of points. Issues ordinary orders, like a player would.</summary>
public sealed class PatrolBehavior
{
    private readonly IReadOnlyList<Vec2> _points;
    private readonly MoveMode _mode;
    private int _next;
    private int _direction = 1;

    public PatrolBehavior(UnitId unit, IReadOnlyList<Vec2> points, MoveMode mode = MoveMode.Auto)
    {
        if (points.Count < 2)
            throw new ArgumentException("A patrol needs at least two points.", nameof(points));
        Unit = unit;
        _points = points;
        _mode = mode;
    }

    public UnitId Unit { get; }

    private static bool EnemyInSight(Simulation sim, Side side)
    {
        foreach (var contact in sim.Knowledge(side).Contacts)
            if (contact.Level == ContactLevel.Visible && sim.FindUnit(contact.Target) is { IsOutOfAction: false })
                return true;
        return false;
    }

    /// <summary>Call once before every <see cref="Simulation.Step"/>.</summary>
    public void Tick(Simulation sim)
    {
        var unit = sim.FindUnit(Unit);
        if (unit is null || unit.MoveTarget is not null || unit.TargetStance is not null)
            return;
        // A patrol is peacetime routine: it stops for good once the man is hurt, shaken or his side sees the enemy.
        if (unit.IsOutOfAction || unit.MoraleState != MoraleState.Steady || unit.Suppression > 0 || EnemyInSight(sim, unit.Side))
            return;

        sim.Submit(unit.Side, new MoveOrder(Unit, _points[_next], _mode));
        if (_next + _direction < 0 || _next + _direction >= _points.Count)
            _direction = -_direction;
        _next += _direction;
    }
}
