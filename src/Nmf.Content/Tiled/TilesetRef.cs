namespace Nmf.Content.Tiled;

/// <summary>A tileset as used by one map: its first global tile id and per-tile custom properties.</summary>
internal sealed record TilesetRef(
    int FirstGid,
    int TileCount,
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> Tiles);
