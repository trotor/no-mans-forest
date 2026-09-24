using Godot;
using Nmf.Client;

namespace Nmf.Game;

/// <summary>Dark overlay on cells the player's units cannot currently see.</summary>
public partial class FogView : Sprite2D
{
    private static readonly Color HiddenColor = new(0.02f, 0.03f, 0.05f, 0.55f);

    private Image _image = null!;
    private ImageTexture _texture = null!;
    private int _version = -1;

    public GameSession Session { get; set; } = null!;

    public override void _Ready()
    {
        var map = Session.Sim.Map;
        _image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;
        Centered = false;
        Scale = new Vector2(Coords.PixelsPerCell, Coords.PixelsPerCell);
        TextureFilter = TextureFilterEnum.Linear;
        Refresh();
    }

    public void Refresh()
    {
        if (_version == Session.FogVersion)
            return;
        _version = Session.FogVersion;
        var map = Session.Sim.Map;
        var visible = Session.VisibleCells;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                _image.SetPixel(x, y, visible[y * map.Width + x] ? Colors.Transparent : HiddenColor);
        _texture.Update(_image);
    }
}
