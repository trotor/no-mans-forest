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
    /// <summary>How many orders are still to come after the current one.</summary>
    public int Left => ItemList.Count - Math.Max(0, Current) - 1;
    public bool Paused { get; internal set; }
    public QueueStop? Stop { get; internal set; }
    public PlannedAttackHalted? Halt { get; internal set; }
    internal long StartedTick;
    internal int? AttackId;
    /// <summary>Attacks of these ids or lower were there before the current order: never taken for its own.</summary>
    internal int AttacksBefore;
    /// <summary>Attack forward: an attack on what it met is under way, the move to go on after.</summary>
    internal bool Detour;
    /// <summary>The current attack could not be planned: the men fire instead, done as a fire order.</summary>
    internal bool NoPlan;
    /// <summary>Attack forward: the enemies already taken on, not to be taken on again.</summary>
    internal readonly HashSet<UnitId> Engaged = [];
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
        Prune(session);
        var set = men.OrderBy(id => id.Value).ToList();
        var group = _groups.FirstOrDefault(g => g.MenList.SequenceEqual(set));
        if (group is null)
        {
            ClearFor(set);
            group = new OrderGroup(set);
            _groups.Add(group);
        }
        group.ItemList.Add(order);
        if (!group.Paused && group.Current < 0)
            Next(session, group);
    }

    /// <summary>
    /// The double click of a Shift double click: its order takes the place of the one the same gesture's first click
    /// queued (the last of these men's queue), started anew if it was already under way.
    /// </summary>
    public void ReplaceLast(GameSession session, IReadOnlyList<UnitId> men, QueuedOrder order)
    {
        Prune(session);
        var set = men.OrderBy(id => id.Value).ToList();
        var group = _groups.FirstOrDefault(g => g.MenList.SequenceEqual(set));
        if (group is null || group.ItemList.Count == 0)
        {
            Add(session, men, order);
            return;
        }
        group.ItemList[^1] = order;
        if (!group.Paused && group.Current == group.ItemList.Count - 1)
        {
            group.Current--;
            Next(session, group);
        }
    }

    /// <summary>The last order queued for exactly these men, if they have a queue.</summary>
    public QueuedOrder? LastOf(IReadOnlyList<UnitId> men)
    {
        var set = men.OrderBy(id => id.Value).ToList();
        return _groups.FirstOrDefault(g => g.MenList.SequenceEqual(set)) is { ItemList.Count: > 0 } group ? group.ItemList[^1] : null;
    }

    /// <summary>The fallen leave their queues; a queue with nobody left goes.</summary>
    private void Prune(GameSession session)
    {
        foreach (var group in _groups)
            group.MenList.RemoveAll(id => session.Sim.FindUnit(id) is not { IsOutOfAction: false });
        _groups.RemoveAll(g => g.MenList.Count == 0);
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
        if (group.Current >= 0 && group.Current < group.ItemList.Count && group.ItemList[group.Current] is { Kind: QueuedKind.AttackForward } forward
            && group.Detour)
        {
            // The attack on the way stopped; the advance itself is not over: on forward.
            group.Detour = false;
            group.AttackId = null;
            Begin(session, group);
            session.IssueAs(group.MenList, () => session.OrderMove(forward.Point, MoveMode.Auto));
            return;
        }
        Next(session, group);
    }

    public void Cancel(OrderGroup group) => _groups.Remove(group);

    /// <summary>After the frame's steps: what is done, what starts, what stops to ask.</summary>
    public void Update(GameSession session, IReadOnlyList<SimEvent> events)
    {
        Prune(session);
        foreach (var group in _groups.ToList())
        {
            if (group.Paused || group.Current < 0 || group.Current >= group.ItemList.Count)
                continue;
            var fit = Fit(session, group);
            if (events.Any(e => e is OrderRejected r && group.MenList.Contains(r.Order.Unit) && e.Tick >= group.StartedTick))
            {
                Pause(group, QueueStop.Rejected);
                continue;
            }
            var order = group.ItemList[group.Current];
            bool waitsOnAttack = (order.Kind == QueuedKind.Attack && !group.NoPlan) || group.Detour;
            // Only an attack begun by this order counts: one the men were in before is not its own.
            if (waitsOnAttack)
                group.AttackId ??= session.Sim.PlannedAttacks
                    .FirstOrDefault(a => !a.Ended && a.Id > group.AttacksBefore && a.Members.Any(group.MenList.Contains))?.Id;
            if (group.AttackId is { } attack)
            {
                if (events.OfType<PlannedAttackHalted>().FirstOrDefault(h => h.Id == attack && h.Side == session.PlayerSide) is { } halted)
                {
                    group.AttackId = null;
                    group.Halt = halted;
                    Pause(group, QueueStop.AttackHalted);
                    continue;
                }
                if (events.OfType<PlannedAttackEnded>().FirstOrDefault(e => e.Id == attack && e.Side == session.PlayerSide) is { } ended)
                {
                    group.AttackId = null;
                    if (!ended.Success)
                    {
                        Pause(group, QueueStop.AttackHalted); // the position was not taken: ask
                        continue;
                    }
                    if (group.Detour)
                    {
                        group.Detour = false; // the position is taken: on to where they were going
                        Begin(session, group);
                        session.IssueAs(group.MenList, () => session.OrderMove(order.Point, MoveMode.Auto));
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

    /// <summary>The current order starts now: attacks there already are not its own.</summary>
    private static void Begin(GameSession session, OrderGroup group)
    {
        group.StartedTick = session.Sim.Tick;
        group.AttacksBefore = session.Sim.PlannedAttacks.Select(a => a.Id).DefaultIfEmpty(0).Max();
        group.AttacksBefore = Math.Max(group.AttacksBefore, LastAttackId(session));
    }

    /// <summary>The highest planned attack id seen so far (attacks that ended are gone from the list).</summary>
    private static int LastAttackId(GameSession session) => session.Sim.PlannedAttacks.Select(a => a.Id).DefaultIfEmpty(0).Max();

    private static List<Unit> Fit(GameSession session, OrderGroup group) =>
        group.MenList.Select(session.Sim.FindUnit).Where(u => u is { IsOutOfAction: false }).Cast<Unit>().ToList();

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
                if (group.Detour)
                {
                    // An attack that started and ended between two looks, or never came: on forward.
                    if (age >= AttackAppearTicks && group.AttackId is null
                        && !session.Sim.PlannedAttacks.Any(a => !a.Ended && a.Members.Any(group.MenList.Contains)))
                    {
                        group.Detour = false;
                        Begin(session, group);
                        session.IssueAs(group.MenList, () => session.OrderMove(order.Point, MoveMode.Auto));
                    }
                    return false;
                }
                if (fit.All(u => u.MoveTarget is null) || age >= MoveTimeoutTicks)
                    return true; // there
                if (SeenEnemyNear(session, group, fit) is { } enemy)
                {
                    group.Engaged.Add(enemy.Id); // once: he is not taken on again on the way
                    Begin(session, group);
                    session.IssueAs(group.MenList, () => session.OrderAttack(enemy.Id));
                    if (session.LastPlan is null)
                        session.IssueAs(group.MenList, () => session.OrderMove(order.Point, MoveMode.Auto)); // no plan: they fired at him, on they go
                    else
                        group.Detour = true;
                }
                return false;
            case QueuedKind.Attack when group.NoPlan:
                return age >= FireTicks || (order.Target is { } aimed && session.Sim.FindUnit(aimed) is { IsOutOfAction: true });
            case QueuedKind.Attack:
                // The old kind of attack (no tactics): done when the men are free again.
                return age >= AttackAppearTicks && !session.Sim.AttackGroups.Any(g => !g.Ended && g.Members.Any(group.MenList.Contains))
                       && !session.Sim.PlannedAttacks.Any(a => !a.Ended && a.Members.Any(group.MenList.Contains));
            case QueuedKind.FireAt or QueuedKind.AreaFire:
                return age >= FireTicks || (order.Target is { } t && session.Sim.FindUnit(t) is { IsOutOfAction: true });
            case QueuedKind.Assault:
                return fit.All(u => u.AssaultTarget is null);
            case QueuedKind.Loot:
                return fit.All(u => u.LootTarget is null);
        }
        return true;
    }

    /// <summary>The nearest enemy the side sees within 150 m of any of the men, not already taken on by this advance.</summary>
    private static Unit? SeenEnemyNear(GameSession session, OrderGroup group, List<Unit> fit)
    {
        long sightSq = (long)ForwardSightCm * ForwardSightCm;
        return session.Sim.Units
            .Where(u => u.Side != session.PlayerSide && !u.IsOutOfAction && !group.Engaged.Contains(u.Id)
                        && session.Knowledge.LevelOf(u.Id) == ContactLevel.Visible)
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
            group.NoPlan = false;
            Begin(session, group);
            session.IssueAs(group.MenList, () => Issue(session, order));
            group.NoPlan = order.Kind == QueuedKind.Attack && session.LastPlanFailed;
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
