"""Generates a skirmish map from real terrain (spec docs/superpowers/specs/2026-09-25-real-map-design.md).

Reads the cached OpenStreetMap and ASTER data in tools/mapgen/data/<area>/ (see fetch.py) and writes
content/core/maps/<area>.tmx (base64 + zlib layers), content/core/tilesets/heights_fine.tsx/.png and
tools/mapgen/data/<area>/preview.png. Deterministic: the same data and seed give the same map.

Run from the repository root: python3 -m tools.mapgen.generate karhumaki
Map data © OpenStreetMap contributors (ODbL 1.0); elevation ASTER GDEM v3 (NASA/METI).
"""
import base64
import json
import math
import pathlib
import sys
import zlib
from dataclasses import dataclass, field

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from tools.mapgen.areas import AREAS

ROOT = pathlib.Path(__file__).resolve().parents[2]
DATA = pathlib.Path(__file__).parent / "data"
MAPS = ROOT / "content" / "core" / "maps"
TILESETS = ROOT / "content" / "core" / "tilesets"
SEED = 1942

# Terrain codes (numpy) and their gids: terrain.tsx firstgid 1 (grass, forest, swamp, road), water.tsx firstgid 5.
FOREST, GRASS, SWAMP, ROAD, WATER = 0, 1, 2, 3, 4
TERRAIN_GID = {GRASS: 1, FOREST: 2, SWAMP: 3, ROAD: 4, WATER: 5}
HEIGHT_FIRST_GID = 6
HEIGHT_STEP_CM = 25
HEIGHT_TILES = 512
OBSTACLE_FIRST_GID = HEIGHT_FIRST_GID + HEIGHT_TILES  # obstacles.tsx: rock, bush, log
NO_OBSTACLE, ROCK, BUSH, LOG = 0, 1, 2, 3

ROAD_WIDTH_M = {"tertiary": 4, "secondary": 5, "unclassified": 4, "residential": 3, "service": 3, "track": 2, "path": 1.5, "footway": 1.5}
STREAM_WIDTH_M = 3
GRASS_TAGS = {("natural", "scrub"), ("natural", "grassland"), ("natural", "heath"), ("landuse", "meadow"),
              ("landuse", "farmland"), ("landuse", "grass"), ("natural", "fell")}
SCRUB_TAGS = {("natural", "scrub"), ("natural", "heath")}
FOREST_TAGS = {("natural", "wood"), ("landuse", "forest")}
WETLAND_TAGS = {("natural", "wetland")}
WATER_TAGS = {("natural", "water")}


@dataclass
class MapData:
    size: int
    terrain: np.ndarray            # uint8 codes
    height_cm: np.ndarray          # int32, multiples of HEIGHT_STEP_CM
    obstacles: np.ndarray          # uint8 NO_OBSTACLE / ROCK / BUSH / LOG
    blue: list = field(default_factory=list)    # [(x, y)] cells
    red: list = field(default_factory=list)
    patrol: list = field(default_factory=list)
    zones: list = field(default_factory=list)   # [(name, type, x0, y0, x1, y1)] cells, end exclusive
    foxholes: list = field(default_factory=list)  # [(x, y)] cells dug 1 m deep
    logs: list = field(default_factory=list)      # [(x0, y0, x1, y1)] fallen trees, cells at both ends
    properties: dict = field(default_factory=dict)


# ---------------------------------------------------------------- geometry

class Projection:
    """Local equirectangular projection: 1 unit = 1 m, x east, y south, the area centre at (size/2, size/2)."""

    def __init__(self, lat, lon, size):
        self.lat, self.lon, self.size = lat, lon, size
        self.kx = 111_320 * math.cos(math.radians(lat))
        self.ky = 110_574

    def __call__(self, lat, lon):
        return (lon - self.lon) * self.kx + self.size / 2, (self.lat - lat) * self.ky + self.size / 2


