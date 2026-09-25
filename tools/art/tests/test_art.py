import pathlib
import unittest

import numpy as np
from PIL import Image

from tools.art import objects, palette, portraits, raster, soldiers, terrain


def wrap_ratio(channel):
    """Mean |last-first| column difference relative to mean neighbouring column difference."""
    c = channel.astype(np.float64)
    seam = np.abs(c[:, -1] - c[:, 0]).mean()
    inner = np.abs(np.diff(c, axis=1)).mean()
    return seam / max(inner, 1e-9)


class PaletteTests(unittest.TestCase):
    def test_quantize_uses_only_palette_colours_and_binary_alpha(self):
        rng = np.random.default_rng(1)
        arr = rng.integers(0, 256, size=(16, 16, 4), dtype=np.uint8)
        out = np.array(palette.quantize(Image.fromarray(arr, "RGBA")))
        colours = {tuple(p) for p in out[out[..., 3] == 255][:, :3]}
        self.assertTrue(colours <= set(palette.PALETTE))
        self.assertTrue(set(np.unique(out[..., 3])) <= {0, 255})

    def test_palette_is_small(self):
        self.assertLessEqual(len(palette.PALETTE), 56)


class RasterTests(unittest.TestCase):
    def test_periodic_noise_is_tileable_and_normalised(self):
        n = raster.periodic_noise(128, 4, seed=3)
        self.assertEqual(n.shape, (128, 128))
        self.assertGreaterEqual(n.min(), 0.0)
        self.assertLessEqual(n.max(), 1.0)
        self.assertLess(wrap_ratio(n * 255), 3.0)
        self.assertLess(wrap_ratio((n * 255).T), 3.0)

    def test_periodic_noise_is_deterministic(self):
        self.assertTrue(np.array_equal(raster.periodic_noise(64, 4, 9), raster.periodic_noise(64, 4, 9)))

    def test_dither_ramp_only_uses_ramp_colours(self):
        ramp = [(10, 10, 10), (100, 100, 100), (200, 200, 200)]
        out = raster.dither_ramp(np.linspace(0, 1, 64 * 64).reshape(64, 64), ramp)
        self.assertEqual(out.shape, (64, 64, 3))
        self.assertTrue({tuple(p) for p in out.reshape(-1, 3)} <= set(ramp))

    def test_render_sprite_outlines_and_centres_shape(self):
        def draw(pen):
            pen.ellipse(0, 0, 0.3, 0.3, (94, 100, 80))

        img = raster.render_sprite(64, draw)
        arr = np.array(img)
        self.assertEqual(img.size, (64, 64))
        self.assertEqual(arr[32, 32, 3], 255)
        self.assertEqual(arr[0, 0, 3], 0)
        opaque = arr[..., 3] == 255
        outline = np.all(arr[..., :3] == palette.OUTLINE, axis=-1) & opaque
        self.assertGreater(outline.sum(), 20)

    def test_pen_rotation_moves_forward_point(self):
        north = raster.render_sprite(64, lambda pen: pen.ellipse(0, 0.6, 0.1, 0.1, (94, 100, 80)), angle_deg=0)
        east = raster.render_sprite(64, lambda pen: pen.ellipse(0, 0.6, 0.1, 0.1, (94, 100, 80)), angle_deg=90)
        ys, xs = np.nonzero(np.array(north)[..., 3])
        self.assertLess(ys.mean(), 20)
        ys, xs = np.nonzero(np.array(east)[..., 3])
        self.assertGreater(xs.mean(), 44)


class TerrainTests(unittest.TestCase):
    def test_textures_are_128_rgb_and_seamless(self):
        for name, make in terrain.TEXTURES.items():
            with self.subTest(name=name):
                img = make(7)
                self.assertEqual(img.size, (128, 128))
                self.assertEqual(img.mode, "RGB")
                arr = np.array(img).astype(np.float64).mean(axis=-1)
                self.assertLess(wrap_ratio(arr), 3.0)
                self.assertLess(wrap_ratio(arr.T), 3.0)

    def test_water_texture_exists_and_is_bluish(self):
        self.assertIn("water", terrain.TEXTURES)
        r, g, b = np.array(terrain.TEXTURES["water"](7)).astype(np.float64).mean(axis=(0, 1))
        self.assertGreater(b, r)

    def test_textures_differ_from_each_other(self):
        means = {name: np.array(make(7)).mean(axis=(0, 1)) for name, make in terrain.TEXTURES.items()}
        names = list(means)
        for i, a in enumerate(names):
            for b in names[i + 1:]:
                self.assertGreater(np.abs(means[a] - means[b]).sum(), 12, f"{a} vs {b}")


