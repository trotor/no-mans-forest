using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Mission;

/// <summary>A rectangle of map cells.</summary>
public readonly record struct MapRegion(int X, int Y, int Width, int Height)
{
    public static MapRegion Whole(GridMap map) => new(0, 0, map.Width, map.Height);
}

/// <summary>An RGBA image of part of a map; one metre is <see cref="PixelsPerMetre"/> pixels.</summary>
public sealed record PaperMapImage(int Width, int Height, int PixelsPerMetre, MapRegion Region, byte[] Rgba);

/// <summary>
/// Draws a map like a Finnish topographic sheet (spec 2026-09-26-maps-design §1): pale forest, yellow open land, the bog
/// in blue dashes, lakes with a shore line, cased roads, boulders, smooth brown contours every 5 m (every fifth bolder)
/// over a faint hillshade, and a 100 m grid. Heights are smoothed first, so 25 cm steps and hummocks make no contours;
/// terrain edges are blended, so nothing is drawn in stairs.
/// </summary>
public static class PaperMap
{
    private static readonly Rgb Paper = new(246, 241, 224);
    private static readonly Rgb Open = new(247, 231, 168);
    private static readonly Rgb Forest = new(222, 235, 203);
    private static readonly Rgb BogTint = new(223, 234, 231);
    private static readonly Rgb BogDash = new(66, 122, 190);
    private static readonly Rgb Water = new(166, 205, 233);
    private static readonly Rgb Shore = new(58, 116, 178);
    private static readonly Rgb RoadFill = new(222, 158, 92);
    private static readonly Rgb RoadCasing = new(96, 64, 40);
    private static readonly Rgb Contour = new(178, 120, 70);
    private static readonly Rgb IndexContour = new(146, 92, 52);
    private static readonly Rgb Rock = new(58, 54, 50);
    private static readonly Rgb Bush = new(186, 212, 160);
    private static readonly Rgb Grid = new(120, 110, 92);

    private const float ContourIntervalM = 5f;
    private const int Margin = 8;

