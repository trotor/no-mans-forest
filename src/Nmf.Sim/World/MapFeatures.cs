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
    IReadOnlyList<MapPath> paths)
{
    public static MapFeatures Empty { get; } = new([], [], []);

    public IReadOnlyList<MapZone> Zones { get; } = zones;
    public IReadOnlyList<MapPoint> Points { get; } = points;
    public IReadOnlyList<MapPath> Paths { get; } = paths;
}
