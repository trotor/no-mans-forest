using Godot;
using Nmf.Client;

namespace Nmf.Game;

/// <summary>Dark overlay on cells the player's units cannot currently see (rebuilt from a byte buffer, not per pixel).</summary>
public partial class FogView : Sprite2D
{
    private static readonly byte[] HiddenRgba = [5, 8, 13, 140];

    private Image _image = null!;
    private ImageTexture _texture = null!;
    private byte[] _pixels = [];
    private int _version = -1;

    public GameSession Session { get; set; } = null!;

    public override void _Ready()
    {
        var map = Session.Sim.Map;
        _pixels = new byte[map.Width * map.Height * 4];
        _image = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rgba8, _pixels);
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
        var visible = Session.VisibleCells;
        for (int i = 0; i < visible.Length; i++)
        {
            int p = i * 4;
            if (visible[i])
            {
                _pixels[p + 3] = 0;
            }
            else
            {
                _pixels[p] = HiddenRgba[0];
                _pixels[p + 1] = HiddenRgba[1];
                _pixels[p + 2] = HiddenRgba[2];
                _pixels[p + 3] = HiddenRgba[3];
            }
        }
        _image.SetData(_image.GetWidth(), _image.GetHeight(), false, Image.Format.Rgba8, _pixels);
        _texture.Update(_image);
    }
}