    public static PaperMapImage Render(GridMap map, MapRegion region, int pixelsPerMetre, bool paperGrain = true)
    {
        int ppm = Math.Max(1, pixelsPerMetre);
        var f = new Fields(map, region);
        int w = region.Width * ppm, h = region.Height * ppm;
        var rgba = new byte[w * h * 4];
        float lineScale = Math.Max(1f, ppm * 0.75f);

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                // Cell-centred world coordinates, in metres.
                float wx = region.X + (px + 0.5f) / ppm - 0.5f;
                float wy = region.Y + (py + 0.5f) / ppm - 0.5f;

                float forest = f.Sample(f.Forest, wx, wy);
                float bog = f.Sample(f.Bog, wx, wy);
                float water = f.Sample(f.Water, wx, wy);
                float road = f.Sample(f.Road, wx, wy);
                float casing = f.Sample(f.RoadWide, wx, wy);
                float rock = f.Sample(f.Rock, wx, wy);
                float bush = f.Sample(f.Bush, wx, wy);

                var c = Rgb.Lerp(Open, Forest, forest);
                c = Rgb.Lerp(c, BogTint, bog);
                // Bog: short horizontal dashes, rows 5 px apart, staggered.
                float dashY = Frac((py + 0.5f) / (5f * ppm / 2f));
                float dashX = Frac((px + ((py / (5 * ppm / 2)) % 2) * 7f * ppm / 2f) / (14f * ppm / 2f));
                if (dashX < 0.62f)
                    c = Rgb.Lerp(c, BogDash, bog * Line(Math.Abs(dashY - 0.5f) * 5f * ppm / 2f, 0.55f * lineScale));
                c = Rgb.Lerp(c, Bush, bush * 0.6f);

                // Faint hillshade, light from the north-west.
                var (gx, gy) = f.Gradient(wx, wy);
                float shade = Math.Clamp(1f + (-(gx * 0.55f) - gy * 0.7f) * 1.3f, 0.9f, 1.06f);
                c = c.Scale(1f + (shade - 1f) * 0.4f * (1f - water));

                // Contours: distance to the nearest 5 m line, measured along the slope, gives an even, smooth stroke.
                float slope = MathF.Sqrt(gx * gx + gy * gy);
                if (slope > 0.004f && water < 0.5f)
                {
                    float v = f.SampleHeight(wx, wy) / ContourIntervalM;
                    float nearest = MathF.Round(v);
                    float distPx = Math.Abs(v - nearest) * ContourIntervalM / slope * ppm;
                    bool index = ((int)nearest % 5 + 5) % 5 == 0;
                    float alpha = Line(distPx, (index ? 0.95f : 0.5f) * lineScale);
                    c = Rgb.Lerp(c, index ? IndexContour : Contour, alpha * 0.9f);
                }

                c = Rgb.Lerp(c, Water, Smooth(water, 0.35f, 0.6f));
                c = Rgb.Lerp(c, Shore, (1f - Math.Abs(water - 0.5f) * 4f) is var s && s > 0 ? s * 0.9f : 0f);
                c = Rgb.Lerp(c, RoadCasing, Smooth(casing, 0.12f, 0.3f));
                c = Rgb.Lerp(c, RoadFill, Smooth(road, 0.35f, 0.6f));
                c = Rgb.Lerp(c, Rock, Smooth(rock, 0.3f, 0.55f));

                // 100 m grid.
                float gridX = DistanceToMultiple(wx + 0.5f, 100f) * ppm, gridY = DistanceToMultiple(wy + 0.5f, 100f) * ppm;
                c = Rgb.Lerp(c, Grid, 0.32f * Math.Max(Line(gridX, 0.45f * lineScale), Line(gridY, 0.45f * lineScale)));

                c = Rgb.Lerp(Paper, c, 0.96f);
                int grain = paperGrain ? (int)((uint)((px >> 1) * 73856093 ^ (py >> 1) * 19349663) % 7) - 3 : 0;
                int i = (py * w + px) * 4;
                rgba[i] = Byte(c.R + grain);
                rgba[i + 1] = Byte(c.G + grain);
                rgba[i + 2] = Byte(c.B + grain);
                rgba[i + 3] = 255;
            }
        }
        return new PaperMapImage(w, h, ppm, region, rgba);
    }

    /// <summary>Anti-aliased line coverage for a pixel <paramref name="distance"/> from the centre of a line of half width <paramref name="halfWidth"/>.</summary>
    private static float Line(float distance, float halfWidth) => Math.Clamp(halfWidth + 0.5f - distance, 0f, 1f);

    private static float Smooth(float v, float from, float to) => Math.Clamp((v - from) / (to - from), 0f, 1f);

    private static float Frac(float v) => v - MathF.Floor(v);

    private static float DistanceToMultiple(float v, float step)
    {
        float r = v - MathF.Floor(v / step) * step;
        return Math.Min(r, step - r);
    }

    private static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    private readonly record struct Rgb(float R, float G, float B)
    {
        public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        public Rgb Scale(float k) => new(R * k, G * k, B * k);
    }

    /// <summary>Terrain masks and smoothed heights over the region plus a margin, sampled bilinearly.</summary>
    private sealed class Fields
    {
        private readonly int _x0, _y0, _w, _h;
        public readonly float[] Forest, Bog, Water, Road, RoadWide, Rock, Bush, Height;

        public Fields(GridMap map, MapRegion region)
        {
            _x0 = Math.Max(0, region.X - Margin);
            _y0 = Math.Max(0, region.Y - Margin);
            int x1 = Math.Min(map.Width, region.X + region.Width + Margin);
            int y1 = Math.Min(map.Height, region.Y + region.Height + Margin);
            _w = x1 - _x0;
            _h = y1 - _y0;
            int n = _w * _h;
            Forest = new float[n]; Bog = new float[n]; Water = new float[n]; Road = new float[n];
            Rock = new float[n]; Bush = new float[n]; Height = new float[n];
            var names = map.TerrainNames;
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    var cell = map[new CellCoord(_x0 + x, _y0 + y)];
                    int i = y * _w + x;
                    string t = names[cell.TerrainId];
                    Forest[i] = t == "forest" ? 1 : 0;
                    Bog[i] = t == "swamp" ? 1 : 0;
                    Water[i] = t == "water" ? 1 : 0;
                    Road[i] = t == "road" ? 1 : 0;
                    Rock[i] = t != "water" && !cell.IsPassable && cell.ObstacleHeightCm >= 100 ? 1 : 0;
                    Bush[i] = t is not ("forest" or "road" or "water") && cell.ObstacleHeightCm is > 0 and < 150 ? 1 : 0;
                    Height[i] = cell.GroundHeightCm / 100f;
                }
            }
            RoadWide = Blur(Road, 1, 1);
            Blur(Forest, 1, 1, inPlace: true);
            Blur(Bog, 1, 1, inPlace: true);
            Blur(Water, 1, 1, inPlace: true);
            Blur(Height, 3, 2, inPlace: true);
        }

        public float Sample(float[] field, float x, float y)
        {
            float fx = Math.Clamp(x - _x0, 0, _w - 1), fy = Math.Clamp(y - _y0, 0, _h - 1);
            int ix = Math.Min((int)fx, _w - 2 < 0 ? 0 : _w - 2), iy = Math.Min((int)fy, _h - 2 < 0 ? 0 : _h - 2);
            float tx = fx - ix, ty = fy - iy;
            int ix1 = Math.Min(ix + 1, _w - 1), iy1 = Math.Min(iy + 1, _h - 1);
            float a = field[iy * _w + ix], b = field[iy * _w + ix1], c = field[iy1 * _w + ix], d = field[iy1 * _w + ix1];
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }

        public float SampleHeight(float x, float y) => Sample(Height, x, y);

        /// <summary>Height change per metre east and south.</summary>
        public (float Gx, float Gy) Gradient(float x, float y) =>
            ((SampleHeight(x + 0.5f, y) - SampleHeight(x - 0.5f, y)), (SampleHeight(x, y + 0.5f) - SampleHeight(x, y - 0.5f)));

        /// <summary>Box blur of the given radius, repeated (several passes approach a Gaussian).</summary>
        private float[] Blur(float[] field, int radius, int passes, bool inPlace = false)
        {
            var src = inPlace ? field : (float[])field.Clone();
            var tmp = new float[src.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        float sum = 0; int count = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int xx = x + k;
                            if (xx < 0 || xx >= _w) continue;
                            sum += src[y * _w + xx]; count++;
                        }
                        tmp[y * _w + x] = sum / count;
                    }
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        float sum = 0; int count = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int yy = y + k;
                            if (yy < 0 || yy >= _h) continue;
                            sum += tmp[yy * _w + x]; count++;
                        }
                        src[y * _w + x] = sum / count;
                    }
            }
            return src;
        }
    }
}

/// <summary>The part of the map a mission map shows: its zones and routes with a margin, square, inside the map.</summary>
public static class MissionMapFrame
{
    public static MapRegion Region(GridMap map, IEnumerable<Vec2> pointsCm, int marginCells)
    {
        var cells = pointsCm.Select(p => p.ToCell()).ToList();
        if (cells.Count == 0)
            return MapRegion.Whole(map);
        int x0 = cells.Min(c => c.X) - marginCells, x1 = cells.Max(c => c.X) + marginCells;
        int y0 = cells.Min(c => c.Y) - marginCells, y1 = cells.Max(c => c.Y) + marginCells;
        int side = Math.Min(Math.Max(x1 - x0, y1 - y0), Math.Min(map.Width, map.Height));
        int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        int x = Math.Clamp(cx - side / 2, 0, map.Width - side);
        int y = Math.Clamp(cy - side / 2, 0, map.Height - side);
        return new MapRegion(x, y, side, side);
    }
}