def assemble_rings(ways):
    """Joins way pieces (lists of points) that share end points into rings; returns a list of point lists."""
    pieces = [list(w) for w in ways if len(w) >= 2]
    rings = []
    while pieces:
        ring = pieces.pop(0)
        grown = True
        while ring[0] != ring[-1] and grown:
            grown = False
            for i, piece in enumerate(pieces):
                if piece[0] == ring[-1]:
                    ring += piece[1:]
                elif piece[-1] == ring[-1]:
                    ring += piece[::-1][1:]
                elif piece[-1] == ring[0]:
                    ring = piece + ring[1:]
                elif piece[0] == ring[0]:
                    ring = piece[::-1] + ring[1:]
                else:
                    continue
                pieces.pop(i)
                grown = True
                break
        rings.append(ring)
    return rings


def tag_pairs(tags):
    return {(k, v) for k, v in tags.items()}


def polygons(osm, project):
    """(tags, outer rings, inner rings) in map coordinates for every closed way and multipolygon relation."""
    result = []
    for el in osm["elements"]:
        tags = el.get("tags", {})
        if el["type"] == "way":
            g = [(p["lat"], p["lon"]) for p in el.get("geometry", []) if p]
            if len(g) >= 4 and g[0] == g[-1]:
                result.append((tags, [[project(*p) for p in g]], []))
        elif el["type"] == "relation":
            members = [m for m in el.get("members", []) if m["type"] == "way" and m.get("geometry")]
            outer = assemble_rings([[(p["lat"], p["lon"]) for p in m["geometry"] if p] for m in members if m.get("role") != "inner"])
            inner = assemble_rings([[(p["lat"], p["lon"]) for p in m["geometry"] if p] for m in members if m.get("role") == "inner"])
            result.append((tags, [[project(*p) for p in r] for r in outer], [[project(*p) for p in r] for r in inner]))
    return result


def lines(osm, project):
    """(tags, points) for every open way that is a road or a stream."""
    result = []
    for el in osm["elements"]:
        tags = el.get("tags", {})
        if el["type"] == "way" and ("highway" in tags or "waterway" in tags):
            g = [(p["lat"], p["lon"]) for p in el.get("geometry", []) if p]
            if len(g) >= 2:
                result.append((tags, [project(*p) for p in g]))
    return result


def mask_of(size, rings_with_holes):
    img = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(img)
    for outer, inner in rings_with_holes:
        for ring in outer:
            if len(ring) >= 3:
                draw.polygon(ring, fill=255)
        for ring in inner:
            if len(ring) >= 3:
                draw.polygon(ring, fill=0)
    return np.array(img) > 0


def line_mask(size, polylines, width):
    img = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(img)
    for pts in polylines:
        draw.line(pts, fill=255, width=max(1, int(round(width))), joint="curve")
        r = width / 2
        for x, y in pts:  # round the joints so bends have no gaps
            draw.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return np.array(img) > 0


def lake_mask(size, shore_polylines, seed):
    """The lake: flood-filled from a cell known to be water, bounded by the shore lines (drawn 2 m wide) and the map edge."""
    if not shore_polylines:
        return np.zeros((size, size), bool)
    img = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(img)
    for pts in shore_polylines:
        draw.line(pts, fill=128, width=2)
    if img.getpixel(seed) != 0:
        raise ValueError(f"lake seed {seed} lies on the shore line; pick a cell clearly in the water")
    ImageDraw.floodfill(img, seed, 255, thresh=0)
    arr = np.array(img)
    water = arr == 255
    if water.mean() > 0.9:
        raise ValueError("the shore lines do not cut the lake off from the land (the fill covered the whole map)")
    # The shore line itself belongs to the lake where it borders it.
    shore = arr == 128
    grown = np.array(Image.fromarray((water * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))) > 0
    return water | (shore & grown)


# ---------------------------------------------------------------- noise and height

def smooth_noise(size, cells, rng):
    """Value noise in 0..1 with features about size/cells across (bicubic upsampling of random values)."""
    coarse = rng.random((cells + 1, cells + 1)).astype(np.float32)
    img = Image.fromarray(coarse, "F").resize((size, size), Image.BICUBIC)
    arr = np.array(img)
    return (arr - arr.min()) / max(1e-6, arr.max() - arr.min())


