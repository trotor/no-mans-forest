namespace Nmf.Sim.World;

/// <summary>Static terrain values of one 1 m cell.</summary>
/// <param name="GroundHeightCm">Ground elevation.</param>
/// <param name="ObstacleHeightCm">Height of what stands on the cell (trees, rocks, walls).</param>
/// <param name="ConcealmentPerM">How much one metre of this cell blocks sight, 0..255 = 0..1.</param>
/// <param name="Cover">How well the cell stops bullets, 0..255 = 0..1.</param>
/// <param name="TerrainId">Index into <see cref="GridMap.TerrainNames"/>.</param>
/// <param name="ExtraMoveCost">Extra movement time in percent (0 = normal, 100 = twice as slow); <see cref="Impassable"/> blocks movement.</param>
/// <param name="LowCover">Cover of something low lying among taller growth (a fallen tree, a boulder in the forest), 0..255.</param>
/// <param name="LowCoverHeightCm">How high that low cover reaches; above it only <see cref="Cover"/> counts.</param>
/// <param name="LowConcealmentPerM">How much that low thing hides, per metre, below its top.</param>
public record struct CellData(
    short GroundHeightCm,
    short ObstacleHeightCm,
    byte ConcealmentPerM,
    byte Cover,
    ushort TerrainId,
    byte ExtraMoveCost = 0,
    byte LowCover = 0,
    short LowCoverHeightCm = 0,
    byte LowConcealmentPerM = 0)
{
    public const byte Impassable = 255;

    public readonly bool IsPassable => ExtraMoveCost != Impassable;

    /// <summary>Movement time multiplier in percent (100 = normal ground).</summary>
    public readonly int MoveCostPct => 100 + ExtraMoveCost;
}
