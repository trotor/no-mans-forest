import base64
import re
import unittest
import zlib

import numpy as np

from tools.mapgen import generate as g
from tools.mapgen.areas import AREAS


def area(size=200):
    return {"lat": 62.88, "lon": 34.44, "size_m": size, "lake_seed": (size - 1, 0), "title": "test"}


def dem(value=50.0, points=5):
    return {"rows_north_to_south": [[value] * points for _ in range(points)]}


def ring(project, lat_lon_box):
    s, w, n, e = lat_lon_box
    pts = [(n, w), (n, e), (s, e), (s, w), (n, w)]
    return [{"lat": a, "lon": b} for a, b in pts]


class ProjectionTests(unittest.TestCase):
    def test_projection_corners(self):
        p = g.Projection(62.88, 34.44, 1000)
        self.assertEqual(p(62.88, 34.44), (500, 500))
        x, y = p(62.88 + 500 / 110_574, 34.44 - 500 / p.kx)
        self.assertAlmostEqual(x, 0, places=6)
        self.assertAlmostEqual(y, 0, places=6)


class TerrainTests(unittest.TestCase):
    def build(self, elements, shore=None, dem_data=None, size=200):
        return g.build(area(size), {"elements": elements}, {"elements": shore or []}, dem_data or dem(), seed=3)

    def test_terrain_priority(self):
        p = g.Projection(62.88, 34.44, 200)
        half_lat, half_lon = 100 / 110_574, 100 / p.kx
        whole = (62.88 - half_lat * 0.9, 34.44 - half_lon * 0.9, 62.88 + half_lat * 0.9, 34.44 + half_lon * 0.9)
        middle = (62.88 - half_lat * 0.3, 34.44 - half_lon * 0.3, 62.88 + half_lat * 0.3, 34.44 + half_lon * 0.3)
        road = [{"lat": 62.88 + half_lat, "lon": 34.44}, {"lat": 62.88 - half_lat, "lon": 34.44}]
        elements = [
            {"type": "way", "tags": {"natural": "scrub"}, "geometry": ring(p, whole)},
            {"type": "way", "tags": {"natural": "wetland"}, "geometry": ring(p, middle)},
            {"type": "way", "tags": {"highway": "track"}, "geometry": road},
        ]
        data = self.build(elements)
        self.assertEqual(data.terrain[100, 100], g.ROAD)      # the road crosses the bog
        self.assertEqual(data.terrain[100, 80], g.SWAMP)      # bog over scrub
        self.assertEqual(data.terrain[100, 1], g.FOREST)      # untagged land is forest (unless a clearing)
        self.assertIn(data.terrain[100, 30], (g.GRASS,))      # scrub is open ground with bushes

    def test_lake_fill_from_corner(self):
        p = g.Projection(62.88, 34.44, 200)
        # A shore line cutting off the north-east corner from (200, 100) to (100, 0).
        lat = lambda y: 62.88 - (y - 100) / 110_574
        lon = lambda x: 34.44 + (x - 100) / p.kx
        shore = [{"type": "way", "geometry": [{"lat": lat(100), "lon": lon(205)}, {"lat": lat(-5), "lon": lon(100)}]}]
        data = self.build([], shore=shore)
        self.assertEqual(data.terrain[5, 195], g.WATER)
        self.assertNotEqual(data.terrain[150, 50], g.WATER)
        self.assertNotEqual(data.terrain[60, 120], g.WATER)

    def test_height_zero_at_lake_and_steps(self):
        p = g.Projection(62.88, 34.44, 200)
        lat = lambda y: 62.88 - (y - 100) / 110_574
        lon = lambda x: 34.44 + (x - 100) / p.kx
        shore = [{"type": "way", "geometry": [{"lat": lat(100), "lon": lon(205)}, {"lat": lat(-5), "lon": lon(100)}]}]
        rows = [[40.0 + i * 2 for _ in range(5)] for i in range(5)]  # rising to the south
        data = self.build([], shore=shore, dem_data={"rows_north_to_south": rows})
        self.assertTrue((data.height_cm[data.terrain == g.WATER] == 0).all())
        self.assertTrue((data.height_cm >= 0).all())
        self.assertTrue((data.height_cm % g.HEIGHT_STEP_CM == 0).all())
        self.assertGreater(data.height_cm[190, 100], data.height_cm[20, 20])

    def test_rings_are_assembled_from_pieces(self):
        a, b, c = (0, 0), (0, 1), (1, 1)
        rings = g.assemble_rings([[a, b], [c, b], [c, a]])
        self.assertEqual(len(rings), 1)
        self.assertEqual(rings[0][0], rings[0][-1])


class LakeSafetyTests(unittest.TestCase):
    def test_seed_on_the_shore_line_fails_loudly(self):
        with self.assertRaises(ValueError):
            g.lake_mask(100, [[(0, 50), (99, 50)]], (40, 50))

    def test_shore_that_does_not_cut_the_map_fails_loudly(self):
        with self.assertRaises(ValueError):
            g.lake_mask(100, [[(20, 20), (60, 60)]], (99, 0))


