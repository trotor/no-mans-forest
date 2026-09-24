using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client;

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>Placeholder map colours until real tile art exists.</summary>
public static class TerrainPalette
{
    private static readonly Rgb Rock = new(128, 128, 126);
    private static readonly Rgb Bush = new(40, 90, 35);

    public static Rgb ColorFor(string terrain) => terrain switch
    {
        "grass" => new Rgb(104, 138, 66),
        "forest" => new Rgb(46, 82, 44),
        "swamp" => new Rgb(88, 104, 84),
        "road" => new Rgb(150, 128, 92),
        _ => new Rgb(255, 0, 255),
    };

    public static Rgb CellColor(GridMap map, CellCoord c)
    {
        var cell = map[c];
        var color = !cell.IsPassable ? Rock : ColorFor(map.TerrainNames[cell.TerrainId]);
        if (cell.IsPassable && cell.ObstacleHeightCm is > 0 and <= 150)
            color = Blend(color, Bush);
        int shade = 88 + Math.Clamp((int)cell.GroundHeightCm, 0, 300) * 12 / 300;
        int jitter = (int)((uint)((c.X * 73856093) ^ (c.Y * 19349663)) % 9) - 4;
        return Scale(color, shade + jitter);
    }

    private static Rgb Blend(Rgb a, Rgb b) =>
        new((byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));

    private static Rgb Scale(Rgb c, int percent) =>
        new(Channel(c.R, percent), Channel(c.G, percent), Channel(c.B, percent));

    private static byte Channel(byte value, int percent) => (byte)Math.Clamp(value * percent / 100, 0, 255);
}
