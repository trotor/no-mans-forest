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
    /// <summary>Every decoration's strip is the kind's name in lower case (spruce.png, fern.png, …).</summary>
    private static readonly Dictionary<DecorationKind, string> ObjectFiles =
        Enum.GetValues<DecorationKind>().ToDictionary(k => k, k => k.ToString().ToLowerInvariant());

    private readonly Dictionary<Side, Texture2D> _soldiers = [];
    private readonly Dictionary<Side, Texture2D> _portraits = [];
    private readonly Dictionary<Side, Texture2D> _silhouettes = [];
    private readonly Dictionary<DecorationKind, Texture2D> _objects = [];

    private ArtLibrary()
    {
    }

    public SpriteSheet SoldierSheet { get; private set; } = null!;
    public ObjectCatalog Catalog { get; private set; } = null!;
    public Texture2D Shadow { get; private set; } = null!;
    public Texture2D Blood { get; private set; } = null!;
    public IReadOnlyList<Texture2D> TerrainTextures { get; private set; } = [];
    public IReadOnlyDictionary<DecorationKind, int> VariantCounts { get; private set; } = new Dictionary<DecorationKind, int>();

    public Texture2D Soldiers(Side side) => _soldiers[side];
    public Texture2D Portraits(Side side) => _portraits[side];

    /// <summary>The soldier sheet as a white silhouette, tinted in the team colour to draw a glow outline.</summary>
    public Texture2D Silhouettes(Side side) => _silhouettes[side];
    public Texture2D Object(DecorationKind kind) => _objects[kind];
    public int ObjectSize(DecorationKind kind) => Catalog.Objects[ObjectFiles[kind]].Size;

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
            var (sheetWidth, sheetHeight) = library.SoldierSheet.RequiredSize();
            library._soldiers[side] = Texture(Path.Combine(art, "soldiers", faction + ".png"), sheetWidth, sheetHeight);
            library._silhouettes[side] = Silhouette(library._soldiers[side]);
            library._portraits[side] = Texture(Path.Combine(art, "portraits", faction + ".png"), PortraitSize * PortraitCount, PortraitSize);
        }
        var counts = new Dictionary<DecorationKind, int>();
        foreach (var (kind, file) in ObjectFiles)
        {
            if (!library.Catalog.Objects.TryGetValue(file, out var info))
                throw new FormatException($"objects.json has no entry for '{file}'");
            library._objects[kind] = Texture(Path.Combine(art, "objects", file + ".png"), info.Size * info.Count, info.Size);
            counts[kind] = info.Count;
        }
        library.VariantCounts = counts;
        if (!library.Catalog.Objects.TryGetValue("blood", out var bloodInfo))
            throw new FormatException("objects.json has no entry for 'blood'");
        library.Blood = Texture(Path.Combine(art, "objects", "blood.png"), bloodInfo.Size * bloodInfo.Count, bloodInfo.Size);
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

    public const int PortraitSize = 64;
    public const int PortraitCount = 8;

    private static Texture2D Silhouette(Texture2D sheet)
    {
        var image = sheet.GetImage();
        image.Convert(Image.Format.Rgba8);
        var data = image.GetData();
        for (int i = 0; i < data.Length; i += 4)
        {
            if (data[i + 3] == 0)
                continue;
            data[i] = 255;
            data[i + 1] = 255;
            data[i + 2] = 255;
        }
        return ImageTexture.CreateFromImage(Image.CreateFromData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, data));
    }

    private static Texture2D Texture(string path, int minWidth = 1, int minHeight = 1)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"art file not found: {path}", path);
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
            throw new IOException($"could not read image: {path}");
        ArtSize.Require(path, image.GetWidth(), image.GetHeight(), minWidth, minHeight);
        return ImageTexture.CreateFromImage(image);
    }
}
