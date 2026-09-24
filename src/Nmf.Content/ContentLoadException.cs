namespace Nmf.Content;

/// <summary>A content file (other than a map) could not be loaded. The message is meant for modders.</summary>
public sealed class ContentLoadException(string path, string message, Exception? inner = null)
    : Exception($"{path}: {message}", inner)
{
    public string Path { get; } = path;
}
