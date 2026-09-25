using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Content.Tiled;

/// <summary>
/// Loads a Tiled map (orthogonal, square tiles, CSV layers) into a <see cref="GridMap"/>.
/// Tile layers: "terrain" (required), "height", "obstacles". One tile = one 1 m cell.
/// </summary>
public static class TmxMapLoader
{
    public static GridMap Load(string tmxPath)
    {
        try
        {
            return new Loader(Path.GetFullPath(tmxPath)).Load();
        }
        catch (MapLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is XmlException or FormatException or OverflowException
                                       or IOException or UnauthorizedAccessException)
        {
            throw new MapLoadException(tmxPath, ex.Message, ex);
        }
    }

    private sealed class Loader(string path)
    {
        private const uint GidMask = 0x0FFFFFFF; // clears Tiled flip/rotation flag bits 28-31
        private static readonly string[] KnownTileLayers = ["terrain", "height", "obstacles"];
        private static readonly IReadOnlyDictionary<string, string> NoProperties = new Dictionary<string, string>();

        private readonly List<TilesetRef> _tilesets = [];
        private int _width;
        private int _height;
        private int _tileSize;

        private int WidthCm => _width * SimConstants.CentimetersPerCell;
        private int HeightCm => _height * SimConstants.CentimetersPerCell;

        public GridMap Load()
        {
            if (!File.Exists(path))
                throw Error("file not found");

            var map = XDocument.Load(path).Root;
            if (map is null || map.Name.LocalName != "map")
                throw Error("root element is not <map>");
            if ((string?)map.Attribute("orientation") != "orthogonal")
                throw Error("only orthogonal maps are supported");
            if (((int?)map.Attribute("infinite") ?? 0) != 0)
                throw Error("infinite maps are not supported; untick 'Infinite' in the Tiled map properties");
            if (map.Elements("group").Any())
                throw Error("layer groups are not supported; move the layers out of the group");

            _width = RequiredInt(map, "width");
            _height = RequiredInt(map, "height");
            _tileSize = RequiredInt(map, "tilewidth");
            if (_tileSize <= 0 || _tileSize != RequiredInt(map, "tileheight"))
                throw Error("tiles must be square (tilewidth == tileheight)");
            if (_width is <= 0 or > GridMap.MaxSideCells || _height is <= 0 or > GridMap.MaxSideCells)
                throw Error($"map size must be 1..{GridMap.MaxSideCells} cells per side, was {_width}x{_height}");

            foreach (var tileset in map.Elements("tileset"))
                _tilesets.Add(LoadTileset(tileset));
            _tilesets.Sort((a, b) => a.FirstGid.CompareTo(b.FirstGid));

            var layers = ReadTileLayers(map);
            if (!layers.TryGetValue("terrain", out var terrain))
                throw Error("missing required tile layer 'terrain'");

            var terrainNames = new List<string> { "none" };
            var cells = new CellData[_width * _height];
            BuildTerrain(terrain, cells, terrainNames);
            if (layers.TryGetValue("height", out var height))
                ApplyHeight(height, cells);
            if (layers.TryGetValue("obstacles", out var obstacles))
                ApplyObstacles(obstacles, cells);

            var gridMap = new GridMap(_width, _height, terrainNames, ReadFeatures(map));
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                    gridMap[new CellCoord(x, y)] = cells[y * _width + x];
            return gridMap;
        }

        private MapFeatures ReadFeatures(XElement map)
        {
            var zones = new List<MapZone>();
            var points = new List<MapPoint>();
            var paths = new List<MapPath>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var group in map.Elements("objectgroup"))
                RequireNoOffset(group, (string?)group.Attribute("name") ?? "");

