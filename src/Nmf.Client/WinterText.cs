using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client;

/// <summary>A man's winter kit for his card (spec 2026-10-10-winter-design §5): on skis, with an ahkio; null in summer or with neither.</summary>
public static class WinterText
{
    public static string? Status(Simulation sim, Unit unit, string language)
    {
        if (sim.Winter is null)
            return null;
        bool fi = language == "fi";
        var parts = new List<string>();
        if (unit.OnSkis)
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
    public static IEnumerable<(Vec2 At, Track Track)> Visible(GameSession session)
    {
        var map = session.Sim.Map;
        foreach (var (key, track) in session.Sim.Tracks)
        {
            var at = new CellCoord(key % map.Width, key / map.Width).CenterCm;
            if (track.Side == session.PlayerSide || session.Fog.IsVisible(at))
                yield return (at, track);
        }
    }
}

/// <summary>How dark the battlefield is drawn (spec 2026-10-10-winter-design §4): nothing by day, never so dark the men cannot be made out.</summary>
public static class LightOverlay
{
    public static float Darkness(int lightPct) => Math.Clamp((100 - lightPct) / 100f * 0.8f, 0f, 0.8f);
}
