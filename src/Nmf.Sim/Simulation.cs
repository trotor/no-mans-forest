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
        CarrySystem.Update(this, Tick, events);

        foreach (var unit in _units)
        {
            Damage.Update(this, unit, Tick, events);
            MoraleSystem.Tick(this, unit, Tick, events);
        }

        if (Tick % VisionRules.IntervalTicks == 0)
        {
            VisionSystem.Update(this, Tick, events);
            foreach (var planned in _plannedAttacks)
                Tactics.PlannedAttackRunner.Update(this, planned, Tick, events);
            _plannedAttacks.RemoveAll(a => a.Ended);
            foreach (var unit in _units)
                SoldierBrain.Update(this, unit, Tick, events);
        }

        Tick++;
        return events;
    }

    private readonly List<Tactics.PlannedAttack> _plannedAttacks = [];
    private int _nextPlannedAttackId;

    /// <summary>Planned attacks under way (spec 2026-10-10-tactics-design).</summary>
    public IReadOnlyList<Tactics.PlannedAttack> PlannedAttacks => _plannedAttacks;

    /// <summary>Free for a carry: whatever else he was doing stops.</summary>
    private static void PrepareForHands(Unit unit)
    {
        Movement.ClearPath(unit);
        LeaveAttack(unit);
        unit.AreaTarget = null;
        unit.AssaultTarget = null;
        LootSystem.Abandon(unit);
        SoldierBrain.ForgetCover(unit);
        unit.StanceOrdered = false;
        unit.AutoPace = false;
        Firing.Cancel(unit);
    }

    /// <summary>A man given another order drops out of his attack, planned or not.</summary>
    internal static void LeaveAttack(Unit unit)
    {
        unit.PlannedAttackId = null;
        unit.AttackRole = AttackRole.None;
    }

    /// <summary>Straight in at the enemy: run, grenade, hand to hand (the assault order, or the end of an attack).</summary>
    /// <param name="goal">Where to run: the target's position, or where the side believes he is.</param>
    internal bool StartAssault(Unit unit, Unit target, Vec2? goal = null, List<Vec2>? path = null)
    {
        var to = goal ?? target.Position;
        path ??= Pathfinder.FindPath(Map, unit.Position, to);
        if (path is null)
            return false;
        Firing.Cancel(unit);
        unit.AreaTarget = null;
        unit.AutoPace = false;
        unit.StanceOrdered = false;
        LootSystem.Abandon(unit);
        unit.AssaultTarget = target.Id;
        SoldierBrain.ForgetCover(unit);
        unit.OrderedTarget = target.Id;
        unit.AssaultGoal = to;
        Movement.StartPath(unit, to, MoveMode.Run, path);
        return true;
    }

    private void ApplyPlanned(Side issuer, PlannedAttackOrder order, List<SimEvent> events)
    {
        var target = FindUnit(order.Plan.Target);
        if (target is null || target.Side == issuer || target.IsOutOfAction)
        {
            events.Add(new OrderRejected(Tick, order, "invalid target"));
            return;
        }
        var attack = Tactics.PlannedAttackRunner.Start(this, _nextPlannedAttackId + 1, issuer, order.Plan, order.Tactic, Tick);
        if (attack is null)
        {
            events.Add(new OrderRejected(Tick, order, "nobody of the plan can go"));
            return;
        }
        _nextPlannedAttackId++;
        _plannedAttacks.Add(attack);
        if (order.Shout && FindUnit(order.Unit) is { } caller)
            events.Add(new CounterattackStarted(Tick, issuer, caller.Id, caller.Position, order.Plan.Target));
    }

    private void Apply(LoggedOrder logged, List<SimEvent> events)
    {
        var order = logged.Order;
        if (order is PlannedAttackOrder planned)
        {
            // The plan's men are checked one by one: a pinned man is left out, the rest go.
            ApplyPlanned(logged.Issuer, planned, events);
            return;
        }
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
        // Hands needed, or going flat: a man carried is laid down first (spec 2026-10-10-carrying-design §2).
        if ((order is FireAtOrder or AreaFireOrder or AssaultOrder or LootOrder && (unit.HandsBusy || unit.CarryTarget is not null))
            || (order is SetStanceOrder { Stance: Stance.Prone } && (unit.Carrying is not null || unit.HelpingCarry is not null)))
            CarrySystem.LetGo(this, unit, Tick, events);

        switch (order)
        {
            case MoveOrder or AssaultOrder or LootOrder or CarryOrder when pinned:
                events.Add(new OrderRejected(Tick, order, "unit is pinned"));
                break;
            case SetStanceOrder stanceWhilePinned when pinned && stanceWhilePinned.Stance != Stance.Prone:
                events.Add(new OrderRejected(Tick, order, "unit is pinned"));
                break;
            case MoveOrder move when !Map.Contains(move.Target):
                events.Add(new OrderRejected(Tick, order, "target outside map"));
                break;
            case CarryOrder carry:
                var wounded = FindUnit(carry.Wounded);
                if (wounded is null || !CarrySystem.CanBeCarried(unit, wounded) || unit.Carrying is not null)
                {
                    events.Add(new OrderRejected(Tick, order, "nobody there who cannot move, or his hands are full"));
                    break;
                }
                if (wounded.CarriedBy is { } carrierId)
                {
                    // Someone has him already: this man helps, if nobody does yet.
                    if (carrierId == unit.Id || _units.Any(u => u.HelpingCarry == wounded.Id))
                    {
                        events.Add(new OrderRejected(Tick, order, "two carry him already"));
                        break;
                    }
                    CarrySystem.LetGo(this, unit, Tick, events);
                    PrepareForHands(unit);
                    unit.HelpingCarry = wounded.Id;
                    break;
                }
                CarrySystem.LetGo(this, unit, Tick, events);
                PrepareForHands(unit);
                unit.CarryTarget = wounded.Id;
                break;
            case DropOrder:
                CarrySystem.LetGo(this, unit, Tick, events);
                break;
            case MoveOrder move:
                var path = Pathfinder.FindPath(Map, unit.Position, move.Target);
                if (path is null)
                {
                    events.Add(new OrderRejected(Tick, order, "target not reachable"));
                    break;
                }
                if (unit.HelpingCarry is not null || unit.CarryTarget is not null)
                    CarrySystem.LetGo(this, unit, Tick, events); // sent elsewhere: he lets go
                unit.AssaultTarget = null;
                LootSystem.Abandon(unit);
                unit.StanceOrdered = false;
                unit.AutoPace = move.Mode == MoveMode.Auto;
                SoldierBrain.ForgetCover(unit);
                LeaveAttack(unit);
                unit.AreaTarget = null;
                var mode = unit.AutoPace ? SoldierBrain.ChoosePace(this, unit) : move.Mode;
                if (unit.Carrying is not null && mode is MoveMode.Sneak or MoveMode.Crawl)
                    mode = MoveMode.Walk; // with a man on his back he walks or runs, no lower
                Movement.StartPath(unit, move.Target, mode, path);
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
                unit.AreaTarget = null;
                StartAssault(unit, assaultTarget, null, assaultPath);
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
                unit.AreaTarget = null;
                Movement.StartPath(unit, body.Position, SoldierBrain.ChoosePace(this, unit), lootPath);
                break;
            case StopOrder:
                Movement.ClearPath(unit);
                LeaveAttack(unit);
                unit.AreaTarget = null;
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
                LeaveAttack(unit);
                unit.AreaTarget = null;
                unit.OrderedTarget = fireTarget.Id;
                if (unit.Target != fireTarget.Id)
                    Firing.Cancel(unit);
                break;
            case AreaFireOrder area when !Map.Contains(area.Target):
                events.Add(new OrderRejected(Tick, order, "target outside map"));
                break;
            case AreaFireOrder area:
                if (unit.Weapon is null || unit.OutOfAmmo || unit.Magazines <= 0)
                {
                    events.Add(new OrderRejected(Tick, order, "no spare magazines"));
                    break;
                }
                if ((area.Target - unit.Position).LengthSquared > (long)unit.Weapon.RangeCm * unit.Weapon.RangeCm)
                {
                    events.Add(new OrderRejected(Tick, order, "out of range"));
                    break;
                }
                LeaveAttack(unit);
                LootSystem.Abandon(unit);
                SoldierBrain.ForgetCover(unit);
                Movement.ClearPath(unit);
                unit.AssaultTarget = null;
                unit.AutoPace = false;
                unit.OrderedTarget = null;
                Firing.Cancel(unit);
                unit.AreaTarget = area.Target;
                break;
            case SetFirePolicyOrder policy:
                // The player's own say: an infiltration he is creeping in with keeps it rather than giving back an older one.
                if (unit.PlannedAttackId is { } attackId && _plannedAttacks.FirstOrDefault(a => a.Id == attackId) is { } creeping)
                    creeping.PolicyBefore.Remove(unit.Id);
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