            foreach (var obj in map.Elements("objectgroup").Elements("object"))
            {
                string id = (string?)obj.Attribute("id") ?? "?";
                string name = (string?)obj.Attribute("name") ?? "";
                // Tiled 1.9 wrote "class"; 1.8 and 1.10+ write "type".
                string type = (string?)obj.Attribute("type") ?? (string?)obj.Attribute("class") ?? "";

                if (obj.Attribute("gid") is not null)
                    throw Error($"object '{name}' (id {id}) is a tile object; tile objects are not supported yet");
                if (name.Length == 0)
                    throw Error($"object id {id} has no name; every map object needs a unique name");
                if (!names.Add(name))
                    throw Error($"duplicate object name '{name}'");
                if (((double?)obj.Attribute("rotation") ?? 0) != 0)
                    throw Error($"object '{name}' is rotated; reset its Rotation to 0 in Tiled");

                var origin = new Vec2(ToCm((double?)obj.Attribute("x") ?? 0, name), ToCm((double?)obj.Attribute("y") ?? 0, name));

                if (obj.Element("point") is not null)
                {
                    points.Add(new MapPoint(name, type, RequireInside(origin, name)));
                }
                else if (obj.Element("polyline") is { } polyline)
                {
                    paths.Add(new MapPath(name, type, ParsePoints(polyline, origin, name)));
                }
                else if (obj.Elements().Any(e => e.Name.LocalName is "polygon" or "ellipse" or "text"))
                {
                    throw Error($"object '{name}': only rectangles, points and polylines are supported");
                }
                else
                {
                    var size = new Vec2(ToCm((double?)obj.Attribute("width") ?? 0, name), ToCm((double?)obj.Attribute("height") ?? 0, name));
                    if (size.X <= 0 || size.Y <= 0)
                        throw Error($"zone '{name}' has zero size");
                    var max = origin + size;
                    if (origin.X < 0 || origin.Y < 0 || max.X > WidthCm || max.Y > HeightCm)
                        throw Error($"zone '{name}' lies outside the map");
                    zones.Add(new MapZone(name, type, origin, max));
                }
            }

            return new MapFeatures(zones, points, paths);
        }

