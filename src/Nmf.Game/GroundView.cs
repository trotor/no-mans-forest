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
        var terrain = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.R8);
        var height = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.R8);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var cell = map[new CellCoord(x, y)];
                terrain.SetPixel(x, y, new Color(TerrainArt.SlotFor(map.TerrainNames[cell.TerrainId]) / 255f, 0, 0));
                height.SetPixel(x, y, new Color(Mathf.Clamp(cell.GroundHeightCm / 300f, 0f, 1f), 0, 0));
            }
        }
        var terrainTexture = ImageTexture.CreateFromImage(terrain);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ground.gdshader") };
        material.SetShaderParameter("terrain_map", terrainTexture);
        material.SetShaderParameter("height_map", ImageTexture.CreateFromImage(height));
        material.SetShaderParameter("tex_grass", art.TerrainTextures[0]);
        material.SetShaderParameter("tex_forest", art.TerrainTextures[1]);
        material.SetShaderParameter("tex_swamp", art.TerrainTextures[2]);
        material.SetShaderParameter("tex_road", art.TerrainTextures[3]);
        material.SetShaderParameter("map_cells", new Vector2(map.Width, map.Height));
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
