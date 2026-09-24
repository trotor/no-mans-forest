using System.Text;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Content;

/// <summary>Human-readable description of a map, for the CLI and for mission authors.</summary>
public static class MapSummary
{
    public static string Describe(GridMap map)
    {
        var counts = new int[map.TerrainNames.Count];
        int minHeight = int.MaxValue, maxHeight = int.MinValue;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var cell = map[new CellCoord(x, y)];
                counts[cell.TerrainId]++;
                minHeight = Math.Min(minHeight, cell.GroundHeightCm);
                maxHeight = Math.Max(maxHeight, cell.GroundHeightCm);
            }
        }
        int total = map.Width * map.Height;

        var sb = new StringBuilder();
        Line(sb, $"Size: {map.Width} x {map.Height} cells ({map.Width} m x {map.Height} m)");
        Line(sb, $"Terrain:");
        var terrains = counts
            .Select((count, id) => (Name: map.TerrainNames[id], Count: count))
            .Where(t => t.Count > 0)
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name, StringComparer.Ordinal);
        foreach (var (name, count) in terrains)
            Line(sb, $"  {name}: {count} ({count * 100.0 / total:0.0} %)");
        Line(sb, $"Ground height: {minHeight} .. {maxHeight} cm");
        Line(sb, $"Zones ({map.Features.Zones.Count}): {Names(map.Features.Zones.Select(z => z.Name))}");
        Line(sb, $"Points ({map.Features.Points.Count}): {Names(map.Features.Points.Select(p => p.Name))}");
        Line(sb, $"Paths ({map.Features.Paths.Count}): {Names(map.Features.Paths.Select(p => p.Name))}");
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, FormattableString text) =>
        sb.Append(FormattableString.Invariant(text)).Append('\n');

    private static string Names(IEnumerable<string> names)
    {
        string joined = string.Join(", ", names);
        return joined.Length == 0 ? "-" : joined;
    }
}