        private List<Vec2> ParsePoints(XElement polyline, Vec2 origin, string name)
        {
            string raw = (string?)polyline.Attribute("points") ?? "";
            var result = new List<Vec2>();
            foreach (var pair in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var xy = pair.Split(',');
                if (xy.Length != 2)
                    throw Error($"path '{name}' has a malformed point '{pair}'");
                var offset = new Vec2(
                    ToCm(double.Parse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture), name),
                    ToCm(double.Parse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture), name));
                result.Add(RequireInside(origin + offset, name));
            }
            if (result.Count < 2)
                throw Error($"path '{name}' needs at least two points");
            return result;
        }

        private int ToCm(double pixels, string objectName)
        {
            if (!double.IsFinite(pixels))
                throw Error($"object '{objectName}' has an invalid coordinate '{pixels.ToString(CultureInfo.InvariantCulture)}'");
            // Anything this far out is off the map anyway; rejecting it here keeps the int conversion and later sums from overflowing.
            if (Math.Abs(pixels) > 2.0 * GridMap.MaxSideCells * _tileSize)
                throw Error($"object '{objectName}' lies outside the map");
            return (int)Math.Round(pixels * SimConstants.CentimetersPerCell / _tileSize, MidpointRounding.AwayFromZero);
        }

        private void RequireNoOffset(XElement layer, string name)
        {
            if (((double?)layer.Attribute("offsetx") ?? 0) != 0 || ((double?)layer.Attribute("offsety") ?? 0) != 0)
                throw Error($"layer '{name}' has an offset; reset its Offset to 0,0 in the Tiled layer properties");
        }

        private Vec2 RequireInside(Vec2 p, string name) =>
            p.X >= 0 && p.Y >= 0 && p.X < WidthCm && p.Y < HeightCm
                ? p
                : throw Error($"object '{name}' lies outside the map at {p}");

        private TilesetRef LoadTileset(XElement tileset)
        {
            int firstGid = RequiredInt(tileset, "firstgid");
            string? source = (string?)tileset.Attribute("source");
            if (source is null)
                return TsxParser.Parse(tileset, firstGid, path);

            string tsxPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, source));
            if (!File.Exists(tsxPath))
                throw Error($"tileset file not found: {source}");
            var root = XDocument.Load(tsxPath).Root;
            if (root is null || root.Name.LocalName != "tileset")
                throw new MapLoadException(tsxPath, "root element is not <tileset>");
            return TsxParser.Parse(root, firstGid, tsxPath);
        }

        private Dictionary<string, uint[]> ReadTileLayers(XElement map)
        {
            var result = new Dictionary<string, uint[]>(StringComparer.Ordinal);
            foreach (var layer in map.Elements("layer"))
            {
                string name = (string?)layer.Attribute("name") ?? "";
                RequireNoOffset(layer, name);
                if (!KnownTileLayers.Contains(name))
                    throw Error($"unknown tile layer '{name}'; expected one of: {string.Join(", ", KnownTileLayers)}");
                if (result.ContainsKey(name))
                    throw Error($"duplicate tile layer '{name}'");
                result[name] = ReadLayerData(layer, name);
            }
            return result;
        }

        private uint[] ReadLayerData(XElement layer, string name)
        {
            if ((int?)layer.Attribute("width") != _width || (int?)layer.Attribute("height") != _height)
                throw Error($"layer '{name}' size differs from the map size {_width}x{_height}");
            var data = layer.Element("data") ?? throw Error($"layer '{name}' has no <data>");
            string encoding = (string?)data.Attribute("encoding") ?? "xml";
            if (encoding == "base64")
                return ReadBase64(data, name);
            if (encoding != "csv")
                throw Error($"layer '{name}' uses '{encoding}' encoding; set Tile Layer Format to CSV or Base64 in the Tiled map properties");

            var parts = data.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != _width * _height)
                throw Error($"layer '{name}' has {parts.Length} tiles, expected {_width * _height}");

            var gids = new uint[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                gids[i] = uint.Parse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture) & GidMask;
            return gids;
        }

        /// <summary>Tiled's base64 layer data: little-endian uint32 gids, optionally zlib or gzip compressed.</summary>
        private uint[] ReadBase64(XElement data, string name)
        {
            string? compression = (string?)data.Attribute("compression");
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data.Value.Trim());
                if (compression is not null)
                {
                    using var input = new MemoryStream(bytes);
                    using Stream unpacked = compression switch
                    {
                        "zlib" => new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress),
                        "gzip" => new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress),
                        _ => throw Error($"layer '{name}' uses '{compression}' compression; use zlib, gzip or none"),
                    };
                    using var output = new MemoryStream();
                    unpacked.CopyTo(output);
                    bytes = output.ToArray();
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException)
            {
                throw Error($"layer '{name}' has invalid base64 data: {ex.Message}");
            }
            if (bytes.Length != _width * _height * 4)
                throw Error($"layer '{name}' has {bytes.Length / 4} tiles, expected {_width * _height}");
            var gids = new uint[_width * _height];
            for (int i = 0; i < gids.Length; i++)
                gids[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4)) & GidMask;
            return gids;
        }

        private void BuildTerrain(uint[] gids, CellData[] cells, List<string> terrainNames)
        {
            var ids = new Dictionary<string, ushort>(StringComparer.Ordinal) { ["none"] = 0 };
            for (int i = 0; i < gids.Length; i++)
            {
                int x = i % _width, y = i / _width;
                if (gids[i] == 0)
                    throw Error($"terrain layer cell ({x},{y}) is empty; every cell needs a terrain tile");

                var props = Resolve(gids[i], "terrain", x, y);
                if (!props.TryGetValue("terrain", out var terrainName) || terrainName.Length == 0)
                    throw Error($"tile {gids[i]} used at ({x},{y}) in layer 'terrain' has no 'terrain' property");

                if (!ids.TryGetValue(terrainName, out var id))
                {
                    if (terrainNames.Count > ushort.MaxValue)
                        throw Error("too many terrain types");
                    id = (ushort)terrainNames.Count;
                    ids[terrainName] = id;
                    terrainNames.Add(terrainName);
                }

                cells[i] = new CellData(
                    GroundHeightCm: 0,
                    ObstacleHeightCm: ShortProperty(props, "obstacle_height_cm", x, y),
                    ConcealmentPerM: FractionProperty(props, "concealment_per_m", x, y),
                    Cover: FractionProperty(props, "cover", x, y),
                    TerrainId: id,
                    ExtraMoveCost: MoveCostProperty(props, x, y));
            }
        }

        private void ApplyHeight(uint[] gids, CellData[] cells)
        {
            for (int i = 0; i < gids.Length; i++)
            {
                if (gids[i] == 0)
                    continue;
                int x = i % _width, y = i / _width;
                var props = Resolve(gids[i], "height", x, y);
                if (!props.ContainsKey("height_cm"))
                    throw Error($"tile {gids[i]} used at ({x},{y}) in layer 'height' has no 'height_cm' property");
                cells[i].GroundHeightCm = ShortProperty(props, "height_cm", x, y);
            }
        }

        private void ApplyObstacles(uint[] gids, CellData[] cells)
        {
            for (int i = 0; i < gids.Length; i++)
            {
                if (gids[i] == 0)
                    continue;
                int x = i % _width, y = i / _width;
                var props = Resolve(gids[i], "obstacles", x, y);
                cells[i].ObstacleHeightCm = Math.Max(cells[i].ObstacleHeightCm, ShortProperty(props, "obstacle_height_cm", x, y));
                cells[i].ConcealmentPerM = Math.Max(cells[i].ConcealmentPerM, FractionProperty(props, "concealment_per_m", x, y));
                cells[i].Cover = Math.Max(cells[i].Cover, FractionProperty(props, "cover", x, y));
                cells[i].ExtraMoveCost = Math.Max(cells[i].ExtraMoveCost, MoveCostProperty(props, x, y));
            }
        }

        private IReadOnlyDictionary<string, string> Resolve(uint gid, string layer, int x, int y)
        {
            TilesetRef? owner = null;
            foreach (var tileset in _tilesets)
            {
                if (tileset.FirstGid <= gid)
                    owner = tileset;
                else
                    break;
            }

            long local = owner is null ? -1 : gid - owner.FirstGid;
            if (owner is null || (owner.TileCount > 0 && local >= owner.TileCount))
                throw Error($"tile {gid} used at ({x},{y}) in layer '{layer}' does not belong to any tileset");
            return owner.Tiles.TryGetValue((int)local, out var props) ? props : NoProperties;
        }

        private short ShortProperty(IReadOnlyDictionary<string, string> props, string name, int x, int y)
        {
            if (!props.TryGetValue(name, out var text))
                return 0;
            if (!short.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                throw Error($"property '{name}' of the tile at ({x},{y}) must be a whole number between {short.MinValue} and {short.MaxValue}, was '{text}'");
            return value;
        }

        private byte MoveCostProperty(IReadOnlyDictionary<string, string> props, int x, int y)
        {
            if (props.TryGetValue("impassable", out var flag))
            {
                if (flag == "true")
                    return CellData.Impassable;
                if (flag != "false")
                    throw Error($"property 'impassable' of the tile at ({x},{y}) must be true or false, was '{flag}'");
            }
            if (!props.TryGetValue("move_cost", out var text))
                return 0;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !(value >= 1 && value <= 3.54))
                throw Error($"property 'move_cost' of the tile at ({x},{y}) must be a number between 1 and 3.54, was '{text}'");
            return (byte)Math.Round((value - 1) * 100, MidpointRounding.AwayFromZero);
        }

        private byte FractionProperty(IReadOnlyDictionary<string, string> props, string name, int x, int y)
        {
            if (!props.TryGetValue(name, out var text))
                return 0;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !(value >= 0 && value <= 1))
                throw Error($"property '{name}' of the tile at ({x},{y}) must be a number between 0 and 1, was '{text}'");
            return (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
        }

        private int RequiredInt(XElement element, string attribute) =>
            (int?)element.Attribute(attribute)
            ?? throw Error($"<{element.Name.LocalName}> is missing attribute '{attribute}'");

        private MapLoadException Error(string message) => new(path, message);
    }
}
