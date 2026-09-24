using System.Collections.Generic;
using Godot;
using Nmf.Client.Art;
using GridMap = Nmf.Sim.World.GridMap;

namespace Nmf.Game;

/// <summary>Rocks, bushes and all shadows under the soldiers; tree canopies over them, fading near visible soldiers.</summary>
public sealed class DecorationView
{
    private const float CanopyAlpha = 0.8f;
    private const float FadePerSecond = 4f;
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
            int size = art.ObjectSize(decoration.Kind);
            var position = Coords.ToPixels(decoration.PositionCm);
            bool tree = decoration.Kind is DecorationKind.Spruce or DecorationKind.Birch;
            var layer = tree ? CanopyLayer : LowLayer;

            float shadowScale = size / 64f * (tree ? 1.05f : 0.9f);
            LowLayer.AddChild(new Sprite2D
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
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                Modulate = new Color(1, 1, 1, tree ? CanopyAlpha : 1f),
            };
            layer.AddChild(sprite);
            if (tree)
                _canopies.Add(sprite);
        }
    }

    public void UpdateCanopyFade(IReadOnlyList<Vector2> soldierPixels, float delta)
    {
        foreach (var canopy in _canopies)
        {
            bool near = false;
            foreach (var unit in soldierPixels)
            {
                if (canopy.Position.DistanceSquaredTo(unit) < FadeRadiusPx * FadeRadiusPx)
                {
                    near = true;
                    break;
                }
            }
            var target = near ? FadedCanopyAlpha : CanopyAlpha;
            var m = canopy.Modulate;
            if (m.A != target)
                canopy.Modulate = new Color(m.R, m.G, m.B, Mathf.MoveToward(m.A, target, FadePerSecond * delta));
        }
    }
}
