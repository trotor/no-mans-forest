namespace Nmf.Client;

public static class ContentLocator
{
    /// <summary>Walks up from <paramref name="startDirectory"/> to the nearest folder containing content/core.</summary>
    public static string? FindContentRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return null; // e.g. res:// in an exported game
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var content = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(Path.Combine(content, "core")))
                return content;
        }
        return null;
    }
}
