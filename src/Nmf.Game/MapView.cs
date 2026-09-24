using Godot;
using Nmf.Client;
using Nmf.Sim.Core;
using Nmf.Sim.World;
using GridMap = Nmf.Sim.World.GridMap;

namespace Nmf.Game;

/// <summary>Static terrain: one texel per 1 m cell, scaled up with nearest filtering.</summary>
public partial class MapView : Sprite2D
{
    public static MapView Create(GridMap map)
    {
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var c = TerrainPalette.CellColor(map, new CellCoord(x, y));
                image.SetPixel(x, y, Color.Color8(c.R, c.G, c.B));
            }
        }
        return new MapView
        {
            Texture = ImageTexture.CreateFromImage(image),
            Centered = false,
            Scale = new Vector2(Coords.PixelsPerCell, Coords.PixelsPerCell),
            TextureFilter = TextureFilterEnum.Nearest,
        };
    }
}
