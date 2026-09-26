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

    /// <summary>
    /// One sprite to draw: a region of a texture at a place, with its own tint — turned, stretched along its length or
    /// mirrored when <see cref="Angle"/>, <see cref="Stretch"/> or <see cref="Flip"/> say so.
    /// </summary>
    private sealed class Item(Texture2D texture, Rect2 target, Rect2? source, Color modulate)
    {
        public Texture2D Texture { get; } = texture;
        public Rect2 Target { get; } = target;
        public Rect2? Source { get; } = source;
        public Color Modulate { get; set; } = modulate;
        public Vector2 Centre => Target.GetCenter();
        public float Angle { get; init; }
        public float Stretch { get; init; } = 1f;
        public bool Flip { get; init; }
    }

    private partial class Chunk : Node2D
    {
        public List<Item> Items { get; } = [];

        public override void _Draw()
        {
            foreach (var item in Items)
            {
                bool transformed = item.Angle != 0 || item.Stretch != 1f || item.Flip;
                var target = item.Target;
                if (transformed)
                {
                    DrawSetTransform(item.Centre, item.Angle, new Vector2(item.Stretch * (item.Flip ? -1 : 1), 1));
                    target = new Rect2(-target.Size / 2, target.Size);
                }
                if (item.Source is { } source)
                    DrawTextureRectRegion(item.Texture, target, source, item.Modulate);
                else
                    DrawTextureRect(item.Texture, target, false, item.Modulate);
                if (transformed)
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }
    }

    /// <summary>A tree's own shade: a touch lighter and warmer, or darker and cooler.</summary>
    private static Color Shade(int shade, float alpha) =>
        new(1f + shade * 0.014f, 1f + shade * 0.01f, 1f + shade * 0.004f, alpha);

    /// <summary>
    /// The light of the slope the thing stands on (<see cref="Relief"/>): trees on a sunlit knoll lighter and warmer,
    /// in a hollow darker and cooler — the same as the ground under them; lighter still on the dry knolls.
    /// </summary>
    private static Color Lit(Color colour, float light, float rise)
    {
        float l = light * (1f + 0.12f * rise); // pines on the dry ground a lighter green
        float warm = Mathf.SmoothStep(0.8f, 1.2f, light);
        return new Color(colour.R * l * Mathf.Lerp(0.93f, 1.05f, warm), colour.G * l * Mathf.Lerp(0.97f, 1f, warm),
            colour.B * l * Mathf.Lerp(1.07f, 0.9f, warm), colour.A);
    }

    private sealed partial class CanopyChunk : Chunk
    {
        public Rect2 Bounds { get; set; }
        public bool Fading { get; set; }
    }

    public void Build(GridMap map, ArtLibrary art, Relief relief)
    {
        var lowChunks = new Dictionary<(int, int), Chunk>();
        foreach (var decoration in Decorations.Place(map, art.VariantCounts))
        {
            var texture = art.Object(decoration.Kind);
            int baseSize = art.ObjectSize(decoration.Kind);
            float size = baseSize * decoration.ScalePct / 100f;
            var position = Coords.ToPixels(decoration.PositionCm);
            bool tree = Decorations.IsTree(decoration.Kind);
            bool flat = decoration.Kind is DecorationKind.Fern or DecorationKind.Moss or DecorationKind.Tuft or DecorationKind.Flowers
                or DecorationKind.Sedge or DecorationKind.Cotton or DecorationKind.Pool or DecorationKind.Puddle;
            var key = ((int)(position.X / ChunkPx), (int)(position.Y / ChunkPx));

            if (!lowChunks.TryGetValue(key, out var low))
            {
                low = new Chunk { TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                lowChunks[key] = low;
                LowLayer.AddChild(low);
            }
            float angle = Mathf.DegToRad(decoration.AngleDeg);
            float stretch = decoration.LengthPct / 100f;
            if (!flat) // ground growth casts no shadow worth drawing
            {
                bool log = decoration.Kind == DecorationKind.Log;
                float shadowScale = size / 64f * (tree ? 1.05f : log ? 1f : 0.9f);
                var shadowSize = log ? new Vector2(size * stretch, size * 0.3f) : new Vector2(64 * shadowScale, 64 * shadowScale * 0.8f);
                var shadowCentre = position + (log ? new Vector2(3, 5) : new Vector2(size * 0.12f, size * 0.16f));
                low.Items.Add(new Item(art.Shadow, new Rect2(shadowCentre - shadowSize / 2, shadowSize), null,
                    new Color(1, 1, 1, tree ? 0.75f : 0.6f)) { Angle = log ? angle : 0 });
            }

            var sprite = new Item(texture, new Rect2(position - new Vector2(size, size) / 2, size, size),
                new Rect2(decoration.Variant * baseSize, 0, baseSize, baseSize),
                Lit(Shade(decoration.Shade, tree ? CanopyAlpha : 1f), relief.LightAt(decoration.PositionCm), relief.RiseAt(decoration.PositionCm)))
            {
                Angle = angle,
                Stretch = stretch,
                Flip = decoration.Flip,
            };
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
