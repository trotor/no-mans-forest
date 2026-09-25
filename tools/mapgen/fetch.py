"""Fetches OpenStreetMap features (Overpass API) and ASTER elevation (OpenTopoData) for a map area into
tools/mapgen/data/<area>/. The generator works offline from these files.

Run from the repository root: python3 -m tools.mapgen.fetch karhumaki
Data: © OpenStreetMap contributors (ODbL); ASTER GDEM v3 (NASA/METI) via opentopodata.org.
"""
import json
import math
import pathlib
import sys
import time
import urllib.parse
import urllib.request

from tools.mapgen.areas import AREAS

UA = "NoMansForest/0.1 (open-source hobby game; map import)"
OVERPASS = ["https://overpass-api.de/api/interpreter", "https://maps.mail.ru/osm/tools/overpass/api/interpreter"]
DEM_POINTS = 33
DATA = pathlib.Path(__file__).parent / "data"


def bbox(area):
    half_lat = area["size_m"] / 2 / 110_574
    half_lon = area["size_m"] / 2 / (111_320 * math.cos(math.radians(area["lat"])))
    return area["lat"] - half_lat, area["lon"] - half_lon, area["lat"] + half_lat, area["lon"] + half_lon


def overpass(query):
    last = None
    for attempt in range(6):
        url = OVERPASS[attempt % len(OVERPASS)]
        try:
            req = urllib.request.Request(url, data=urllib.parse.urlencode({"data": query}).encode(), headers={"User-Agent": UA})
            return json.load(urllib.request.urlopen(req, timeout=180))
        except Exception as ex:  # busy servers answer 429/504; wait and try the other one
            last = ex
            time.sleep(15)
    raise RuntimeError(f"Overpass failed: {last}")


def fetch_osm(area):
    s, w, n, e = bbox(area)
    b = f"{s:.6f},{w:.6f},{n:.6f},{e:.6f}"
    lake = area.get("lake_relation", 0)
    features = overpass(f"""[out:json][timeout:120];
(way["landuse"]({b}); way["natural"]({b}); way["waterway"]({b}); way["highway"]({b});
 relation["natural"]({b}); relation["landuse"]({b}););
out geom;""".replace('relation["natural"]', f'relation["natural"](if: id() != {lake})'))
    time.sleep(5)
    shore = overpass(f"""[out:json][timeout:120];
relation({lake}); way(r)({b});
out geom;""") if lake else {"elements": []}
    return features, shore


def fetch_dem(area):
    s, w, n, e = bbox(area)
    points = [(n - (n - s) * i / (DEM_POINTS - 1), w + (e - w) * j / (DEM_POINTS - 1))
              for i in range(DEM_POINTS) for j in range(DEM_POINTS)]
    heights = []
    for k in range(0, len(points), 100):
        locs = "|".join(f"{a:.6f},{b:.6f}" for a, b in points[k:k + 100])
        for attempt in range(6):
            try:
                r = json.load(urllib.request.urlopen(
                    urllib.request.Request(f"https://api.opentopodata.org/v1/aster30m?locations={locs}", headers={"User-Agent": UA}), timeout=60))
                break
            except Exception:
                time.sleep(3)
        else:
            raise RuntimeError("OpenTopoData failed")
        heights += [res["elevation"] for res in r["results"]]
        time.sleep(1.2)
    return {"dataset": "aster30m", "points": DEM_POINTS,
            "rows_north_to_south": [heights[i * DEM_POINTS:(i + 1) * DEM_POINTS] for i in range(DEM_POINTS)]}


def main(name):
    area = AREAS[name]
    out = DATA / name
    out.mkdir(parents=True, exist_ok=True)
    features, shore = fetch_osm(area)
    (out / "osm.json").write_text(json.dumps(features, ensure_ascii=False))
    (out / "shore.json").write_text(json.dumps(shore, ensure_ascii=False))
    (out / "dem.json").write_text(json.dumps(fetch_dem(area)))
    print(f"wrote {out}: {len(features['elements'])} features, {len(shore['elements'])} shore ways")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "karhumaki")
