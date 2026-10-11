using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>One route on the paper map: where the men are, the way they are going, then the legs queued after it.</summary>
/// <param name="QueuedFrom">The index in <paramref name="Points"/> where the queued legs begin (= Count when none).</param>
public sealed record MapRoute(IReadOnlyList<UnitId> Men, IReadOnlyList<Vec2> Points, int QueuedFrom);

/// <summary>The routes of our men for the paper map (M): one per queue (its first man's way), one per other man on the move.</summary>
public static class MapRoutes
{
    public static IReadOnlyList<MapRoute> For(GameSession session)
    {
        // Orders not taken yet (the map pauses the game) already show where the men will go.
        var pending = new Dictionary<UnitId, Vec2>();
        foreach (var logged in session.Sim.PendingOrders)
            if (logged.Order is Nmf.Sim.Orders.MoveOrder move)
                pending[move.Unit] = move.Target;
            else
                pending.Remove(logged.Order.Unit);
        var routes = new List<MapRoute>();
        var inQueue = new HashSet<UnitId>();
        foreach (var group in session.Queue.Groups)
        {
            var men = group.Men.Select(id => session.Sim.FindUnit(id)).OfType<Unit>().Where(u => !u.IsOutOfAction).ToList();
            var legs = group.Items.Skip(group.Current + 1).Where(o => o.Kind is QueuedKind.Move or QueuedKind.AttackForward).Select(o => o.Point).ToList();
            if (men.Count == 0 || legs.Count == 0)
                continue;
            inQueue.UnionWith(group.Men);
            var points = Way(men[0], pending);
            routes.Add(new MapRoute(men.Select(m => m.Id).ToList(), [.. points, .. legs], points.Count));
        }
        foreach (var man in session.OwnUnits)
        {
            if (man.IsOutOfAction || man.Stranded || (man.MoveTarget is null && !pending.ContainsKey(man.Id)) || inQueue.Contains(man.Id))
                continue;
            var points = Way(man, pending);
            routes.Add(new MapRoute([man.Id], points, points.Count));
        }
        return routes;
    }

    /// <summary>Where he is, then the rest of his path to where he is going (straight to where an order not yet taken sends him).</summary>
    private static List<Vec2> Way(Unit man, Dictionary<UnitId, Vec2> pending)
    {
        var points = new List<Vec2> { man.Position };
        if (pending.TryGetValue(man.Id, out var ordered))
        {
            points.Add(ordered);
            return points;
        }
        if (man.MoveTarget is not { } goal)
            return points;
        for (int i = man.PathIndex; i < man.Path.Count; i++)
            points.Add(man.Path[i]);
        if (points[^1] != goal)
            points.Add(goal);
        return points;
    }
}
