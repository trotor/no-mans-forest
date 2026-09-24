using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim;

/// <summary>
/// The whole deterministic world. Orders are the only input and events the only output;
/// nothing outside this class mutates simulation state.
/// </summary>
public sealed class Simulation
{
    private readonly List<Unit> _units = [];
    private readonly Dictionary<UnitId, Unit> _unitsById = [];
    private readonly List<LoggedOrder> _pending = [];
    private readonly List<LoggedOrder> _orderLog = [];
    private readonly SideKnowledge[] _knowledge = [new(), new()];
    private int _nextUnitId = 1;

    public Simulation(GridMap map, ulong seed)
    {
        Map = map;
        Seed = seed;
        Rng = new Rng(seed);
    }

    public GridMap Map { get; }
    public ulong Seed { get; }
    public Rng Rng { get; }
    public long Tick { get; private set; }
    public IReadOnlyList<Unit> Units => _units;
    public IReadOnlyList<LoggedOrder> OrderLog => _orderLog;

    public SideKnowledge Knowledge(Side side) => _knowledge[(int)side];

    public Unit SpawnUnit(Side side, Vec2 position, int speedCmPerTick) => SpawnUnit(side, position, speedCmPerTick, null);

    public Unit SpawnUnit(Side side, Vec2 position, int speedCmPerTick, WeaponDef? weapon, bool isLeader = false)
    {
        if (!Map.Contains(position))
            throw new ArgumentOutOfRangeException(nameof(position), $"Spawn position {position} is outside the map.");
        if (speedCmPerTick <= 0)
            throw new ArgumentOutOfRangeException(nameof(speedCmPerTick), speedCmPerTick, "Speed must be positive.");

        var unit = new Unit(new UnitId(_nextUnitId++), side, position, speedCmPerTick, weapon, isLeader);
        _units.Add(unit);
        _unitsById.Add(unit.Id, unit);
        return unit;
    }

    public Unit? FindUnit(UnitId id) => _unitsById.GetValueOrDefault(id);

    /// <summary>Queues an order; it is applied at the start of the next <see cref="Step"/>.</summary>
    public void Submit(Side issuer, Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _pending.Add(new LoggedOrder(Tick, issuer, order));
    }

    public IReadOnlyList<SimEvent> Step()
    {
        var events = new List<SimEvent>();

        foreach (var logged in _pending)
        {
            _orderLog.Add(logged);
            Apply(logged, events);
        }
        _pending.Clear();

        foreach (var unit in _units)
            Movement.Update(unit, Map, Tick, events);

        if (Tick % VisionRules.IntervalTicks == 0)
            VisionSystem.Update(this, Tick, events);

        Tick++;
        return events;
    }

    private void Apply(LoggedOrder logged, List<SimEvent> events)
    {
        var order = logged.Order;
        var unit = FindUnit(order.Unit);
        if (unit is null)
        {
            events.Add(new OrderRejected(Tick, order, "unknown unit"));
            return;
        }
        if (unit.Side != logged.Issuer)
        {
            events.Add(new OrderRejected(Tick, order, "unit belongs to another side"));
            return;
        }

        switch (order)
        {
            case MoveOrder move when !Map.Contains(move.Target):
                events.Add(new OrderRejected(Tick, order, "target outside map"));
                break;
            case MoveOrder move:
                var path = Pathfinder.FindPath(Map, unit.Position, move.Target);
                if (path is null)
                {
                    events.Add(new OrderRejected(Tick, order, "target not reachable"));
                    break;
                }
                Movement.StartPath(unit, move.Target, move.Mode, path);
                break;
            case StopOrder:
                Movement.ClearPath(unit);
                break;
            case SetStanceOrder stance:
                Movement.ClearPath(unit);
                Movement.BeginStanceChange(unit, stance.Stance);
                break;
            default:
                events.Add(new OrderRejected(Tick, order, $"unsupported order {order.GetType().Name}"));
                break;
        }
    }
}
