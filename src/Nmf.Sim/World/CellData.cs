namespace Nmf.Sim.World;

/// <summary>Static terrain values of one 1 m cell.</summary>
/// <param name="GroundHeightCm">Ground elevation.</param>
/// <param name="ObstacleHeightCm">Height of what stands on the cell (trees, rocks, walls).</param>
/// <param name="ConcealmentPerM">How much one metre of this cell blocks sight, 0..255 = 0..1.</param>
/// <param name="Cover">How well the cell stops bullets, 0..255 = 0..1.</param>
/// <param name="TerrainId">Index into <see cref="GridMap.TerrainNames"/>.</param>
public record struct CellData(
    short GroundHeightCm,
    short ObstacleHeightCm,
    byte ConcealmentPerM,
    byte Cover,
    ushort TerrainId);
