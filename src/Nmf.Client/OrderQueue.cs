using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Client;

public enum QueuedKind { Move, Attack, AttackForward, FireAt, AreaFire, TakePosition, Loot, Assault }

/// <summary>One order waiting its turn (spec 2026-10-10-controls-design §4).</summary>
public sealed record QueuedOrder(QueuedKind Kind, Vec2 Point, UnitId? Target = null, MoveMode Mode = MoveMode.Auto, string? Tactic = null);

/// <summary>Why a queue stopped to ask.</summary>
public enum QueueStop { AttackHalted, Rejected, NobodyLeft }

/// <summary>The orders queued for one set of men, in turn.</summary>
public sealed class OrderGroup
{
    internal OrderGroup(IEnumerable<UnitId> men) => MenList.AddRange(men.OrderBy(id => id.Value));

    internal readonly List<UnitId> MenList = [];
    public IReadOnlyList<UnitId> Men => MenList;
    internal readonly List<QueuedOrder> ItemList = [];
    public IReadOnlyList<QueuedOrder> Items => ItemList;
    /// <summary>The order being carried out; -1 before the first.</summary>
    public int Current { get; internal set; } = -1;
    /// <summary>How many orders are still to come after the current one (with the current one when paused).</summary>
    public int Left => Paused ? ItemList.Count - Current - 1 : ItemList.Count - Math.Max(0, Current) - 1;
    public bool Paused { get; internal set; }
    public QueueStop? Stop { get; internal set; }
    public PlannedAttackHalted? Halt { get; internal set; }
    internal long StartedTick;
    internal int? AttackId;
    /// <summary>Attack forward: an attack on what it met is under way, the move to go on after.</summary>
    internal bool Detour;
}

/// <summary>
/// Queued orders (spec 2026-10-10-controls-design §4): Shift adds to the queue of the men ordered; each order starts when
/// the one before is done; a halted attack, a rejected order or nobody left stops the queue to ask. Client side only: the
/// queue gives ordinary orders, so the simulation and replays know nothing of it.
/// </summary>
public sealed class OrderQueue
{
    private const int SettleTicks = 10;
    private const int MoveTimeoutTicks = 120 * SimConstants.TicksPerSecond;
    private const int FireTicks = 15 * SimConstants.TicksPerSecond;
    private const int AttackAppearTicks = 20;
    private const int ForwardSightCm = 15_000;

    private readonly List<OrderGroup> _groups = [];
    public IReadOnlyList<OrderGroup> Groups => _groups;

    /// <summary>Adds the order to these men's queue (a new queue when they have none of their own, starting at once).</summary>
    public void Add(GameSession session, IReadOnlyList<UnitId> men, QueuedOrder order)
    {
        var set = men.OrderBy(id => id.Value).ToList();
        var group = _groups.FirstOrDefault(g => g.MenList.SequenceEqual(set));
        if (group is null)
        {
            ClearFor(set);
            group = new OrderGroup(set);
            _groups.Add(group);
        }
        group.ItemList.Add(order);
        if (!group.Paused && (group.Current < 0 || group.Current >= group.ItemList.Count - 1 && Idle(session, group)))
            Next(session, group);
    }

    /// <summary>These men got an order of their own: they leave whatever queue they were in.</summary>
    public void ClearFor(IReadOnlyList<UnitId> men)
    {
        foreach (var group in _groups)
            group.MenList.RemoveAll(men.Contains);
        _groups.RemoveAll(g => g.MenList.Count == 0);
    }

    public void Continue(GameSession session, OrderGroup group)
    {
        group.Paused = false;
        group.Stop = null;
        group.Halt = null;
        Next(session, group);
    }

    public void Cancel(OrderGroup group) => _groups.Remove(group);

    /// <summary>After the frame's steps: what is done, what starts, what stops to ask.</summary>
    public void Update(GameSession session, IReadOnlyList<SimEvent> events)
    {
        foreach (var group in _groups.ToList())
        {
            if (group.Paused || group.Current < 0 || group.Current >= group.ItemList.Count)
                continue;
            var fit = Fit(session, group);
            if (fit.Count == 0)
            {
                Pause(group, QueueStop.NobodyLeft);
                continue;
            }
            if (events.Any(e => e is OrderRejected r && group.MenList.Contains(r.Order.Unit) && e.Tick >= group.StartedTick))
            {
                Pause(group, QueueStop.Rejected);
                continue;
            }
            var order = group.ItemList[group.Current];
            group.AttackId ??= session.Sim.PlannedAttacks.FirstOrDefault(a => !a.Ended && a.Members.Any(group.MenList.Contains))?.Id;
            if (group.AttackId is { } attack)
            {
                if (events.OfType<PlannedAttackHalted>().FirstOrDefault(h => h.Id == attack && h.Side == session.PlayerSide) is { } halted)
                {
                    group.AttackId = null;
                    group.Detour = false;
                    group.Halt = halted;
                    Pause(group, QueueStop.AttackHalted);
                    continue;
                }
                if (events.OfType<PlannedAttackEnded>().Any(e => e.Id == attack && e.Side == session.PlayerSide))
                {
                    group.AttackId = null;
                    if (group.Detour)
                    {
                        group.Detour = false; // the position is taken: on to where they were going
                        session.IssueAs(group.MenList, () => session.OrderMove(order.Point, MoveMode.Auto));
                        group.StartedTick = session.Sim.Tick;
                        continue;
                    }
                    Next(session, group);
                    continue;
                }
                if (session.Sim.PlannedAttacks.Any(a => a.Id == attack && !a.Ended))
                    continue; // still at it
                group.AttackId = null;
            }
            if (Done(session, group, order, fit))
                Next(session, group);
        }
    }

