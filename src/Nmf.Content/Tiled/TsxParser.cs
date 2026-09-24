using System.Xml.Linq;

namespace Nmf.Content.Tiled;

internal static class TsxParser
{
    /// <summary>Parses a &lt;tileset&gt; element (external .tsx root or embedded in a .tmx).</summary>
    public static TilesetRef Parse(XElement tileset, int firstGid, string sourcePath)
    {
        int tileCount = (int?)tileset.Attribute("tilecount") ?? 0;
        var tiles = new Dictionary<int, IReadOnlyDictionary<string, string>>();

        foreach (var tile in tileset.Elements("tile"))
        {
            int id = (int?)tile.Attribute("id")
                ?? throw new MapLoadException(sourcePath, "<tile> is missing attribute 'id'");
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in tile.Element("properties")?.Elements("property") ?? [])
            {
                string name = (string?)property.Attribute("name")
                    ?? throw new MapLoadException(sourcePath, $"tile {id} has a property without a name");
                // Multi-line string properties are stored as element text instead of a value attribute.
                properties[name] = (string?)property.Attribute("value") ?? property.Value;
            }
            tiles[id] = properties;
        }

        return new TilesetRef(firstGid, tileCount, tiles);
    }
}
