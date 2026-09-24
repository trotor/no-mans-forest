import unittest

import numpy as np
from PIL import Image

from tools.art import palette, raster


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


if __name__ == "__main__":
    unittest.main()
