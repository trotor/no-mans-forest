using Nmf.Sim.Core;

namespace Nmf.Sim.World;

/// <summary>Named rectangle; <paramref name="Max"/> is exclusive.</summary>
public sealed record MapZone(string Name, string Type, Vec2 Min, Vec2 Max);

public sealed record MapPoint(string Name, string Type, Vec2 Position);

public sealed record MapPath(string Name, string Type, IReadOnlyList<Vec2> Points);

/// <summary>Named zones, points and paths placed by the mission author.</summary>
public sealed class MapFeatures(
    IReadOnlyList<MapZone> zones,
    IReadOnlyList<MapPoint> points,
    IReadOnlyList<MapPath> paths,
    IReadOnlyDictionary<string, string>? properties = null)
{
    public static MapFeatures Empty { get; } = new([], [], []);

    /// <summary>Map-level properties from the map file, e.g. <c>source</c> (data sources and licences).</summary>
    public IReadOnlyDictionary<string, string> Properties { get; } = properties ?? new Dictionary<string, string>();

    public IReadOnlyList<MapZone> Zones { get; } = zones;
    public IReadOnlyList<MapPoint> Points { get; } = points;
    public IReadOnlyList<MapPath> Paths { get; } = paths;
}
