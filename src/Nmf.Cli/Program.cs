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

Console.Error.WriteLine("usage: nmf map-info <map.tmx>");
return 2;