class OutputTests(unittest.TestCase):
    def test_heights_tileset_steps(self):
        xml = g.heights_tileset()
        values = [int(v) for v in re.findall(r'name="height_cm" type="int" value="(\d+)"', xml)]
        self.assertEqual(len(values), g.HEIGHT_TILES)
        self.assertEqual(values[:3], [0, 25, 50])
        self.assertEqual(values[-1], (g.HEIGHT_TILES - 1) * g.HEIGHT_STEP_CM)

    def test_tmx_roundtrip_structure(self):
        data = g.build(area(64), {"elements": []}, {"elements": []}, dem(), seed=3)
        data.blue, data.red, data.patrol = [(5, 5)], [(50, 50)], [(10, 10), (20, 10)]
        text = g.tmx(data)
        layers = re.findall(r'<data encoding="base64" compression="zlib">\s*(\S+)\s*</data>', text)
        self.assertEqual(len(layers), 3)
        terrain = np.frombuffer(zlib.decompress(base64.b64decode(layers[0])), "<u4")
        self.assertEqual(terrain.size, 64 * 64)
        self.assertTrue(set(np.unique(terrain)) <= {1, 2, 3, 4, 5})
        heights = np.frombuffer(zlib.decompress(base64.b64decode(layers[1])), "<u4")
        self.assertTrue(((heights >= g.HEIGHT_FIRST_GID) & (heights < g.OBSTACLE_FIRST_GID)).all())
        self.assertIn('name="source"', text)
        self.assertIn("OpenStreetMap", text)


class RealAreaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = g.build(AREAS["karhumaki"], *g.load("karhumaki"))

    def test_deterministic(self):
        again = g.build(AREAS["karhumaki"], *g.load("karhumaki"))
        self.assertTrue((again.terrain == self.data.terrain).all())
        self.assertTrue((again.height_cm == self.data.height_cm).all())
        self.assertTrue((again.obstacles == self.data.obstacles).all())
        self.assertEqual(again.red, self.data.red)

    def test_spawns_passable_and_apart(self):
        d = self.data
        for x, y in d.blue + d.red + d.patrol:
            self.assertNotEqual(d.terrain[y, x], g.WATER)
            self.assertEqual(d.obstacles[y, x], g.NO_OBSTACLE)
        (bx, by), (rx, ry) = d.blue[0], d.red[0]
        self.assertTrue(250 <= np.hypot(rx - bx, ry - by) <= 450)
        self.assertEqual(len(d.blue), 7)  # strike squad 4, support squad 3
        self.assertEqual(len(d.red), 9)   # the post 5, the reserve 4

    def test_windfalls_in_the_forest(self):
        d = self.data
        self.assertGreater(len(d.logs), 100)
        text = g.tmx(d)
        self.assertEqual(text.count('type="log"'), len(d.logs))
        for x0, y0, x1, y1 in d.logs:
            self.assertTrue(3 <= np.hypot(x1 - x0, y1 - y0) <= 6.5)
            cells = g.line_cells(x0, y0, x1, y1)
            self.assertTrue(all(d.obstacles[y, x] == g.LOG for x, y in cells), (x0, y0, x1, y1))
            self.assertTrue(all(d.terrain[y, x] == g.FOREST for x, y in cells))
        for x, y in d.blue + d.red + d.foxholes:  # never across a start point or a foxhole
            self.assertNotEqual(d.obstacles[y, x], g.LOG)

    def test_foxholes_on_the_knoll(self):
        d = self.data
        self.assertEqual(len(d.foxholes), 8)
        for x, y in d.red[:5]:  # every man of the post starts in one
            self.assertIn((x, y), d.foxholes)
        for x, y in d.foxholes:
            around = [d.height_cm[y + dy, x + dx] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))]
            self.assertGreaterEqual(min(around) - d.height_cm[y, x], 100, (x, y))
            self.assertEqual(d.obstacles[y, x], g.NO_OBSTACLE)
        self.assertEqual(g.tmx(d).count('type="foxhole"'), 8)

    def test_the_reserve_waits_behind_the_post(self):
        d = self.data
        bx, by = np.mean(d.blue[:4], axis=0)
        post = np.mean([np.hypot(x - bx, y - by) for x, y in d.red[:5]])
        reserve = [np.hypot(x - bx, y - by) for x, y in d.red[5:]]
        self.assertTrue(all(r > post + 30 for r in reserve), f"post {post:.0f} m, reserve {reserve}")
        rx, ry = d.red[0]
        self.assertTrue(all(np.hypot(x - rx, y - ry) < 90 for x, y in d.red[5:]))

    def test_zones(self):
        zones = {name: (x0, y0, x1, y1) for name, _, x0, y0, x1, y1 in self.data.zones}
        x0, y0, x1, y1 = zones["start_zone"]
        self.assertTrue(all(x0 <= x < x1 and y0 <= y < y1 for x, y in self.data.blue))
        self.assertEqual((x1 - x0, y1 - y0), (40, 30))
        x0, y0, x1, y1 = zones["outpost"]
        self.assertEqual((x1 - x0, y1 - y0), (70, 50))
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        rx, ry = self.data.red[0]
        self.assertTrue(5 <= np.hypot(cx - rx, cy - ry) <= 15)  # reported roughly, not exactly

    def test_zones_in_the_tmx(self):
        text = g.tmx(self.data)
        self.assertIn('name="start_zone" type="zone"', text)
        self.assertIn('name="outpost" type="zone"', text)

    def test_bushes_only_where_they_are_drawn(self):
        # The game draws no bushes in forest cells, so none may stand there unseen (they block sight).
        self.assertEqual(int(((self.data.terrain == g.FOREST) & (self.data.obstacles == g.BUSH)).sum()), 0)

    def test_has_the_lake_bog_and_relief(self):
        d = self.data
        self.assertGreater((d.terrain == g.WATER).mean(), 0.002)
        self.assertGreater((d.terrain == g.SWAMP).mean(), 0.02)
        self.assertGreater(d.height_cm.max(), 5000)


if __name__ == "__main__":
    unittest.main()
