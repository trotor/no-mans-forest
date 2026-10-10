using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.World;

/// <summary>
/// When the last man of each side went through a cell of snow (spec 2026-10-10-winter-design §3); never: <see cref="None"/>.
/// Both are kept, so one side's tracks over the other's leave them as they were.
/// </summary>
public readonly record struct Track(long BlueTick, long RedTick)
{
    public const long None = long.MinValue;

    public long TickOf(Side side) => side == Side.Blue ? BlueTick : RedTick;

    public Track With(Side side, long tick) => side == Side.Blue ? this with { BlueTick = tick } : this with { RedTick = tick };
}

/// <summary>In winter every man on the move leaves his track in each cell he goes through; a man carried leaves none.</summary>
internal static class TrackSystem
{
    /// <summary>Tracks older than this tell nothing.</summary>
    public const int FreshTicks = 30 * 60 * SimConstants.TicksPerSecond;

    public static void Update(Simulation sim, long tick)
    {
        if (sim.Winter is null)
            return;
        foreach (var unit in sim.Units)
            if (unit.IsMoving && unit.CarriedBy is null)
                sim.LeaveTrack(unit.Position, unit.Side, tick);
    }

    /// <summary>
    /// The freshest tracks of the other side round <paramref name="at"/> within <paramref name="rangeCm"/>, fresher than
    /// <paramref name="after"/>: cells and the tick they were made, freshest first, at most <paramref name="take"/>.
    /// </summary>
    public static List<(Vec2 At, long Tick)> FreshAround(Simulation sim, Vec2 at, Side seer, int rangeCm, long tick, long after, int take)
    {
        var found = new List<(int Key, long Tick)>();
        var other = seer == Side.Blue ? Side.Red : Side.Blue;
        var map = sim.Map;
        var centre = at.ToCell();
        int cells = rangeCm / 100;
        long rangeSq = (long)cells * cells;
        for (int dy = -cells; dy <= cells; dy++)
            for (int dx = -cells; dx <= cells; dx++)
            {
                if ((long)dx * dx + (long)dy * dy > rangeSq)
                    continue;
                int x = centre.X + dx, y = centre.Y + dy;
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
                    continue;
                int key = y * map.Width + x;
                if (sim.Tracks.TryGetValue(key, out var track) && track.TickOf(other) is var made && made != Track.None
                    && made > after && tick - made <= FreshTicks)
                    found.Add((key, made));
            }
        return found.OrderByDescending(f => f.Tick).ThenBy(f => f.Key).Take(take)
            .Select(f => (new CellCoord(f.Key % map.Width, f.Key / map.Width).CenterCm, f.Tick)).ToList();
    }
}
