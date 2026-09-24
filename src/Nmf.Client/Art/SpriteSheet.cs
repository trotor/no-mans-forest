using System.Text.Json;

namespace Nmf.Client.Art;

public sealed record AnimationInfo(int Row, int Frames, int StrideCm);

/// <summary>Soldier sheet layout (content/core/art/soldiers/sheet.json, spec §5).</summary>
public sealed class SpriteSheet
{
    public static readonly string[] RequiredAnimations = ["idle", "walk", "run", "crouch", "prone", "crawl"];

    private SpriteSheet(int cellSize, int pixelsPerMetre, IReadOnlyList<string> directions, IReadOnlyDictionary<string, AnimationInfo> animations)
    {
        CellSize = cellSize;
        PixelsPerMetre = pixelsPerMetre;
        Directions = directions;
        Animations = animations;
    }

    public int CellSize { get; }
    public int PixelsPerMetre { get; }
    public IReadOnlyList<string> Directions { get; }
    public IReadOnlyDictionary<string, AnimationInfo> Animations { get; }

    public static SpriteSheet Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int cell = Positive(root, "cellSize");
            int ppm = Positive(root, "pixelsPerMetre");
            var directions = Required(root, "directions").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            if (directions.Count != 8)
                throw new FormatException($"'directions' must list 8 directions, found {directions.Count}");

            var animations = new Dictionary<string, AnimationInfo>(StringComparer.Ordinal);
            foreach (var property in Required(root, "animations").EnumerateObject())
            {
                var a = property.Value;
                int frames = Required(a, "frames").GetInt32();
                if (frames < 1)
                    throw new FormatException($"animation '{property.Name}' must have at least 1 frame");
                int row = Required(a, "row").GetInt32();
                if (row < 0)
                    throw new FormatException($"animation '{property.Name}' has a negative row");
                animations[property.Name] = new AnimationInfo(row, frames, Required(a, "strideCm").GetInt32());
            }
            foreach (var name in RequiredAnimations)
                if (!animations.ContainsKey(name))
                    throw new FormatException($"animation '{name}' is missing");
            return new SpriteSheet(cell, ppm, directions, animations);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new FormatException($"invalid sprite sheet JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Smallest image (width, height) that contains every frame this layout refers to.</summary>
    public (int Width, int Height) RequiredSize() =>
        (Animations.Values.Max(a => a.Frames) * CellSize, (Animations.Values.Max(a => a.Row) + Directions.Count) * CellSize);

    /// <summary>Pixel rectangle of one frame; the frame index wraps, so a sheet with fewer frames never reads outside itself.</summary>
    public (int X, int Y, int Size) FrameRect(string animation, int direction, int frame)
    {
        if (!Animations.TryGetValue(animation, out var info))
            throw new ArgumentException($"unknown animation '{animation}'", nameof(animation));
        if (direction is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Direction must be 0..7.");
        int column = ((frame % info.Frames) + info.Frames) % info.Frames;
        return (column * CellSize, (info.Row + direction) * CellSize, CellSize);
    }

    private static JsonElement Required(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : throw new FormatException($"missing '{name}'");

    private static int Positive(JsonElement element, string name)
    {
        int value = Required(element, name).GetInt32();
        return value > 0 ? value : throw new FormatException($"'{name}' must be positive");
    }
}
