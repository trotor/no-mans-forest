using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.World;

/// <summary>The last man through a cell of snow: his side and when (spec 2026-10-10-winter-design §3).</summary>
public readonly record struct Track(Side Side, long Tick);

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
    /// <paramref name="after"/>: cells, freshest first, at most <paramref name="take"/>.
    /// </summary>
    public static List<(Vec2 At, Track Track)> FreshAround(Simulation sim, Vec2 at, Side seer, int rangeCm, long tick, long after, int take)
    {
        var found = new List<(int Key, Track Track)>();
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
                if (sim.Tracks.TryGetValue(key, out var track) && track.Side != seer && track.Tick > after && tick - track.Tick <= FreshTicks)
                    found.Add((key, track));
            }
        return found.OrderByDescending(f => f.Track.Tick).ThenBy(f => f.Key).Take(take)
            .Select(f => (new CellCoord(f.Key % map.Width, f.Key / map.Width).CenterCm, f.Track)).ToList();
    }
}
