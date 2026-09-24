namespace Nmf.Content.Tiled;

/// <summary>A map or tileset file could not be loaded. The message is meant for mission authors.</summary>
public sealed class MapLoadException(string path, string message, Exception? inner = null)
    : Exception($"{path}: {message}", inner)
{
    public string Path { get; } = path;
}
