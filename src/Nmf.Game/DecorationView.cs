using System.Collections.Generic;
using Godot;
using Nmf.Client.Art;
using GridMap = Nmf.Sim.World.GridMap;

namespace Nmf.Game;

/// <summary>
/// Rocks, bushes and all shadows under the soldiers; tree canopies over them, fading near visible soldiers.
/// Drawn in 32 × 32-cell chunks so a 1 km map is a few thousand culled canvas items, not hundreds of thousands of nodes.
/// </summary>
public sealed partial class DecorationView
{
    private const float CanopyAlpha = 0.8f;
    private const float FadePerSecond = 4f;
    private const float FadedCanopyAlpha = 0.35f;
    private const float FadeRadiusPx = 2.5f * Coords.PixelsPerCell;
    private const int ChunkCells = 32;
    private const float ChunkPx = ChunkCells * Coords.PixelsPerCell;

    private readonly Dictionary<(int, int), CanopyChunk> _canopyChunks = [];

    public Node2D LowLayer { get; } = new() { Name = "LowObjects" };
    public Node2D CanopyLayer { get; } = new() { Name = "Canopies" };

    /// <summary>One sprite to draw: a region of a texture at a place, with its own tint.</summary>
    private sealed class Item(Texture2D texture, Rect2 target, Rect2? source, Color modulate)
    {
        public Texture2D Texture { get; } = texture;
        public Rect2 Target { get; } = target;
        public Rect2? Source { get; } = source;
        public Color Modulate { get; set; } = modulate;
        public Vector2 Centre => Target.GetCenter();
    }

    private partial class Chunk : Node2D
    {
        public List<Item> Items { get; } = [];

        public override void _Draw()
        {
            foreach (var item in Items)
            {
                if (item.Source is { } source)
                    DrawTextureRectRegion(item.Texture, item.Target, source, item.Modulate);
                else
                    DrawTextureRect(item.Texture, item.Target, false, item.Modulate);
            }
        }
    }

    private sealed partial class CanopyChunk : Chunk
    {
        public Rect2 Bounds { get; set; }
        public bool Fading { get; set; }
    }

    public void Build(GridMap map, ArtLibrary art)
    {
        var lowChunks = new Dictionary<(int, int), Chunk>();
        foreach (var decoration in Decorations.Place(map, art.VariantCounts))
        {
            var texture = art.Object(decoration.Kind);
            int size = art.ObjectSize(decoration.Kind);
            var position = Coords.ToPixels(decoration.PositionCm);
            bool tree = decoration.Kind is DecorationKind.Spruce or DecorationKind.Birch;
            var key = ((int)(position.X / ChunkPx), (int)(position.Y / ChunkPx));

            if (!lowChunks.TryGetValue(key, out var low))
            {
                low = new Chunk { TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                lowChunks[key] = low;
                LowLayer.AddChild(low);
            }
            float shadowScale = size / 64f * (tree ? 1.05f : 0.9f);
            var shadowSize = new Vector2(64 * shadowScale, 64 * shadowScale * 0.8f);
            var shadowCentre = position + new Vector2(size * 0.12f, size * 0.16f);
            low.Items.Add(new Item(art.Shadow, new Rect2(shadowCentre - shadowSize / 2, shadowSize), null,
                new Color(1, 1, 1, tree ? 0.75f : 0.6f)));

            var sprite = new Item(texture, new Rect2(position - new Vector2(size, size) / 2, size, size),
                new Rect2(decoration.Variant * size, 0, size, size), new Color(1, 1, 1, tree ? CanopyAlpha : 1f));
            if (!tree)
            {
                low.Items.Add(sprite);
                continue;
            }
            if (!_canopyChunks.TryGetValue(key, out var canopy))
            {
                canopy = new CanopyChunk
                {
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    Bounds = new Rect2(key.Item1 * ChunkPx, key.Item2 * ChunkPx, ChunkPx, ChunkPx).Grow(FadeRadiusPx + 64),
                };
                _canopyChunks[key] = canopy;
                CanopyLayer.AddChild(canopy);
            }
            canopy.Items.Add(sprite);
        }
    }

    /// <summary>Fades canopies near soldiers; only chunks next to a soldier, or still fading, are touched and redrawn.</summary>
    public void UpdateCanopyFade(IReadOnlyList<Vector2> soldierPixels, float delta)
    {
        foreach (var chunk in _canopyChunks.Values)
        {
            bool nearAny = false;
            foreach (var unit in soldierPixels)
            {
                if (chunk.Bounds.HasPoint(unit))
                {
                    nearAny = true;
                    break;
                }
            }
            if (!nearAny && !chunk.Fading)
                continue;
            bool changed = false, stillFading = false;
            foreach (var canopy in chunk.Items)
            {
                bool near = false;
                if (nearAny)
                {
                    foreach (var unit in soldierPixels)
                    {
                        if (canopy.Centre.DistanceSquaredTo(unit) < FadeRadiusPx * FadeRadiusPx)
                        {
                            near = true;
                            break;
                        }
                    }
                }
                var target = near ? FadedCanopyAlpha : CanopyAlpha;
                var m = canopy.Modulate;
                if (m.A == target)
                    continue;
                canopy.Modulate = new Color(m.R, m.G, m.B, Mathf.MoveToward(m.A, target, FadePerSecond * delta));
                changed = true;
                stillFading |= canopy.Modulate.A != CanopyAlpha;
            }
            chunk.Fading = stillFading || nearAny;
            if (changed)
                chunk.QueueRedraw();
        }
    }
}
