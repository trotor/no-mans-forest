using System.Collections.Generic;
using Godot;
using Nmf.Client.Art;
using GridMap = Nmf.Sim.World.GridMap;

namespace Nmf.Game;

/// <summary>Rocks and bushes under the soldiers; tree canopies over them, fading near own soldiers.</summary>
public partial class DecorationView : Node2D
{
    private const float CanopyAlpha = 0.85f;
    private const float FadedCanopyAlpha = 0.35f;
    private const float FadeRadiusPx = 2.5f * Coords.PixelsPerCell;

    private readonly List<Sprite2D> _canopies = [];

    public Node2D LowLayer { get; } = new() { Name = "LowObjects" };
    public Node2D CanopyLayer { get; } = new() { Name = "Canopies" };

    public void Build(GridMap map, ArtLibrary art)
    {
        foreach (var decoration in Decorations.Place(map, art.VariantCounts))
        {
            var texture = art.Object(decoration.Kind);
            int size = texture.GetHeight();
            var position = Coords.ToPixels(decoration.PositionCm);
            bool tree = decoration.Kind is DecorationKind.Spruce or DecorationKind.Birch;
            var layer = tree ? CanopyLayer : LowLayer;

            float shadowScale = size / 64f * (tree ? 1.05f : 0.9f);
            layer.AddChild(new Sprite2D
            {
                Texture = art.Shadow,
                Position = position + new Vector2(size * 0.12f, size * 0.16f),
                Scale = new Vector2(shadowScale, shadowScale * 0.8f),
                Modulate = new Color(1, 1, 1, tree ? 0.75f : 0.6f),
            });
            var sprite = new Sprite2D
            {
                Texture = new AtlasTexture { Atlas = texture, Region = new Rect2(decoration.Variant * size, 0, size, size) },
                Position = position,
                TextureFilter = TextureFilterEnum.Nearest,
                Modulate = new Color(1, 1, 1, tree ? CanopyAlpha : 1f),
            };
            layer.AddChild(sprite);
            if (tree)
                _canopies.Add(sprite);
        }
    }

    public void UpdateCanopyFade(IReadOnlyList<Vector2> ownUnitPixels)
    {
        foreach (var canopy in _canopies)
        {
            bool near = false;
            foreach (var unit in ownUnitPixels)
            {
                if (canopy.Position.DistanceSquaredTo(unit) < FadeRadiusPx * FadeRadiusPx)
                {
                    near = true;
                    break;
                }
            }
            var target = near ? FadedCanopyAlpha : CanopyAlpha;
            var m = canopy.Modulate;
            canopy.Modulate = new Color(m.R, m.G, m.B, Mathf.MoveToward(m.A, target, 0.08f));
        }
    }
}
