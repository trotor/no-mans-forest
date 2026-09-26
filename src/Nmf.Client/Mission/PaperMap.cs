using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Mission;

/// <summary>An RGBA image of a map drawn like a topographic paper map.</summary>
public sealed record PaperMapImage(int Width, int Height, int MetresPerPixel, byte[] Rgba);

/// <summary>
/// Draws the whole map once as a paper map (spec 2026-09-26-missions-design §5): terrain colours, 5 m brown contours with
/// every fifth one darker, roads, the bog in blue stripes, lakes, boulders as dots.
/// </summary>
public static class PaperMap
{
    private static readonly (byte R, byte G, byte B) Open = (243, 236, 205);
    private static readonly (byte R, byte G, byte B) Forest = (206, 228, 186);
    private static readonly (byte R, byte G, byte B) Swamp = (226, 234, 222);
    private static readonly (byte R, byte G, byte B) SwampLine = (92, 136, 188);
    private static readonly (byte R, byte G, byte B) Water = (150, 192, 224);
    private static readonly (byte R, byte G, byte B) Road = (170, 92, 52);
    private static readonly (byte R, byte G, byte B) Contour = (182, 126, 78);
    private static readonly (byte R, byte G, byte B) IndexContour = (140, 88, 48);
    private static readonly (byte R, byte G, byte B) Rock = (40, 38, 36);
    private static readonly (byte R, byte G, byte B) Bush = (150, 190, 128);
    private const int ContourCm = 500;

    public static PaperMapImage Render(GridMap map, int metresPerPixel)
    {
        int w = (map.Width + metresPerPixel - 1) / metresPerPixel;
        int h = (map.Height + metresPerPixel - 1) / metresPerPixel;
        var rgba = new byte[w * h * 4];
        var names = map.TerrainNames;

        CellData Sample(int px, int py) =>
            map[new CellCoord(Math.Min(map.Width - 1, px * metresPerPixel + metresPerPixel / 2), Math.Min(map.Height - 1, py * metresPerPixel + metresPerPixel / 2))];

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                var cell = Sample(px, py);
                string terrain = names[cell.TerrainId];
                var colour = terrain switch
                {
                    "forest" => Forest,
                    "water" => Water,
                    "road" => Road,
                    "swamp" => py % 3 == 0 ? SwampLine : Swamp,
                    _ => Open,
                };
                if (terrain != "water")
                {
                    if (!cell.IsPassable && cell.ObstacleHeightCm >= 100)
                        colour = Rock;
                    else if (cell.ObstacleHeightCm is > 0 and < 150 && terrain != "forest" && terrain != "road")
                        colour = Bush;
                    int level = cell.GroundHeightCm / ContourCm;
                    int right = px + 1 < w ? Sample(px + 1, py).GroundHeightCm / ContourCm : level;
                    int down = py + 1 < h ? Sample(px, py + 1).GroundHeightCm / ContourCm : level;
                    if (right != level || down != level)
                        colour = Math.Max(Math.Max(right, down), level) % 5 == 0 ? IndexContour : Contour;
                }
                // A faint, fixed paper grain.
                int grain = (int)((uint)(px * 73856093 ^ py * 19349663) % 7) - 3;
                int i = (py * w + px) * 4;
                rgba[i] = Clamp(colour.R + grain);
                rgba[i + 1] = Clamp(colour.G + grain);
                rgba[i + 2] = Clamp(colour.B + grain);
                rgba[i + 3] = 255;
            }
        }
        return new PaperMapImage(w, h, metresPerPixel, rgba);
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
}
