using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;

namespace Nmf.Sim.AI;

/// <summary>Walks a unit back and forth along a list of points. Issues ordinary orders, like a player would.</summary>
public sealed class PatrolBehavior
{
    private readonly IReadOnlyList<Vec2> _points;
    private readonly MoveMode _mode;
    private int _next;
    private int _direction = 1;

    public PatrolBehavior(UnitId unit, IReadOnlyList<Vec2> points, MoveMode mode = MoveMode.Walk)
    {
        if (points.Count < 2)
            throw new ArgumentException("A patrol needs at least two points.", nameof(points));
        Unit = unit;
        _points = points;
        _mode = mode;
    }

    public UnitId Unit { get; }

    /// <summary>Call once before every <see cref="Simulation.Step"/>.</summary>
    public void Tick(Simulation sim)
    {
        var unit = sim.FindUnit(Unit);
        if (unit is null || unit.MoveTarget is not null || unit.TargetStance is not null)
            return;

        sim.Submit(unit.Side, new MoveOrder(Unit, _points[_next], _mode));
        if (_next + _direction < 0 || _next + _direction >= _points.Count)
            _direction = -_direction;
        _next += _direction;
    }
}
