using Godot;
using Nmf.Client.Art;
using Nmf.Sim.Core;
using GridMap = Nmf.Sim.World.GridMap;

namespace Nmf.Game;

/// <summary>The whole ground as one quad drawn by the splat + hillshade shader.</summary>
public partial class GroundView : Sprite2D
{
    public static GroundView Create(GridMap map, ArtLibrary art)
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
        var terrain = Image.CreateFromData(map.Width, map.Height, false, Image.Format.R8, slots);
        var height = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rh, heights);
        var terrainTexture = ImageTexture.CreateFromImage(terrain);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ground.gdshader") };
        material.SetShaderParameter("terrain_map", terrainTexture);
        material.SetShaderParameter("height_map", ImageTexture.CreateFromImage(height));
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
