using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>The player's selected units, kept in unit-id order.</summary>
public sealed class Selection
{
    private readonly SortedSet<int> _ids = [];

    public IReadOnlyList<UnitId> Ids => _ids.Select(id => new UnitId(id)).ToList();
    public int Count => _ids.Count;

    public bool Contains(UnitId id) => _ids.Contains(id.Value);

    public void Clear() => _ids.Clear();

    public void Add(UnitId id) => _ids.Add(id.Value);

    public bool SelectAt(IEnumerable<Unit> units, Side side, Vec2 point, int radiusCm, bool additive)
    {
        if (!additive)
            Clear();
        long radiusSquared = (long)radiusCm * radiusCm;
        var nearest = units
            .Where(u => u.Side == side && (u.Position - point).LengthSquared <= radiusSquared)
            .OrderBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
        if (nearest is null)
            return false;
        _ids.Add(nearest.Id.Value);
        return true;
    }

    public int SelectInBox(IEnumerable<Unit> units, Side side, Vec2 a, Vec2 b, bool additive)
    {
        if (!additive)
            Clear();
        int minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
        int minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
        int added = 0;
        foreach (var unit in units)
        {
            var p = unit.Position;
            if (unit.Side == side && p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY && _ids.Add(unit.Id.Value))
                added++;
        }
        return added;
    }
}
