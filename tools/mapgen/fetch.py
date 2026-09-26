"""Fetches OpenStreetMap features (Overpass API) and ASTER elevation (OpenTopoData) for a map area into
tools/mapgen/data/<area>/. The generator works offline from these files.

Run from the repository root: python3 -m tools.mapgen.fetch karhumaki
Data: © OpenStreetMap contributors (ODbL); ASTER GDEM v3 (NASA/METI) via opentopodata.org;
land from satellite: Copernicus Sentinel-2 L2A (the clearest summer scene, via the Earth Search STAC API) and
ESA WorldCover 10 m 2021 v200 (© ESA, CC BY 4.0), both read from their open cloud copies onto the game's 10 m grid.

`python3 -m tools.mapgen.fetch karhumaki land` fetches only the satellite land cover (needs `pip install rasterio`).
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
(way["landuse"]({b}); way["natural"]({b}); way["waterway"]({b}); way["highway"]({b}); way["railway"]({b});
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


WORLDCOVER = "https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/ESA_WorldCover_10m_2021_v200_{tile}_Map.tif"
STAC = "https://earth-search.aws.element84.com/v1"
LAND_CELL_M = 10
SOURCES = ("Contains modified Copernicus Sentinel data ({year}); "
           "© ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by ESA WorldCover "
           "consortium, CC BY 4.0 (creativecommons.org/licenses/by/4.0), resampled and reclassified for the game")


def local_grid(area):
    """The game's own 10 m grid over the area as a lon/lat transform (the generator's projection is linear in lon/lat)."""
    from rasterio.transform import Affine

    kx = 111_320 * math.cos(math.radians(area["lat"]))
    ky = 110_574
    west = area["lon"] - area["size_m"] / 2 / kx
    north = area["lat"] + area["size_m"] / 2 / ky
    n = area["size_m"] // LAND_CELL_M
    return Affine(LAND_CELL_M / kx, 0, west, 0, -LAND_CELL_M / ky, north), n


def summer_scenes(area):
    """Summer Sentinel-2 scenes over the area, clearest first."""
    s, w, n, e = bbox(area)
    body = {"collections": ["sentinel-2-l2a"], "bbox": [w, s, e, n], "datetime": "2021-06-01T00:00:00Z/2025-08-31T23:59:59Z",
            "query": {"eo:cloud_cover": {"lt": 2}}, "limit": 100}
    req = urllib.request.Request(STAC + "/search", data=json.dumps(body).encode(), headers={"Content-Type": "application/json", "User-Agent": UA})
    found = json.load(urllib.request.urlopen(req, timeout=60))["features"]
    summer = [f for f in found if f["properties"]["datetime"][5:7] in ("06", "07", "08")]
    return sorted(summer, key=lambda f: (f["properties"]["eo:cloud_cover"], f["id"]))


def fetch_land(area):
    """Satellite land cover on the game's 10 m grid: Sentinel-2 true colour and NDVI from the clearest summer scene, and
    the ESA WorldCover classes. Reprojected here so the generator needs no projection library."""
    import numpy as np
    import rasterio
    from rasterio.warp import Resampling, reproject

    transform, n = local_grid(area)

    def read(scene, name):
        with rasterio.open(scene["assets"][name]["href"]) as src:
            out = np.zeros((n, n), np.float32)
            reproject(rasterio.band(src, 1), out, dst_transform=transform, dst_crs="EPSG:4326", resampling=Resampling.bilinear)
            return out

    # The clearest scene that covers the whole area (one at the edge of a swath leaves part of it black).
    for scene in summer_scenes(area):
        red = read(scene, "red")
        if (red == 0).mean() < 0.001:
            break
    else:
        raise RuntimeError("no summer Sentinel-2 scene covers the whole area")
    bands = {"red": red, **{name: read(scene, name) for name in ("green", "blue", "nir")}}
    tile = f"N{int(math.floor(area['lat'] / 3) * 3):02d}E{int(math.floor(area['lon'] / 3) * 3):03d}"
    with rasterio.open("/vsicurl/" + WORLDCOVER.format(tile=tile)) as src:
        classes = np.zeros((n, n), np.uint8)
        reproject(rasterio.band(src, 1), classes, dst_transform=transform, dst_crs="EPSG:4326", resampling=Resampling.nearest)
    ndvi = (bands["nir"] - bands["red"]) / np.maximum(bands["nir"] + bands["red"], 1)
    rgb = np.clip(np.stack([bands[c] for c in ("red", "green", "blue")], -1) / 2500 * 255, 0, 255).astype(np.uint8)
    meta = {"cell_m": LAND_CELL_M, "size": n, "scene": scene["id"], "date": scene["properties"]["datetime"][:10],
            "sources": SOURCES.format(year=scene["properties"]["datetime"][:4])}
    return rgb, np.clip((ndvi + 1) * 127.5, 0, 255).astype(np.uint8), classes, meta


def save_land(area, out):
    from PIL import Image
    rgb, ndvi, classes, meta = fetch_land(area)
    Image.fromarray(rgb, "RGB").save(out / "satellite.png", optimize=True)
    Image.fromarray(ndvi, "L").save(out / "ndvi.png", optimize=True)
    Image.fromarray(classes, "L").save(out / "worldcover.png", optimize=True)
    (out / "land.json").write_text(json.dumps(meta, indent=1))
    print(f"wrote {out}: satellite land cover {meta['size']} x {meta['size']} from {meta['scene']}")


def main(name):
    area = AREAS[name]
    out = DATA / name
    out.mkdir(parents=True, exist_ok=True)
    only_land = len(sys.argv) > 2 and sys.argv[2] == "land"
    if not only_land:
        features, shore = fetch_osm(area)
        (out / "osm.json").write_text(json.dumps(features, ensure_ascii=False))
        (out / "shore.json").write_text(json.dumps(shore, ensure_ascii=False))
        (out / "dem.json").write_text(json.dumps(fetch_dem(area)))
        print(f"wrote {out}: {len(features['elements'])} features, {len(shore['elements'])} shore ways")
    try:
        save_land(area, out)
    except (ImportError, RuntimeError, OSError) as ex:
        if only_land:
            raise
        # The generator makes its own clearings without the satellite.
        print(f"no satellite land cover ({ex}); `pip install rasterio` and run `fetch {name} land` to add it")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "karhumaki")
