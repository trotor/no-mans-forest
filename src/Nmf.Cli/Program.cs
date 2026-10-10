using Nmf.Cli;
using Nmf.Content;
using Nmf.Content.Tiled;

if (args is ["map-info", var mapPath])
{
    try
    {
        Console.Write(MapSummary.Describe(TmxMapLoader.Load(mapPath)));
        return 0;
    }
    catch (MapLoadException ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
}

if (args is ["probe-attack", .. var rest])
{
    string Option(string name, string fallback)
    {
        int i = Array.IndexOf(rest, "--" + name);
        return i >= 0 && i + 1 < rest.Length ? rest[i + 1] : fallback;
    }
    string? content = Option("content", "") is { Length: > 0 } c ? c : FindContentRoot(Directory.GetCurrentDirectory());
    if (content is null)
    {
        Console.Error.WriteLine("error: no content/core found here or above; pass --content <dir>");
        return 1;
    }
    string tactic = Option("tactic", "legacy");
    if (tactic != "legacy")
    {
        Console.Error.WriteLine($"error: unknown tactic '{tactic}'");
        return 2;
    }
    AttackProbe.Run(content, Option("mission", "iskuosasto"), int.Parse(Option("seeds", "8")), int.Parse(Option("minutes", "8")), null, Console.Out);
    return 0;
}

Console.Error.WriteLine("usage: nmf map-info <map.tmx>\n       nmf probe-attack [--mission iskuosasto] [--tactic legacy] [--seeds 8] [--minutes 8] [--content <dir>]");
return 2;

/// <summary>The nearest folder at or above <paramref name="start"/> holding content/core.</summary>
static string? FindContentRoot(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "content", "core")))
            return Path.Combine(dir.FullName, "content");
    return null;
}
