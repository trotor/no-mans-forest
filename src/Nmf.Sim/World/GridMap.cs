using Nmf.Sim.Core;

namespace Nmf.Sim.World;

/// <summary>Grid of 1 m cells. Row-major, origin at the top-left corner.</summary>
public sealed class GridMap
{
    public const int MaxSideCells = 4096;

    private readonly CellData[] _cells;

    public GridMap(int width, int height, IReadOnlyList<string> terrainNames, MapFeatures? features = null)
    {
        if (width is <= 0 or > MaxSideCells)
            throw new ArgumentOutOfRangeException(nameof(width), width, $"Width must be 1..{MaxSideCells}.");
        if (height is <= 0 or > MaxSideCells)
            throw new ArgumentOutOfRangeException(nameof(height), height, $"Height must be 1..{MaxSideCells}.");
        if (terrainNames.Count == 0)
            throw new ArgumentException("At least one terrain name (id 0) is required.", nameof(terrainNames));

        Width = width;
        Height = height;
        TerrainNames = terrainNames;
        Features = features ?? MapFeatures.Empty;
        _cells = new CellData[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public int WidthCm => Width * SimConstants.CentimetersPerCell;
    public int HeightCm => Height * SimConstants.CentimetersPerCell;
    public IReadOnlyList<string> TerrainNames { get; }
    public MapFeatures Features { get; }

    public bool InBounds(CellCoord c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    public bool Contains(Vec2 p) => p.X >= 0 && p.Y >= 0 && p.X < WidthCm && p.Y < HeightCm;

    public ref CellData this[CellCoord c]
    {
        get
        {
            if (!InBounds(c))
                throw new ArgumentOutOfRangeException(nameof(c), $"Cell ({c.X},{c.Y}) is outside the {Width}x{Height} map.");
            return ref _cells[c.Y * Width + c.X];
        }
    }

    public CellData CellAt(Vec2 posCm) => this[posCm.ToCell()];

    /// <summary>All cells, row-major, for tight loops.</summary>
    internal ReadOnlySpan<CellData> Cells => _cells;
}