def heights(size, dem, water, rng):
    """Ground height in cm above the lowest point (the lake surface is 0), in HEIGHT_STEP_CM steps."""
    grid = np.array(dem["rows_north_to_south"], dtype=np.float32)
    grid = np.where(np.isnan(grid), np.nanmin(grid), grid)
    metres = np.array(Image.fromarray(grid, "F").resize((size, size), Image.BICUBIC))
    base = metres[water].min() if water.any() else metres.min()
    cm = (metres - base) * 100 + (smooth_noise(size, 60, rng) - 0.5) * 80  # hummocks of ±40 cm
    cm = np.clip(cm, 0, (HEIGHT_TILES - 1) * HEIGHT_STEP_CM)
    cm[water] = 0
    return (np.round(cm / HEIGHT_STEP_CM) * HEIGHT_STEP_CM).astype(np.int32)


# ---------------------------------------------------------------- the map

def build(area, osm, shore, dem, seed=SEED):
    size = area["size_m"]
    rng = np.random.default_rng(seed)
    project = Projection(area["lat"], area["lon"], size)
    polys = polygons(osm, project)
    ways = lines(osm, project)

    def union(tagset):
        return mask_of(size, [(o, i) for tags, o, i in polys if tag_pairs(tags) & tagset])

    forest = union(FOREST_TAGS)
    grass = union(GRASS_TAGS)
    scrub = union(SCRUB_TAGS)
    wetland = union(WETLAND_TAGS)
    ponds = union(WATER_TAGS)
    shore_lines = [[project(p["lat"], p["lon"]) for p in el.get("geometry", []) if p] for el in shore["elements"]]
    lake = lake_mask(size, shore_lines, area["lake_seed"]) | ponds

    # Untagged land in these parts is mostly forest; the 1942 changes below open clearings in it.
    terrain = np.full((size, size), FOREST, np.uint8)
    terrain[grass & ~forest] = GRASS
    clearing = (smooth_noise(size, 25, rng) > 0.8) & (terrain == FOREST)
    terrain[clearing] = GRASS
    terrain[wetland] = SWAMP
    streams = [pts for tags, pts in ways if tags.get("waterway") in ("stream", "ditch", "drain", "river")]
    terrain[line_mask(size, streams, STREAM_WIDTH_M)] = SWAMP

    # An old field cleared beside the road, south of the centre.
    road_lines = [(tags, pts) for tags, pts in ways if "highway" in tags]
    field_centre = old_field_centre(size, road_lines)
    if field_centre is not None:
        fx, fy = field_centre
        yy, xx = np.mgrid[0:size, 0:size]
        field_mask = (((xx - fx) / 40.0) ** 2 + ((yy - fy) / 30.0) ** 2 <= 1.0) & ~wetland
        terrain[field_mask] = GRASS

    # 1942: the asphalt road is a cart track, the forest road a path.
    for tags, pts in road_lines:
        kind = tags.get("highway")
        width = {"tertiary": 4, "secondary": 4, "unclassified": 3, "track": 2}.get(kind, ROAD_WIDTH_M.get(kind, 1.5))
        terrain[line_mask(size, [pts], width)] = ROAD
    terrain[lake] = WATER

    height_cm = heights(size, dem, lake, rng)

    blue, red, patrol = place_forces(size, terrain, height_cm)
    foxholes = dig_foxholes(size, terrain, height_cm, red[:5], blue[:4])
    obstacles = scatter_obstacles(size, terrain, height_cm, scrub, rng, blue + red + patrol + foxholes)
    logs = windfalls(size, terrain, obstacles, rng, blue + red + patrol + foxholes)

    return MapData(size, terrain, height_cm, obstacles, blue, red, patrol, zones_for(size, blue, red), foxholes=foxholes, logs=logs, properties={
        "source": "Map data © OpenStreetMap contributors (ODbL 1.0, openstreetmap.org/copyright); "
                  "elevation ASTER GDEM v3 (NASA/METI) via opentopodata.org; changed for the game (1942 look)",
        "origin_lat": f"{area['lat']:.6f}",
        "origin_lon": f"{area['lon']:.6f}",
        "title": area["title"],
    })


