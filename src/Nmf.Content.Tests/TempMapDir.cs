namespace Nmf.Content.Tests;

/// <summary>Temporary directory containing the fixture tilesets, for writing ad-hoc test maps.</summary>
internal sealed class TempMapDir : IDisposable
{
    public TempMapDir()
    {
        Dir = Directory.CreateTempSubdirectory("nmf-test-").FullName;
        foreach (var file in new[] { "terrain.tsx", "heights.tsx" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", file), Path.Combine(Dir, file));
    }

    public string Dir { get; }

    public string WriteMap(string xml)
    {
        var path = Path.Combine(Dir, "map.tmx");
        File.WriteAllText(path, xml);
        return path;
    }

    public void Dispose() => Directory.Delete(Dir, recursive: true);
}