    private static List<Unit> Fit(GameSession session, OrderGroup group) =>
        group.MenList.Select(session.Sim.FindUnit).Where(u => u is { IsOutOfAction: false }).Cast<Unit>().ToList();

    private static bool Idle(GameSession session, OrderGroup group) =>
        Fit(session, group).All(u => u.MoveTarget is null) && !session.Sim.PlannedAttacks.Any(a => !a.Ended && a.Members.Any(group.MenList.Contains));

    private static void Pause(OrderGroup group, QueueStop why)
    {
        group.Paused = true;
        group.Stop = why;
    }

    private bool Done(GameSession session, OrderGroup group, QueuedOrder order, List<Unit> fit)
    {
        long age = session.Sim.Tick - group.StartedTick;
        if (age < SettleTicks)
            return false;
        switch (order.Kind)
        {
            case QueuedKind.Move or QueuedKind.TakePosition:
                return fit.All(u => u.MoveTarget is null) || age >= MoveTimeoutTicks;
            case QueuedKind.AttackForward:
                if (!group.Detour && SeenEnemyNear(session, fit) is { } enemy)
                {
                    group.Detour = true;
                    session.IssueAs(group.MenList, () => session.OrderAttack(enemy.Id));
                    if (session.LastPlanFailed || session.LastPlan is null)
                    {
                        // No plan to be had: they fire at him and go on.
                        group.Detour = false;
                        session.IssueAs(group.MenList, () => session.OrderMove(order.Point, MoveMode.Auto));
                    }
                    group.StartedTick = session.Sim.Tick;
                    return false;
                }
                return !group.Detour && (fit.All(u => u.MoveTarget is null) || age >= MoveTimeoutTicks);
            case QueuedKind.Attack:
                // No planned attack came of it (no plan, or the old kind): done when the men are free again.
                return age >= AttackAppearTicks && !session.Sim.AttackGroups.Any(g => !g.Ended && g.Members.Any(group.MenList.Contains));
            case QueuedKind.FireAt or QueuedKind.AreaFire:
                return age >= FireTicks || (order.Target is { } t && session.Sim.FindUnit(t) is { IsOutOfAction: true });
            case QueuedKind.Assault:
                return fit.All(u => u.AssaultTarget is null);
            case QueuedKind.Loot:
                return fit.All(u => u.LootTarget is null);
        }
        return true;
    }

    /// <summary>The nearest enemy the side sees within 150 m of any of the men, not already under their attack.</summary>
    private static Unit? SeenEnemyNear(GameSession session, List<Unit> fit)
    {
        long sightSq = (long)ForwardSightCm * ForwardSightCm;
        return session.Sim.Units
            .Where(u => u.Side != session.PlayerSide && !u.IsOutOfAction && session.Knowledge.LevelOf(u.Id) == ContactLevel.Visible)
            .Select(u => (Unit: u, Distance: fit.Min(m => (m.Position - u.Position).LengthSquared)))
            .Where(x => x.Distance <= sightSq)
            .OrderBy(x => x.Distance).ThenBy(x => x.Unit.Id.Value)
            .Select(x => x.Unit)
            .FirstOrDefault();
    }

    /// <summary>Starts the next order (one on a man who has fallen meanwhile is skipped); the queue goes when all are done.</summary>
    private void Next(GameSession session, OrderGroup group)
    {
        while (true)
        {
            group.Current++;
            group.AttackId = null;
            group.Detour = false;
            if (group.Current >= group.ItemList.Count)
            {
                _groups.Remove(group);
                return;
            }
            var order = group.ItemList[group.Current];
            if (order.Target is { } t && order.Kind is QueuedKind.Attack or QueuedKind.FireAt or QueuedKind.Assault
                && session.Sim.FindUnit(t) is not { IsOutOfAction: false })
                continue;
            group.StartedTick = session.Sim.Tick;
            session.IssueAs(group.MenList, () => Issue(session, order));
            return;
        }
    }

    internal static void Issue(GameSession session, QueuedOrder order)
    {
        switch (order.Kind)
        {
            case QueuedKind.Move:
                session.OrderMove(order.Point, order.Mode);
                break;
            case QueuedKind.AttackForward:
                session.OrderMove(order.Point, MoveMode.Auto);
                break;
            case QueuedKind.Attack:
                session.OrderAttack(order.Target!.Value, order.Tactic);
                break;
            case QueuedKind.FireAt:
                session.OrderFireAt(order.Target!.Value);
                break;
            case QueuedKind.AreaFire:
                session.OrderAreaFire(order.Point);
                break;
            case QueuedKind.TakePosition:
                session.OrderTakePosition(order.Point);
                break;
            case QueuedKind.Assault:
                session.OrderAssault(order.Target!.Value);
                break;
            case QueuedKind.Loot:
                if (session.Sim.FindUnit(order.Target!.Value) is { } body)
                    session.OrderLoot(body);
                break;
        }
    }
}
