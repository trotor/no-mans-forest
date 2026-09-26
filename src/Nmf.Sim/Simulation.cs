using Nmf.Sim.AI;
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
    private readonly List<Grenade> _grenades = [];
    private int _nextGrenadeId = 1;
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

    /// <summary>Grenades in flight or lying live, in throw order.</summary>
    public IReadOnlyList<Grenade> Grenades => _grenades;

    internal Grenade AddGrenade(Unit thrower, Vec2 landing, long tick)
    {
        var grenade = new Grenade(_nextGrenadeId++, thrower.Id, thrower.Side, thrower.GrenadeType!, thrower.Position, landing, tick);
        _grenades.Add(grenade);
        return grenade;
    }

    internal void RemoveExplodedGrenades() => _grenades.RemoveAll(g => g.Exploded);

    public Unit SpawnUnit(Side side, Vec2 position, int speedCmPerTick) => SpawnUnit(side, position, speedCmPerTick, null);

    public Unit SpawnUnit(Side side, Vec2 position, int speedCmPerTick, WeaponDef? weapon, bool isLeader = false, GrenadeDef? grenade = null)
    {
        if (!Map.Contains(position))
            throw new ArgumentOutOfRangeException(nameof(position), $"Spawn position {position} is outside the map.");
        if (speedCmPerTick <= 0)
            throw new ArgumentOutOfRangeException(nameof(speedCmPerTick), speedCmPerTick, "Speed must be positive.");

        var unit = new Unit(new UnitId(_nextUnitId++), side, position, speedCmPerTick, weapon, isLeader, grenade);
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
        foreach (var unit in _units)
            Firing.Update(this, unit, Tick, events);
        foreach (var unit in _units)
            GrenadeSystem.UpdateThrowing(this, unit, Tick, events);
        GrenadeSystem.UpdateGrenades(this, Tick, events);
        MeleeSystem.Update(this, Tick, events);
        LootSystem.Update(this, Tick, events);

        foreach (var unit in _units)
        {
            Damage.Update(this, unit, Tick, events);
            MoraleSystem.Tick(this, unit, Tick, events);
        }

        if (Tick % VisionRules.IntervalTicks == 0)
        {
            VisionSystem.Update(this, Tick, events);
            foreach (var group in _attackGroups)
                AttackPlanner.Update(this, group, Tick, events);
            _attackGroups.RemoveAll(g => g.Ended);
            foreach (var unit in _units)
                SoldierBrain.Update(this, unit, Tick, events);
        }

        Tick++;
        return events;
    }

    private readonly List<AttackGroup> _attackGroups = [];
    private int _nextAttackGroupId;

    /// <summary>Attacks by fire and movement now under way.</summary>
    public IReadOnlyList<AttackGroup> AttackGroups => _attackGroups;

    /// <summary>A man given another order drops out of his attack.</summary>
    private static void LeaveAttack(Unit unit)
    {
        unit.AttackGroupId = null;
        unit.AttackRole = AttackRole.None;
        unit.BoundIssued = unit.BoundSettled = false;
    }

    /// <summary>Straight in at the enemy: run, grenade, hand to hand (the assault order, or the end of an attack).</summary>
    internal void StartAssault(Unit unit, Unit target, List<Vec2>? path = null)
    {
        path ??= Pathfinder.FindPath(Map, unit.Position, target.Position);
        if (path is null)
            return;
        Firing.Cancel(unit);
        unit.AutoPace = false;
        unit.StanceOrdered = false;
        LootSystem.Abandon(unit);
        unit.AssaultTarget = target.Id;
        SoldierBrain.ForgetCover(unit);
        unit.OrderedTarget = target.Id;
        unit.AssaultGoal = target.Position;
        Movement.StartPath(unit, target.Position, MoveMode.Run, path);
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
        if (unit.IsOutOfAction)
        {
            events.Add(new OrderRejected(Tick, order, "unit is out of action"));
            return;
        }
        if (unit.MoraleState == MoraleState.Broken)
        {
            events.Add(new OrderRejected(Tick, order, "unit is broken"));
            return;
        }
        bool pinned = unit.MoraleState == MoraleState.Pinned;

        switch (order)
        {
            case MoveOrder or AssaultOrder or LootOrder or AttackOrder when pinned:
                events.Add(new OrderRejected(Tick, order, "unit is pinned"));
                break;
            case SetStanceOrder stanceWhilePinned when pinned && stanceWhilePinned.Stance != Stance.Prone:
                events.Add(new OrderRejected(Tick, order, "unit is pinned"));
                break;
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
                unit.AssaultTarget = null;
                LootSystem.Abandon(unit);
                unit.StanceOrdered = false;
                unit.AutoPace = move.Mode == MoveMode.Auto;
                SoldierBrain.ForgetCover(unit);
                LeaveAttack(unit);
                Movement.StartPath(unit, move.Target, unit.AutoPace ? SoldierBrain.ChoosePace(this, unit) : move.Mode, path);
                break;
            case AssaultOrder assault:
                var assaultTarget = FindUnit(assault.Target);
                if (assaultTarget is null || assaultTarget.Side == unit.Side || assaultTarget.IsOutOfAction)
                {
                    events.Add(new OrderRejected(Tick, order, "invalid target"));
                    break;
                }
                var assaultPath = Pathfinder.FindPath(Map, unit.Position, assaultTarget.Position);
                if (assaultPath is null)
                {
                    events.Add(new OrderRejected(Tick, order, "target not reachable"));
                    break;
                }
                LeaveAttack(unit);
                StartAssault(unit, assaultTarget, assaultPath);
                break;
            case AttackOrder attack:
                var attackTarget = FindUnit(attack.Target);
                if (attackTarget is null || attackTarget.Side == unit.Side || attackTarget.IsOutOfAction)
                {
                    events.Add(new OrderRejected(Tick, order, "invalid target"));
                    break;
                }
                LeaveAttack(unit);
                LootSystem.Abandon(unit);
                SoldierBrain.ForgetCover(unit);
                unit.AssaultTarget = null;
                unit.AutoPace = false;
                unit.StanceOrdered = false;
                Movement.ClearPath(unit);
                var group = _attackGroups.FirstOrDefault(g => !g.Ended && g.Side == unit.Side && g.Target == attackTarget.Id && g.CreatedTick == Tick);
                if (group is null)
                {
                    group = new AttackGroup(++_nextAttackGroupId, unit.Side, attackTarget.Id, Tick);
                    _attackGroups.Add(group);
                }
                group.MemberList.Add(unit.Id);
                unit.AttackGroupId = group.Id;
                unit.OrderedTarget = attackTarget.Id;
                break;
            case LootOrder loot:
                var body = FindUnit(loot.Body);
                if (body is null || body == unit || !body.IsOutOfAction)
                {
                    events.Add(new OrderRejected(Tick, order, "invalid target"));
                    break;
                }
                if (body.Looted)
                {
                    events.Add(new OrderRejected(Tick, order, "already looted"));
                    break;
                }
                var lootPath = Pathfinder.FindPath(Map, unit.Position, body.Position);
                if (lootPath is null)
                {
                    events.Add(new OrderRejected(Tick, order, "target not reachable"));
                    break;
                }
                LootSystem.Abandon(unit);
                unit.AssaultTarget = null;
                unit.StanceOrdered = false;
                unit.AutoPace = true;
                unit.LootTarget = body.Id;
                SoldierBrain.ForgetCover(unit);
                LeaveAttack(unit);
                Movement.StartPath(unit, body.Position, SoldierBrain.ChoosePace(this, unit), lootPath);
                break;
            case StopOrder:
                Movement.ClearPath(unit);
                LeaveAttack(unit);
                LootSystem.Abandon(unit);
                SoldierBrain.ForgetCover(unit);
                unit.AssaultTarget = null;
                unit.AutoPace = false;
                break;
            case SetStanceOrder stance:
                Movement.ClearPath(unit);
                LeaveAttack(unit);
                LootSystem.Abandon(unit);
                SoldierBrain.ForgetCover(unit);
                unit.AssaultTarget = null;
                unit.AutoPace = false;
                unit.StanceOrdered = true;
                Movement.BeginStanceChange(unit, stance.Stance);
                break;
            case FireAtOrder fire:
                var fireTarget = FindUnit(fire.Target);
                if (fireTarget is null || fireTarget.Side == unit.Side)
                {
                    events.Add(new OrderRejected(Tick, order, "invalid target"));
                    break;
                }
                unit.OrderedTarget = fireTarget.Id;
                if (unit.Target != fireTarget.Id)
                    Firing.Cancel(unit);
                break;
            case SetFirePolicyOrder policy:
                unit.FirePolicy = policy.Policy;
                if (policy.Policy == FirePolicy.HoldFire)
                    Firing.Cancel(unit);
                break;
            default:
                events.Add(new OrderRejected(Tick, order, $"unsupported order {order.GetType().Name}"));
                break;
        }
    }
}
