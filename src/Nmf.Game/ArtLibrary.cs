using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Nmf.Client.Art;
using Nmf.Sim.Units;
using Side = Nmf.Sim.Units.Side;

namespace Nmf.Game;

/// <summary>Loads the generated (or hand-made) art from content/core/art at runtime.</summary>
public sealed class ArtLibrary
{
    private static readonly Dictionary<DecorationKind, string> ObjectFiles = new()
    {
        [DecorationKind.Spruce] = "spruce",
        [DecorationKind.Birch] = "birch",
        [DecorationKind.Rock] = "rock",
        [DecorationKind.Bush] = "bush",
    };

    private readonly Dictionary<Side, Texture2D> _soldiers = [];
    private readonly Dictionary<Side, Texture2D> _portraits = [];
    private readonly Dictionary<DecorationKind, Texture2D> _objects = [];

    private ArtLibrary()
    {
    }

    public SpriteSheet SoldierSheet { get; private set; } = null!;
    public ObjectCatalog Catalog { get; private set; } = null!;
    public Texture2D Shadow { get; private set; } = null!;
    public IReadOnlyList<Texture2D> TerrainTextures { get; private set; } = [];
    public IReadOnlyDictionary<DecorationKind, int> VariantCounts { get; private set; } = new Dictionary<DecorationKind, int>();

    public Texture2D Soldiers(Side side) => _soldiers[side];
    public Texture2D Portraits(Side side) => _portraits[side];
    public Texture2D Object(DecorationKind kind) => _objects[kind];

    public static ArtLibrary Load(string contentRoot)
    {
        string art = Path.Combine(contentRoot, "core", "art");
        var library = new ArtLibrary
        {
            SoldierSheet = ParseWithPath(Path.Combine(art, "soldiers", "sheet.json"), SpriteSheet.Parse),
            Catalog = ParseWithPath(Path.Combine(art, "objects", "objects.json"), ObjectCatalog.Parse),
            Shadow = Texture(Path.Combine(art, "objects", "shadow.png")),
            TerrainTextures = TerrainArt.TextureNames.Select(n => Texture(Path.Combine(art, "terrain", n + ".png"))).ToList(),
        };
        foreach (var (side, faction) in new[] { (Side.Blue, "finnish"), (Side.Red, "soviet") })
        {
            library._soldiers[side] = Texture(Path.Combine(art, "soldiers", faction + ".png"));
            library._portraits[side] = Texture(Path.Combine(art, "portraits", faction + ".png"));
        }
        var counts = new Dictionary<DecorationKind, int>();
        foreach (var (kind, file) in ObjectFiles)
        {
            if (!library.Catalog.Objects.TryGetValue(file, out var info))
                throw new FormatException($"objects.json has no entry for '{file}'");
            library._objects[kind] = Texture(Path.Combine(art, "objects", file + ".png"));
            counts[kind] = info.Count;
        }
        library.VariantCounts = counts;
        return library;
    }

    private static T ParseWithPath<T>(string path, System.Func<string, T> parse)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"art file not found: {path}", path);
        try
        {
            return parse(File.ReadAllText(path));
        }
        catch (FormatException ex)
        {
            throw new FormatException($"{path}: {ex.Message}", ex);
        }
    }

    private static Texture2D Texture(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"art file not found: {path}", path);
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
            throw new IOException($"could not read image: {path}");
        return ImageTexture.CreateFromImage(image);
    }
}
