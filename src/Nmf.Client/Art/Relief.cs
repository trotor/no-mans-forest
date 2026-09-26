using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Art;

/// <summary>
/// How the lie of the land lights the picture (presentation only): knolls and ridges catch the light from the north-west
/// and stand a little lighter than the ground around them, hollows fall into shade. Worked out from the landforms —
/// heights smoothed over a few metres — so a knoll shows but a hummock does not; ground and trees both take it.
/// </summary>
public sealed class Relief
{
    public const float Darkest = 0.62f;
    public const float Lightest = 1.35f;
    /// <summary>Box blur radius (cells, applied twice) for the landforms that are shaded.</summary>
    private const int LandformRadius = 4;
    /// <summary>Box blur radius (cells, applied twice) for the ground a knoll is measured against.</summary>
    private const int SurroundRadius = 30;
    /// <summary>Slopes drawn steeper than they are: a naive picture where a 5 m rise reads at a glance.</summary>
    private const float Exaggeration = 5f;
    /// <summary>Metres above (below) the ground around at which a knoll (hollow) is at its lightest (darkest).</summary>
    private const float FullProminenceM = 4f;
    private const float ProminenceTint = 0.18f;

    private static readonly (float X, float Y, float Z) LightFrom = Normalize(-0.55f, -0.7f, 0.9f);

    public int Width { get; }
    public int Height { get; }
    /// <summary>A brightness factor per cell, row by row: 1 on flat ground.</summary>
    public float[] Light { get; }
    /// <summary>
    /// How far a cell stands above (up to 1) or below (down to −1) the ground around it: the dry knolls and ridges where
    /// pale lichen and heather show between the pines, the damp hollows dark with moss.
    /// </summary>
    public float[] Rise { get; }

    private Relief(int width, int height, float[] light, float[] rise) => (Width, Height, Light, Rise) = (width, height, light, rise);

    public static Relief Of(GridMap map)
    {
        int w = map.Width, h = map.Height;
        var metres = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                metres[y * w + x] = map[new CellCoord(x, y)].GroundHeightCm / 100f;
        var landform = Blur(metres, w, h, LandformRadius);
        var surround = Blur(landform, w, h, SurroundRadius);

        var light = new float[w * h];
        var rise = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float gx = (landform[y * w + Math.Min(w - 1, x + 1)] - landform[y * w + Math.Max(0, x - 1)]) / 2f;
                float gy = (landform[Math.Min(h - 1, y + 1) * w + x] - landform[Math.Max(0, y - 1) * w + x]) / 2f;
                var (nx, ny, nz) = Normalize(-gx * Exaggeration, -gy * Exaggeration, 1f);
                float shade = (nx * LightFrom.X + ny * LightFrom.Y + nz * LightFrom.Z) / LightFrom.Z;
                float prominence = Math.Clamp((landform[y * w + x] - surround[y * w + x]) / FullProminenceM, -1f, 1f);
                rise[y * w + x] = prominence;
                light[y * w + x] = Math.Clamp(shade * (1f + prominence * ProminenceTint), Darkest, Lightest);
            }
        }
        return new Relief(w, h, light, rise);
    }

    public float LightAtCell(int x, int y) => Light[Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];

    public float RiseAtCell(int x, int y) => Rise[Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];

    public float RiseAt(Vec2 positionCm)
    {
        var cell = positionCm.ToCell();
        return RiseAtCell(cell.X, cell.Y);
    }

    public float LightAt(Vec2 positionCm)
    {
        var cell = positionCm.ToCell();
        return LightAtCell(cell.X, cell.Y);
    }

    /// <summary>Two passes of a running-sum box blur each way: close to a Gaussian, linear in the map size.</summary>
    private static float[] Blur(float[] values, int w, int h, int radius)
    {
        var a = (float[])values.Clone();
        var b = new float[a.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            BoxLines(a, b, w, h, radius, horizontal: true);
            BoxLines(b, a, w, h, radius, horizontal: false);
        }
        return a;
    }

    private static void BoxLines(float[] from, float[] to, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h;
        for (int line = 0; line < lines; line++)
        {
            int Index(int i) => horizontal ? line * w + Math.Clamp(i, 0, length - 1) : Math.Clamp(i, 0, length - 1) * w + line;
            float sum = 0;
            for (int i = -radius; i <= radius; i++)
                sum += from[Index(i)];
            for (int i = 0; i < length; i++)
            {
                to[Index(i)] = sum / (2 * radius + 1);
                sum += from[Index(i + radius + 1)] - from[Index(i - radius)];
            }
        }
    }

    private static (float X, float Y, float Z) Normalize(float x, float y, float z)
    {
        float length = MathF.Sqrt(x * x + y * y + z * z);
        return (x / length, y / length, z / length);
    }
}
