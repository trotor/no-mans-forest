namespace Nmf.Content.Tests;

/// <summary>Builds small TMX documents referencing the fixture tilesets (terrain gids 1-4, heights gids 5-8).</summary>
internal static class TmxText
{
    public static string Map(int width, int height, string terrainCsv, string extra = "") =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.10" orientation="orthogonal" renderorder="right-down" width="{width}" height="{height}" tilewidth="16" tileheight="16" infinite="0">
         <tileset firstgid="1" source="terrain.tsx"/>
         <tileset firstgid="5" source="heights.tsx"/>
         <layer id="1" name="terrain" width="{width}" height="{height}">
          <data encoding="csv">{terrainCsv}</data>
         </layer>
         {extra}
        </map>
        """;

    public static string Layer(string name, int width, int height, string csv) =>
        $"""<layer id="9" name="{name}" width="{width}" height="{height}"><data encoding="csv">{csv}</data></layer>""";
}
