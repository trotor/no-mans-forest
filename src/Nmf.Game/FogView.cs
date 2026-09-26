using Godot;
using Nmf.Client;
using Nmf.Client.Fog;

namespace Nmf.Game;

/// <summary>Dark overlay where the player's men cannot see: one texel per 4 m fog block, filtered for soft edges.</summary>
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
        var fog = Session.Fog;
        _pixels = new byte[fog.Width * fog.Height * 4];
        _image = Image.CreateFromData(fog.Width, fog.Height, false, Image.Format.Rgba8, _pixels);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;
        Centered = false;
        Scale = new Vector2(FogOfWar.BlockCells * Coords.PixelsPerCell, FogOfWar.BlockCells * Coords.PixelsPerCell);
        TextureFilter = TextureFilterEnum.Linear;
        Refresh();
    }

    public void Refresh()
    {
        if (_version == Session.FogVersion)
            return;
        _version = Session.FogVersion;
        var visible = Session.Fog.Visible;
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
