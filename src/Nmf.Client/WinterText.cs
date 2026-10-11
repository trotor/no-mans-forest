using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client;

/// <summary>A man's winter kit for his card (spec 2026-10-10-winter-design §5): on skis, with an ahkio; null in summer or with neither.</summary>
public static class WinterText
{
    /// <summary>What a man on skis is doing on the move: skiing (walking or running upright); null otherwise.</summary>
    public static string? Moving(Simulation sim, Unit unit, string language) =>
        Skiing(sim, unit) ? language == "fi" ? "Hiihtää" : "Skiing" : null;

    private static bool Skiing(Simulation sim, Unit unit) =>
        sim.Winter is not null && unit.OnSkis && unit.MoveTarget is not null && !unit.IsOutOfAction
        && unit.MoveMode is MoveMode.Walk or MoveMode.Run or MoveMode.Auto && unit.Stance == Stance.Standing;

    public static string? Status(Simulation sim, Unit unit, string language)
    {
        if (sim.Winter is null)
            return null;
        bool fi = language == "fi";
        var parts = new List<string>();
        if (unit.OnSkis && !Skiing(sim, unit)) // skiing says it already
            parts.Add(fi ? "Sukset" : "On skis");
        if (unit.HasAhkio)
            parts.Add("ahkio");
        if (parts.Count == 0)
            return null;
        parts[0] = char.ToUpperInvariant(parts[0][0]) + parts[0][1..];
        return string.Join(" · ", parts);
    }
}

/// <summary>Tracks in the snow the player may see (spec 2026-10-10-winter-design §3): ours always, the enemy's only where our men see now.</summary>
public static class TrackMarks
{
    /// <param name="min">With <paramref name="max"/>: only the cells in this box (the part of the map on the screen).</param>
    public static IEnumerable<(Vec2 At, Side Side)> Visible(GameSession session, CellCoord? min = null, CellCoord? max = null)
    {
        var map = session.Sim.Map;
        var enemy = session.PlayerSide == Side.Blue ? Side.Red : Side.Blue;
        var tracks = session.Sim.Tracks;
        IEnumerable<(int Key, Track Track)> cells = tracks.Select(kv => (kv.Key, kv.Value));
        if (min is { } lo && max is { } hi)
        {
            int x0 = Math.Max(0, lo.X), y0 = Math.Max(0, lo.Y), x1 = Math.Min(map.Width - 1, hi.X), y1 = Math.Min(map.Height - 1, hi.Y);
            long box = (long)Math.Max(0, x1 - x0 + 1) * Math.Max(0, y1 - y0 + 1);
            // Whichever is fewer: the cells of the box, or the tracks there are.
            cells = box < tracks.Count
                ? BoxCells(x0, y0, x1, y1, map.Width).Where(tracks.ContainsKey).Select(k => (k, tracks[k]))
                : cells.Where(c => c.Key % map.Width >= x0 && c.Key % map.Width <= x1 && c.Key / map.Width >= y0 && c.Key / map.Width <= y1);
        }
        foreach (var (key, track) in cells)
        {
            var at = new CellCoord(key % map.Width, key / map.Width).CenterCm;
            if (track.TickOf(session.PlayerSide) != Track.None)
                yield return (at, session.PlayerSide);
            if (track.TickOf(enemy) != Track.None && session.Fog.IsVisible(at))
                yield return (at, enemy);
        }
    }

    /// <summary>A man's next step is in a neighbouring cell at most this much later.</summary>
    public const int StepTicks = 3 * Nmf.Sim.SimConstants.TicksPerSecond;

    /// <summary>
    /// The trails as lines (spec 2026-10-10-winter-design §3): each track cell joined to the neighbouring cell of the same
    /// side made soonest after it, so a man's way through the snow reads as one line.
    /// </summary>
    public static IEnumerable<(Vec2 From, Vec2 To, Side Side)> Segments(GameSession session, CellCoord? min = null, CellCoord? max = null)
    {
        var map = session.Sim.Map;
        var tracks = session.Sim.Tracks;
        foreach (var (at, side) in Visible(session, min, max))
        {
            var cell = at.ToCell();
            long tick = tracks[cell.Y * map.Width + cell.X].TickOf(side);
            Vec2? next = null;
            long best = long.MaxValue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cell.X + dx, y = cell.Y + dy;
                    if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= map.Width || y >= map.Height
                        || !tracks.TryGetValue(y * map.Width + x, out var other))
                        continue;
                    long t = other.TickOf(side);
                    if (t != Track.None && t > tick && t - tick <= StepTicks && t < best)
                    {
                        best = t;
                        next = new CellCoord(x, y).CenterCm;
                    }
                }
            if (next is { } to && (side == session.PlayerSide || session.Fog.IsVisible(to)))
                yield return (at, to, side);
        }
    }

    private static IEnumerable<int> BoxCells(int x0, int y0, int x1, int y1, int width)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return y * width + x;
    }
}

/// <summary>How dark the battlefield is drawn (spec 2026-10-10-winter-design §4): nothing by day, never so dark the men cannot be made out.</summary>
public static class LightOverlay
{
    public static float Darkness(int lightPct) => Math.Clamp((100 - lightPct) / 100f * 0.8f, 0f, 0.8f);
}
