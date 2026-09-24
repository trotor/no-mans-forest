using System.Text.Json;

namespace Nmf.Client.Art;

public sealed record ObjectInfo(int Size, int Count);

/// <summary>Object strips (content/core/art/objects/objects.json).</summary>
public sealed class ObjectCatalog
{
    private ObjectCatalog(IReadOnlyDictionary<string, ObjectInfo> objects) => Objects = objects;

    public IReadOnlyDictionary<string, ObjectInfo> Objects { get; }

    public static ObjectCatalog Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("objects", out var objects))
                throw new FormatException("missing 'objects'");
            var result = new Dictionary<string, ObjectInfo>(StringComparer.Ordinal);
            foreach (var property in objects.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("size", out var size) || !property.Value.TryGetProperty("count", out var count))
                    throw new FormatException($"object '{property.Name}' needs 'size' and 'count'");
                var info = new ObjectInfo(size.GetInt32(), count.GetInt32());
                if (info.Size < 1 || info.Count < 1)
                    throw new FormatException($"object '{property.Name}' must have positive size and count");
                result[property.Name] = info;
            }
            return new ObjectCatalog(result);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new FormatException($"invalid objects JSON: {ex.Message}", ex);
        }
    }
}