def zones_for(size, blue, red):
    """The start area round the Finns, and the enemy position as reported: its box sits about 10 m off the real one."""
    def box(name, cx, cy, w, h):
        x0 = int(round(min(max(cx - w / 2, 0), size - w)))
        y0 = int(round(min(max(cy - h / 2, 0), size - h)))
        return (name, "zone", x0, y0, x0 + w, y0 + h)
    bx, by = np.mean(blue, axis=0)
    rx, ry = red[0]
    return [box("start_zone", bx, by, 40, 30), box("outpost", rx + 8, ry - 6, 70, 50)]


def old_field_centre(size, road_lines):
    """A spot 55 m east of the main road, about 70 % of the way south; None without a road."""
    best = None
    target_y = size * 0.7
    for tags, pts in road_lines:
        if tags.get("highway") not in ("tertiary", "secondary", "unclassified"):
            continue
        for x, y in pts:
            if 0 <= x < size and 0 <= y < size and (best is None or abs(y - target_y) < abs(best[1] - target_y)):
                best = (x, y)
    return None if best is None else (best[0] + 55, best[1])


def passable(terrain, x, y):
    return 0 <= x < terrain.shape[1] and 0 <= y < terrain.shape[0] and terrain[y, x] != WATER


def nearest_passable(terrain, x, y, want=(FOREST, GRASS)):
    x, y = int(round(x)), int(round(y))
    for r in range(0, 60):
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if max(abs(dx), abs(dy)) != r:
                    continue
                cx, cy = x + dx, y + dy
                if passable(terrain, cx, cy) and terrain[cy, cx] in want:
                    return cx, cy
    raise ValueError(f"no suitable cell near ({x},{y})")


def place_forces(size, terrain, height_cm):
    """Finns in the south-western forest; the Soviets on the highest ground 300–400 m to the north-east, mid-map."""
    blue_centre = nearest_passable(terrain, size * 0.25, size * 0.82, (FOREST,))
    yy, xx = np.mgrid[0:size, 0:size]
    distance = np.hypot(xx - blue_centre[0], yy - blue_centre[1])
    # On a hill in the middle of the map, so the approach crosses the bog and the road and the flanks stay open.
    ring = (distance > 300) & (distance < 400) & (yy < size * 0.55) & (xx > size * 0.35) & (xx < size * 0.75) \
        & (terrain != WATER) & (terrain != SWAMP)
    candidates = np.where(ring, height_cm, -1)
    ry, rx = np.unravel_index(int(np.argmax(candidates)), candidates.shape)
    red_centre = (int(rx), int(ry))

    toward_blue = np.array(blue_centre, float) - np.array(red_centre, float)
    toward_blue /= np.linalg.norm(toward_blue)
    side = np.array([-toward_blue[1], toward_blue[0]])

    blue = [nearest_passable(terrain, *(np.array(blue_centre) + side * (i - 1.5) * 4), (FOREST, GRASS)) for i in range(4)]
    # The support squad (with the machine gun) a little to the east and back: leader first.
    blue += [nearest_passable(terrain, *np.clip(np.array(blue_centre) + side * (10 + i * 4) + toward_blue * 4, 0, size - 1), (FOREST, GRASS))
             for i in range(3)]
    # The Soviet squad in an arc on the slope, facing the Finns; the leader first, in the middle.
    arc = [0, -1, 1, -2, 2]
    red = [nearest_passable(terrain, *(np.array(red_centre) + side * k * 7 + toward_blue * abs(k) * 2), (FOREST, GRASS))
           for k in arc]
    # The reserve squad 60 m behind the knoll, out of sight of the approach: its leader first.
    red += [nearest_passable(terrain, *np.clip(np.array(red_centre) - toward_blue * 60 + side * k * 5, 0, size - 1), (FOREST, GRASS))
            for k in (0, -1, 1, 2)]
    foot = np.array(red_centre) + toward_blue * 60
    patrol = [nearest_passable(terrain, *(foot + side * s), (FOREST, GRASS, ROAD)) for s in (-50, 0, 50)]
    return blue, red, patrol


