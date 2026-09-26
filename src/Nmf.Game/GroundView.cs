using Godot;
using Nmf.Client.Art;
using Nmf.Sim.Core;
using GridMap = Nmf.Sim.World.GridMap;

namespace Nmf.Game;

/// <summary>The whole ground as one quad drawn by the splat + hillshade shader, lit by the lie of the land (<see cref="Relief"/>).</summary>
public partial class GroundView : Sprite2D
{
    /// <summary>
    /// Heights come in 25 cm steps; a 3 × 3 box blur (for the picture only) keeps gentle slopes from showing as terraces.
    /// </summary>
    private static void SmoothForShading(byte[] halfHeights, int width, int height)
    {
        var values = new float[width * height];
        for (int i = 0; i < values.Length; i++)
            values[i] = (float)System.BitConverter.UInt16BitsToHalf((ushort)(halfHeights[i * 2] | halfHeights[i * 2 + 1] << 8));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
                        sum += values[yy * width + xx];
                        n++;
                    }
                ushort half = System.BitConverter.HalfToUInt16Bits((System.Half)(sum / n));
                halfHeights[(y * width + x) * 2] = (byte)half;
                halfHeights[(y * width + x) * 2 + 1] = (byte)(half >> 8);
            }
        }
    }

    public static GroundView Create(GridMap map, ArtLibrary art, Relief relief)
    {
        // Built from byte buffers: a 1 km map has a million cells, far too many for per-pixel calls.
        int count = map.Width * map.Height;
        var slots = new byte[count];
        var heights = new byte[count * 2];
        int maxHeight = 1;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                maxHeight = System.Math.Max(maxHeight, map[new CellCoord(x, y)].GroundHeightCm);
        var slotOf = new byte[map.TerrainNames.Count];
        for (int i = 0; i < slotOf.Length; i++)
            slotOf[i] = (byte)TerrainArt.SlotFor(map.TerrainNames[i]);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var cell = map[new CellCoord(x, y)];
                int i = y * map.Width + x;
                slots[i] = slotOf[cell.TerrainId];
                // Half-float heights (0..1 of the map's range): smooth hillshade even on 100 m of relief.
                ushort half = System.BitConverter.HalfToUInt16Bits((System.Half)(System.Math.Max(0, (int)cell.GroundHeightCm) / (float)maxHeight));
                heights[i * 2] = (byte)half;
                heights[i * 2 + 1] = (byte)(half >> 8);
            }
        }
        SmoothForShading(heights, map.Width, map.Height);
        var light = new byte[count * 2]; // R: light, G: rise (−1..1 as 0..1)
        for (int i = 0; i < count; i++)
        {
            light[i * 2] = (byte)System.Math.Clamp((int)((relief.Light[i] - Relief.Darkest) / (Relief.Lightest - Relief.Darkest) * 255 + 0.5f), 0, 255);
            light[i * 2 + 1] = (byte)System.Math.Clamp((int)((relief.Rise[i] + 1f) * 127.5f + 0.5f), 0, 255);
        }
        var terrain = Image.CreateFromData(map.Width, map.Height, false, Image.Format.R8, slots);
        var height = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rh, heights);
        var terrainTexture = ImageTexture.CreateFromImage(terrain);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ground.gdshader") };
        material.SetShaderParameter("terrain_map", terrainTexture);
        material.SetShaderParameter("height_map", ImageTexture.CreateFromImage(height));
        material.SetShaderParameter("relief_map", ImageTexture.CreateFromImage(Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rg8, light)));
        material.SetShaderParameter("relief_darkest", Relief.Darkest);
        material.SetShaderParameter("relief_lightest", Relief.Lightest);
        material.SetShaderParameter("tex_grass", art.TerrainTextures[0]);
        material.SetShaderParameter("tex_forest", art.TerrainTextures[1]);
        material.SetShaderParameter("tex_swamp", art.TerrainTextures[2]);
        material.SetShaderParameter("tex_road", art.TerrainTextures[3]);
        material.SetShaderParameter("tex_water", art.TerrainTextures[4]);
        material.SetShaderParameter("map_cells", new Vector2(map.Width, map.Height));
        material.SetShaderParameter("height_range_m", maxHeight / 100f);
        return new GroundView
        {
            Texture = terrainTexture,
            Centered = false,
            Scale = new Vector2(Coords.PixelsPerCell, Coords.PixelsPerCell),
            Material = material,
            TextureFilter = TextureFilterEnum.Nearest,
        };
    }
}
