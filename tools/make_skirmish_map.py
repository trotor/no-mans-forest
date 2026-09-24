"""Generates content/core/maps/skirmish.tmx, the 128 x 96 phase 2 test map. Deterministic (fixed seed).

Run from the repository root: python3 tools/make_skirmish_map.py
"""
import math
import pathlib
import random

W, H, TILE = 128, 96, 16
GRASS, FOREST, SWAMP, ROAD = 1, 2, 3, 4  # terrain.tsx, firstgid 1
HEIGHT_FIRST_GID = 5                      # heights.tsx: gids 5..8 = 0..3 m
ROCK, BUSH = 9, 10                        # obstacles.tsx, firstgid 9

BLUE = [(58, 88), (62, 89), (66, 88), (70, 89)]
RED = [(52, 14), (60, 12), (68, 14), (76, 16), (45, 22)]
PATROL = [(45, 22), (85, 22), (85, 34)]
OUT = pathlib.Path("content/core/maps/skirmish.tmx")


def generate():
    rng = random.Random(1942)
    terrain = [[GRASS] * W for _ in range(H)]

    def blob(kind, cx, cy, r):
        for y in range(max(0, cy - r), min(H, cy + r + 1)):
            for x in range(max(0, cx - r), min(W, cx + r + 1)):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r * (0.7 + 0.3 * rng.random()):
                    terrain[y][x] = kind

    for _ in range(16):
        blob(FOREST, rng.randrange(W), rng.randrange(H), rng.randint(5, 13))
    for _ in range(4):
        blob(SWAMP, rng.randrange(W), rng.randrange(20, H - 20), rng.randint(4, 8))
    for y in range(H):
        cx = int(64 + 10 * math.sin(y / 14.0))
        for x in (cx, cx + 1):
            terrain[y][x] = ROAD

    def hill(x, y, cx, cy, r, peak):
        return peak * math.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (2 * r * r))

    height = [
        [min(3, int(round(hill(x, y, 36, 44, 12, 3.4) + hill(x, y, 96, 58, 9, 2.6)))) for x in range(W)]
        for y in range(H)
    ]

    keep_clear = BLUE + RED + PATROL

    def near_clear(x, y):
        return any((x - cx) ** 2 + (y - cy) ** 2 <= 9 for cx, cy in keep_clear)

    obstacles = [[0] * W for _ in range(H)]

    def scatter(kind, count):
        placed = 0
        while placed < count:
            x, y = rng.randrange(W), rng.randrange(H)
            if terrain[y][x] == ROAD or obstacles[y][x] or near_clear(x, y):
                continue
            obstacles[y][x] = kind
            placed += 1

    scatter(ROCK, 60)
    scatter(BUSH, 220)
    return terrain, height, obstacles


def csv(rows):
    return ",\n".join(",".join(str(v) for v in row) for row in rows)


def objects():
    lines = []
    oid = 1
    for side, points in (("blue", BLUE), ("red", RED)):
        for i, (x, y) in enumerate(points, start=1):
            lines.append(
                f'  <object id="{oid}" name="{side}_{i}" type="{side}" x="{x * TILE + 8}" y="{y * TILE + 8}">\n'
                f"   <point/>\n  </object>"
            )
            oid += 1
    x0, y0 = PATROL[0]
    rel = " ".join(f"{(x - x0) * TILE},{(y - y0) * TILE}" for x, y in PATROL)
    lines.append(
        f'  <object id="{oid}" name="patrol_north" type="patrol" x="{x0 * TILE + 8}" y="{y0 * TILE + 8}">\n'
        f'   <polyline points="{rel}"/>\n  </object>'
    )
    return "\n".join(lines), oid + 1


def main():
    terrain, height, obstacles = generate()
    height_gids = [[HEIGHT_FIRST_GID + h for h in row] for row in height]
    object_xml, next_id = objects()
    tmx = f"""<?xml version="1.0" encoding="UTF-8"?>
<map version="1.10" tiledversion="1.11.2" orientation="orthogonal" renderorder="right-down" width="{W}" height="{H}" tilewidth="{TILE}" tileheight="{TILE}" infinite="0" nextlayerid="5" nextobjectid="{next_id}">
 <tileset firstgid="1" source="../tilesets/terrain.tsx"/>
 <tileset firstgid="5" source="../tilesets/heights.tsx"/>
 <tileset firstgid="9" source="../tilesets/obstacles.tsx"/>
 <layer id="1" name="terrain" width="{W}" height="{H}">
  <data encoding="csv">
{csv(terrain)}
</data>
 </layer>
 <layer id="2" name="height" width="{W}" height="{H}">
  <data encoding="csv">
{csv(height_gids)}
</data>
 </layer>
 <layer id="3" name="obstacles" width="{W}" height="{H}">
  <data encoding="csv">
{csv(obstacles)}
</data>
 </layer>
 <objectgroup id="4" name="ai">
{object_xml}
 </objectgroup>
</map>
"""
    OUT.write_text(tmx)
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