FOXHOLE_DEPTH_CM = 100
PARAPET_CM = 25


def dig_foxholes(size, terrain, height_cm, post, strike):
    """One-man foxholes for the post, and three more along its arc: 1 m deep, the spoil thrown up round them."""
    rc = np.array(post[0], float)
    toward_blue = np.mean(strike, axis=0) - rc
    toward_blue /= np.linalg.norm(toward_blue)
    side = np.array([-toward_blue[1], toward_blue[0]])
    extra = [rc + side * k * 7 + toward_blue * abs(k) * 2 for k in (-3, 3)] + [rc - toward_blue * 6]
    holes = list(post)
    for p in extra:
        cell = nearest_passable(terrain, *np.clip(p, 1, size - 2), (FOREST, GRASS))
        if all(max(abs(cell[0] - hx), abs(cell[1] - hy)) > 1 for hx, hy in holes):
            holes.append(cell)
    rims = {(x + dx, y + dy) for x, y in holes for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - set(holes)
    for x, y in rims:
        if 0 <= x < size and 0 <= y < size:
            height_cm[y, x] += PARAPET_CM
    for x, y in holes:
        height_cm[y, x] = max(0, height_cm[y, x] - FOXHOLE_DEPTH_CM)
    return holes


def line_cells(x0, y0, x1, y1):
    """The cells a straight line between two cells crosses (Bresenham)."""
    cells, dx, dy = [], abs(x1 - x0), abs(y1 - y0)
    sx, sy = (1 if x1 > x0 else -1), (1 if y1 > y0 else -1)
    err, x, y = dx - dy, x0, y0
    while True:
        cells.append((x, y))
        if (x, y) == (x1, y1):
            return cells
        e2 = 2 * err
        if e2 > -dy:
            err -= dy
            x += sx
        if e2 < dx:
            err += dx
            y += sy


def windfalls(size, terrain, obstacles, rng, keep_clear, patches=90):
    """Fallen trees in the forest: patches of windthrow, the trunks of each lying roughly the way the storm blew."""
    clear = np.zeros((size, size), bool)
    for cx, cy in keep_clear:
        clear[max(0, cy - 4):cy + 5, max(0, cx - 4):cx + 5] = True
    logs = []
    for _ in range(patches):
        cx, cy = int(rng.integers(8, size - 8)), int(rng.integers(8, size - 8))
        if terrain[cy, cx] != FOREST:
            continue
        storm = rng.uniform(0, math.pi)
        for _ in range(int(rng.integers(3, 8))):
            x0 = int(round(cx + rng.normal(0, 6)))
            y0 = int(round(cy + rng.normal(0, 6)))
            angle = storm + rng.normal(0, 0.35)
            length = rng.uniform(3, 6)
            x1 = int(round(x0 + length * math.cos(angle)))
            y1 = int(round(y0 + length * math.sin(angle)))
            if not (0 <= min(x0, x1) and max(x0, x1) < size and 0 <= min(y0, y1) and max(y0, y1) < size):
                continue
            if math.hypot(x1 - x0, y1 - y0) < 3:
                continue
            cells = line_cells(x0, y0, x1, y1)
            if any(terrain[y, x] != FOREST or obstacles[y, x] != NO_OBSTACLE or clear[y, x] for x, y in cells):
                continue
            for x, y in cells:
                obstacles[y, x] = LOG
            logs.append((x0, y0, x1, y1))
    return logs


def scatter_obstacles(size, terrain, height_cm, scrub, rng, keep_clear):
    """Boulders in the forest and on slopes, bushes along forest edges, in scrub and round the bogs."""
    obstacles = np.zeros((size, size), np.uint8)
    gy, gx = np.gradient(height_cm.astype(np.float32) / 100.0)
    slope = np.hypot(gx, gy)  # metres per metre
    roll = rng.random((size, size))
    land = (terrain == FOREST) | (terrain == GRASS)
    rock_p = (1 + np.clip(slope, 0, 1) * 6) / 600
    obstacles[land & (roll < rock_p)] = ROCK

    forest = terrain == FOREST
    open_ground = (terrain == GRASS) | (terrain == SWAMP)
    near_open = np.array(Image.fromarray((open_ground * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))) > 0
    near_forest = np.array(Image.fromarray((forest * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))) > 0
    # Only on the open side of the forest edge: the game draws no bushes inside the forest, and unseen ones would block sight.
    edge = (terrain == GRASS) & near_forest
    roll2 = rng.random((size, size))
    bush_p = np.where(edge, 0.18, 0.0) + np.where(scrub & (terrain == GRASS), 0.08, 0.0) \
        + np.where((terrain == GRASS) & ~edge, 0.004, 0.0)
    obstacles[(obstacles == NO_OBSTACLE) & land & (roll2 < bush_p)] = BUSH

    for cx, cy in keep_clear:
        obstacles[max(0, cy - 3):cy + 4, max(0, cx - 3):cx + 4] = NO_OBSTACLE
    obstacles[terrain == ROAD] = NO_OBSTACLE
    return obstacles


# ---------------------------------------------------------------- output

def layer_base64(gids):
    return base64.b64encode(zlib.compress(np.ascontiguousarray(gids, dtype="<u4").tobytes(), 9)).decode("ascii")


def tmx(data):
    size = data.size
    terrain_gids = np.vectorize(TERRAIN_GID.get)(data.terrain).astype(np.uint32)
    height_gids = (HEIGHT_FIRST_GID + data.height_cm // HEIGHT_STEP_CM).astype(np.uint32)
    obstacle_gids = np.where(data.obstacles == NO_OBSTACLE, 0, OBSTACLE_FIRST_GID - 1 + data.obstacles.astype(np.uint32)).astype(np.uint32)
    objects, oid = [], 1
    for side, cells in (("blue", data.blue), ("red", data.red)):
        for i, (x, y) in enumerate(cells, start=1):
            objects.append(f'  <object id="{oid}" name="{side}_{i}" type="{side}" x="{x * 16 + 8}" y="{y * 16 + 8}">\n   <point/>\n  </object>')
            oid += 1
    for i, (x0, y0, x1, y1) in enumerate(data.logs, start=1):
        objects.append(f'  <object id="{oid}" name="log_{i}" type="log" x="{x0 * 16 + 8}" y="{y0 * 16 + 8}">\n   <polyline points="0,0 {(x1 - x0) * 16},{(y1 - y0) * 16}"/>\n  </object>')
        oid += 1
    for i, (x, y) in enumerate(data.foxholes, start=1):
        objects.append(f'  <object id="{oid}" name="foxhole_{i}" type="foxhole" x="{x * 16 + 8}" y="{y * 16 + 8}">\n   <point/>\n  </object>')
        oid += 1
    for name, kind, zx0, zy0, zx1, zy1 in data.zones:
        objects.append(f'  <object id="{oid}" name="{name}" type="{kind}" x="{zx0 * 16}" y="{zy0 * 16}" width="{(zx1 - zx0) * 16}" height="{(zy1 - zy0) * 16}"/>')
        oid += 1
    x0, y0 = data.patrol[0]
    rel = " ".join(f"{(x - x0) * 16},{(y - y0) * 16}" for x, y in data.patrol)
    objects.append(f'  <object id="{oid}" name="patrol_hill" type="patrol" x="{x0 * 16 + 8}" y="{y0 * 16 + 8}">\n   <polyline points="{rel}"/>\n  </object>')
    props = "\n".join(f'  <property name="{k}" value="{escape(v)}"/>' for k, v in data.properties.items())

    def layer(lid, name, gids):
        return (f' <layer id="{lid}" name="{name}" width="{size}" height="{size}">\n'
                f'  <data encoding="base64" compression="zlib">\n   {layer_base64(gids)}\n  </data>\n </layer>')

    return f"""<?xml version="1.0" encoding="UTF-8"?>
<map version="1.10" tiledversion="1.11.2" orientation="orthogonal" renderorder="right-down" width="{size}" height="{size}" tilewidth="16" tileheight="16" infinite="0" nextlayerid="5" nextobjectid="{oid + 1}">
 <properties>
{props}
 </properties>
 <tileset firstgid="1" source="../tilesets/terrain.tsx"/>
 <tileset firstgid="5" source="../tilesets/water.tsx"/>
 <tileset firstgid="{HEIGHT_FIRST_GID}" source="../tilesets/heights_fine.tsx"/>
 <tileset firstgid="{OBSTACLE_FIRST_GID}" source="../tilesets/obstacles.tsx"/>
{layer(1, "terrain", terrain_gids)}
{layer(2, "height", height_gids)}
{layer(3, "obstacles", obstacle_gids)}
 <objectgroup id="4" name="ai">
{chr(10).join(objects)}
 </objectgroup>
</map>
"""


def escape(text):
    return text.replace("&", "&amp;").replace('"', "&quot;").replace("<", "&lt;").replace(">", "&gt;")


def heights_tileset():
    """512 tiles, 25 cm apart (0 .. 127.75 m), for maps with real relief."""
    tiles = "\n".join(f' <tile id="{i}"><properties><property name="height_cm" type="int" value="{i * HEIGHT_STEP_CM}"/></properties></tile>'
                      for i in range(HEIGHT_TILES))
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="heights_fine" tilewidth="16" tileheight="16" tilecount="{HEIGHT_TILES}" columns="32">
 <image source="heights_fine.png" width="512" height="256"/>
{tiles}
</tileset>
"""


def heights_tileset_image():
    arr = np.zeros((256, 512), np.uint8)
    for i in range(HEIGHT_TILES):
        r, c = divmod(i, 32)
        arr[r * 16:(r + 1) * 16, c * 16:(c + 1) * 16] = int(20 + 235 * i / (HEIGHT_TILES - 1))
    return Image.fromarray(arr, "L").convert("RGB")


def preview(data):
    colours = np.array([(46, 92, 44), (118, 156, 72), (104, 100, 70), (160, 130, 90), (52, 78, 110)], np.uint8)
    rgb = colours[data.terrain].astype(np.float32)
    gy, gx = np.gradient(data.height_cm.astype(np.float32) / 100.0)
    shade = np.clip(1 + (gx * 0.55 + gy * 0.7) * -1.5, 0.6, 1.3)
    rgb *= shade[..., None]
    rgb[data.obstacles == ROCK] = (150, 150, 150)
    rgb[data.obstacles == BUSH] = (70, 130, 60)
    rgb[data.obstacles == LOG] = (120, 90, 50)
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB")
    draw = ImageDraw.Draw(img)
    for cells, colour in ((data.blue, (60, 120, 255)), (data.red, (230, 40, 40)), (data.patrol, (255, 160, 40))):
        for x, y in cells:
            draw.ellipse([x - 4, y - 4, x + 4, y + 4], fill=colour)
    return img


def load(name):
    folder = DATA / name
    return (json.loads((folder / "osm.json").read_text()), json.loads((folder / "shore.json").read_text()),
            json.loads((folder / "dem.json").read_text()))


def main(name):
    area = AREAS[name]
    data = build(area, *load(name))
    (MAPS / f"{name}.tmx").write_text(tmx(data))
    (TILESETS / "heights_fine.tsx").write_text(heights_tileset())
    heights_tileset_image().save(TILESETS / "heights_fine.png")
    preview(data).save(DATA / name / "preview.png")
    shares = {n: float((data.terrain == c).mean()) for n, c in (("forest", FOREST), ("grass", GRASS), ("swamp", SWAMP), ("road", ROAD), ("water", WATER))}
    print(f"wrote {MAPS / (name + '.tmx')}: " + ", ".join(f"{k} {v:.0%}" for k, v in shares.items())
          + f"; relief {data.height_cm.max() / 100:.1f} m; rocks {(data.obstacles == ROCK).sum()}, bushes {(data.obstacles == BUSH).sum()}")
    print(f"blue {data.blue} red {data.red} patrol {data.patrol}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "karhumaki")