class ObjectTests(unittest.TestCase):
    def test_strips_have_declared_sizes_and_content(self):
        for name, (size, count, _) in objects.OBJECTS.items():
            with self.subTest(name=name):
                img = objects.strip(name)
                self.assertEqual(img.size, (size * count, size))
                arr = np.array(img)
                for i in range(count):
                    cell = arr[:, i * size:(i + 1) * size, 3]
                    self.assertGreater((cell > 0).sum(), size * size // 10)
                    self.assertEqual(cell[0, 0], 0)

    def test_blood_strip_exists(self):
        img = objects.strip("blood")
        self.assertEqual(img.size, (128, 64))
        self.assertGreater((np.array(img)[..., 3] > 0).sum(), 400)

    def test_sprites_except_shadow_use_binary_alpha(self):
        for name in ("spruce", "birch", "rock", "bush"):
            alpha = np.array(objects.strip(name))[..., 3]
            self.assertTrue(set(np.unique(alpha)) <= {0, 255}, name)


class SoldierTests(unittest.TestCase):
    def test_meta_matches_spec_contract(self):
        meta = soldiers.sheet_meta()
        self.assertEqual(meta["cellSize"], 64)
        self.assertEqual(meta["pixelsPerMetre"], 32)
        self.assertEqual(meta["directions"], ["N", "NE", "E", "SE", "S", "SW", "W", "NW"])
        self.assertEqual(
            {k: (v["row"], v["frames"], v["strideCm"]) for k, v in meta["animations"].items()},
            {"idle": (0, 1, 0), "walk": (8, 6, 120), "run": (16, 6, 180),
             "crouch": (24, 1, 0), "prone": (32, 1, 0), "crawl": (40, 4, 60), "dead": (48, 1, 0)})

    def test_sheets_fill_exactly_the_declared_frames(self):
        meta = soldiers.sheet_meta()
        for faction in soldiers.FACTIONS:
            with self.subTest(faction=faction):
                arr = np.array(soldiers.sheet(faction))
                self.assertEqual(arr.shape, (3584, 384, 4))
                self.assertTrue(set(np.unique(arr[..., 3])) <= {0, 255})
                for anim in meta["animations"].values():
                    for d in range(8):
                        for f in range(6):
                            y, x = (anim["row"] + d) * 64, f * 64
                            opaque = (arr[y:y + 64, x:x + 64, 3] > 0).sum()
                            if f < anim["frames"]:
                                self.assertGreater(opaque, 120)
                                self.assertEqual(arr[y, x, 3], 0)
                            else:
                                self.assertEqual(opaque, 0)

    def test_factions_look_different(self):
        a = np.array(soldiers.sheet("finnish"))[:64, :64, :3].astype(int)
        b = np.array(soldiers.sheet("soviet"))[:64, :64, :3].astype(int)
        self.assertGreater(np.abs(a - b).sum(), 10_000)

    def test_generation_is_deterministic(self):
        self.assertEqual(soldiers.sheet("soviet").tobytes(), soldiers.sheet("soviet").tobytes())


class PortraitTests(unittest.TestCase):
    def test_strip_size_and_variety(self):
        for faction in soldiers.FACTIONS:
            arr = np.array(portraits.strip(faction))
            self.assertEqual(arr.shape, (64, 512, 4))
            faces = [arr[:, i * 64:(i + 1) * 64, :3].astype(int) for i in range(8)]
            for i in range(7):
                self.assertGreater(np.abs(faces[i] - faces[i + 1]).sum(), 1000)


class AssembleSheetTests(unittest.TestCase):
    def test_assembles_frames_mirrors_west_and_downsamples(self):
        import tempfile
        from tools.art import assemble_sheet
        with tempfile.TemporaryDirectory() as tmp:
            folder = pathlib.Path(tmp)
            east = soldiers.frame("finnish", "idle", 2, 0).resize((256, 256), Image.NEAREST)
            east.save(folder / "idle_E_0.png")
            soldiers.frame("finnish", "idle", 0, 0).save(folder / "idle_N_0.png")
            sheet, missing = assemble_sheet.assemble(folder)
            arr = np.array(sheet)
            self.assertEqual(arr.shape, (3584, 384, 4))
            north = arr[0:64, 0:64]
            east_cell = arr[2 * 64:3 * 64, 0:64]
            west_cell = arr[6 * 64:7 * 64, 0:64]
            self.assertGreater((north[..., 3] > 0).sum(), 120)
            self.assertTrue(np.array_equal(west_cell[..., 3], east_cell[..., 3][:, ::-1]))
            self.assertIn("walk_N_0.png", missing)
            self.assertNotIn("idle_W_0.png", missing)

    def test_rejects_frames_that_are_not_multiples_of_the_cell(self):
        import tempfile
        from tools.art import assemble_sheet
        with tempfile.TemporaryDirectory() as tmp:
            Image.new("RGBA", (50, 50)).save(pathlib.Path(tmp) / "idle_N_0.png")
            with self.assertRaises(ValueError):
                assemble_sheet.assemble(tmp)


class AssembleFallbackTests(unittest.TestCase):
    def _folder(self, tmp, frames):
        folder = pathlib.Path(tmp)
        for name, img in frames.items():
            img.save(folder / name)
        return folder

    def test_missing_animations_fall_back_so_no_cell_is_empty(self):
        import tempfile
        from tools.art import assemble_sheet
        with tempfile.TemporaryDirectory() as tmp:
            folder = self._folder(tmp, {"idle_N_0.png": soldiers.frame("finnish", "idle", 0, 0)})
            sheet, missing = assemble_sheet.assemble(folder)
            arr = np.array(sheet)
            meta = soldiers.sheet_meta()
            for anim in meta["animations"].values():
                for f in range(anim["frames"]):
                    cell = arr[anim["row"] * 64:(anim["row"] + 1) * 64, f * 64:(f + 1) * 64, 3]
                    self.assertGreater((cell > 0).sum(), 100)
            self.assertIn("run_N_2.png", missing)

    def test_existing_outline_in_brief_colour_is_not_doubled(self):
        import tempfile
        from tools.art import assemble_sheet
        img = raster.render_sprite(64, lambda pen: pen.ellipse(0, 0, 0.3, 0.3, (94, 100, 80)), outline=(56, 52, 44))
        with tempfile.TemporaryDirectory() as tmp:
            out = assemble_sheet.load_frame(self._folder(tmp, {"idle_N_0.png": img}), "idle", "N", 0)
        self.assertEqual((np.array(img)[..., 3] > 0).sum(), (np.array(out)[..., 3] > 0).sum())

    def test_frame_without_outline_gets_one_despite_dark_detail(self):
        import tempfile
        from tools.art import assemble_sheet
        arr = np.zeros((64, 64, 4), dtype=np.uint8)
        arr[20:44, 20:44] = (146, 150, 124, 255)
        arr[30, 30] = (22, 20, 18, 255)  # a dark eye pixel, not an outline
        with tempfile.TemporaryDirectory() as tmp:
            out = assemble_sheet.load_frame(self._folder(tmp, {"idle_N_0.png": Image.fromarray(arr, "RGBA")}), "idle", "N", 0)
        self.assertEqual((np.array(out)[..., 3] > 0).sum(), 26 * 26 - 4)

    def test_opaque_background_is_rejected(self):
        import tempfile
        from tools.art import assemble_sheet
        with tempfile.TemporaryDirectory() as tmp:
            folder = self._folder(tmp, {"idle_N_0.png": Image.new("RGBA", (64, 64), (255, 255, 255, 255))})
            with self.assertRaises(ValueError):
                assemble_sheet.load_frame(folder, "idle", "N", 0)

    def test_portraits_are_joined_into_a_strip(self):
        import tempfile
        from tools.art import assemble_sheet
        with tempfile.TemporaryDirectory() as tmp:
            folder = pathlib.Path(tmp)
            for i in range(8):
                portraits.portrait("finnish", i).resize((128, 128), Image.NEAREST).save(folder / f"portrait_finnish_{i}.png")
            strip = assemble_sheet.assemble_portraits(folder, "finnish")
        self.assertEqual(strip.size, (512, 64))
        self.assertEqual(np.array(strip)[..., 3].min(), 255)


if __name__ == "__main__":
    unittest.main()
