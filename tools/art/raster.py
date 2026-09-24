"""Drawing helpers: tileable noise, ordered dithering and supersampled vector sprites."""
import math

import numpy as np
from PIL import Image, ImageDraw

from .palette import OUTLINE, quantize

SS = 4                 # supersampling factor for sprites
PX_PER_M = 32          # art scale: one metre is 32 pixels
LIGHT = (-0.6, -0.8)   # screen-space direction toward the light (north-west, mostly north)

BAYER4 = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], dtype=np.float64) / 16.0


def periodic_noise(size, cells, seed, octaves=4, persistence=0.5):
    """Tileable value noise in 0..1; the first octave has `cells` lattice cells across."""
    rng = np.random.default_rng(seed)
    total = np.zeros((size, size))
    amplitude, norm = 1.0, 0.0
    for octave in range(octaves):
        n = cells * (2 ** octave)
        if n > size:
            break
        lattice = rng.random((n, n))
        coords = np.arange(size) * n / size
        i0 = np.floor(coords).astype(int)
        f = coords - i0
        f = f * f * (3 - 2 * f)
        i1 = (i0 + 1) % n
        a, b = lattice[np.ix_(i0, i0)], lattice[np.ix_(i0, i1)]
        c, d = lattice[np.ix_(i1, i0)], lattice[np.ix_(i1, i1)]
        fy, fx = f[:, None], f[None, :]
        total += ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy) * amplitude
        norm += amplitude
        amplitude *= persistence
    total /= norm
    lo, hi = total.min(), total.max()
    return (total - lo) / max(hi - lo, 1e-9)


def dither_ramp(values, ramp):
    """Maps a 0..1 array onto `ramp` colours with 4x4 ordered dithering."""
    h, w = values.shape
    threshold = np.tile(BAYER4, (h // 4 + 1, w // 4 + 1))[:h, :w]
    n = len(ramp)
    idx = np.clip(np.floor(values * (n - 1) + threshold), 0, n - 1).astype(int)
    return np.array(ramp, dtype=np.uint8)[idx]


def add_outline(image, color=OUTLINE):
    """Paints every transparent pixel that touches an opaque one (4-neighbourhood)."""
    arr = np.array(image)
    solid = arr[..., 3] > 0
    grown = np.zeros_like(solid)
    grown[1:, :] |= solid[:-1, :]
    grown[:-1, :] |= solid[1:, :]
    grown[:, 1:] |= solid[:, :-1]
    grown[:, :-1] |= solid[:, 1:]
    edge = grown & ~solid
    arr[edge] = (*color, 255)
    return Image.fromarray(arr, "RGBA")


def lighter(color, amount=28):
    return tuple(min(255, c + amount) for c in color)


def darker(color, amount=28):
    return tuple(max(0, c - amount) for c in color)


class Pen:
    """Draws body-space shapes (metres; x right, y forward) onto a supersampled canvas rotated by angle_deg (clockwise from north)."""

    def __init__(self, draw, size_px, angle_deg=0.0):
        self.draw = draw
        self.centre = size_px * SS / 2
        self.k = PX_PER_M * SS
        a = math.radians(angle_deg)
        self.cos, self.sin = math.cos(a), math.sin(a)

    def to_screen(self, x, y):
        return (self.centre + self.k * (x * self.cos + y * self.sin),
                self.centre + self.k * (x * self.sin - y * self.cos))

    def polygon(self, points, color):
        self.draw.polygon([self.to_screen(x, y) for x, y in points], fill=(*color, 255))

    def ellipse(self, cx, cy, rx, ry, color, highlight=None, segments=28):
        pts = [(cx + rx * math.cos(2 * math.pi * i / segments), cy + ry * math.sin(2 * math.pi * i / segments))
               for i in range(segments)]
        screen = [self.to_screen(x, y) for x, y in pts]
        self.draw.polygon(screen, fill=(*color, 255))
        if highlight is not None:
            # A smaller blob nudged toward the light gives a consistent NW highlight in every rotation.
            sx, sy = self.to_screen(cx, cy)
            r = min(rx, ry) * self.k
            ox, oy = LIGHT[0] * r * 0.35, LIGHT[1] * r * 0.35
            inner = [(sx + (px - sx) * 0.55 + ox, sy + (py - sy) * 0.55 + oy) for px, py in screen]
            self.draw.polygon(inner, fill=(*highlight, 255))

    def line(self, x0, y0, x1, y1, width_m, color):
        self.draw.line([self.to_screen(x0, y0), self.to_screen(x1, y1)], fill=(*color, 255),
                       width=max(1, int(round(width_m * self.k))))


def render_sprite(size, draw_fn, outline=OUTLINE, angle_deg=0.0):
    """Draws `draw_fn(pen)` at 4x, downsamples, snaps to the palette and adds a 1 px outline."""
    big = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    draw_fn(Pen(ImageDraw.Draw(big), size, angle_deg))
    small = big.resize((size, size), Image.BOX)
    return add_outline(quantize(small), outline)
