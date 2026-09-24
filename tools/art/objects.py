"""Top-down map objects: trees, rocks, bushes and a soft drop shadow."""
import math

import numpy as np
from PIL import Image, ImageDraw

from .raster import SS, lighter, render_sprite

CONIFER = [(22, 40, 26), (32, 56, 34), (46, 76, 42), (64, 98, 52)]
BIRCH = [(78, 112, 48), (104, 140, 60), (136, 168, 80)]
ROCK = [(84, 84, 80), (108, 108, 102), (134, 132, 124), (162, 160, 150)]
BUSH = [(32, 56, 34), (46, 76, 42), (64, 98, 52), (78, 112, 48)]
DARK_GREEN_OUTLINE = (22, 40, 26)


def _star(cx, cy, r_outer, r_inner, spikes, rotation):
    pts = []
    for i in range(spikes * 2):
        r = r_outer if i % 2 == 0 else r_inner
        a = rotation + math.pi * i / spikes
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def spruce(variant):
    rng = np.random.default_rng(100 + variant)
    spikes = 11 + variant
    rot = rng.random() * math.pi

    def draw(pen):
        # layered star from the dark base up to the lit tip, each layer nudged toward the light (north-west)
        for layer, (radius, colour) in enumerate([(1.35, CONIFER[0]), (1.1, CONIFER[1]), (0.8, CONIFER[2]), (0.45, CONIFER[3])]):
            shift = -0.08 * layer
            pen.polygon(_star(shift, -shift, radius, radius * 0.72, spikes, rot + layer * 0.3), colour)
        pen.ellipse(-0.12, 0.12, 0.12, 0.12, lighter(CONIFER[3], 20))

    return render_sprite(96, draw, outline=DARK_GREEN_OUTLINE)


def birch(variant):
    rng = np.random.default_rng(200 + variant)
    blobs = [(rng.uniform(-0.75, 0.75), rng.uniform(-0.75, 0.75), rng.uniform(0.28, 0.45)) for _ in range(16)]
    blobs = [b for b in blobs if math.hypot(b[0], b[1]) < 0.85]

    def draw(pen):
        for x, y, r in blobs:
            pen.ellipse(x + 0.06, y - 0.06, r, r, BIRCH[0])
        for x, y, r in blobs:
            pen.ellipse(x, y, r * 0.8, r * 0.8, BIRCH[1], highlight=BIRCH[2])

    return render_sprite(80, draw, outline=DARK_GREEN_OUTLINE)


def rock(variant):
    rng = np.random.default_rng(300 + variant)
    n = 9
    radii = rng.uniform(0.28, 0.42, size=n)
    pts = [(radii[i] * math.cos(2 * math.pi * i / n), radii[i] * math.sin(2 * math.pi * i / n)) for i in range(n)]

    def draw(pen):
        pen.polygon(pts, ROCK[0])
        pen.polygon([(x * 0.8 - 0.03, y * 0.8 + 0.04) for x, y in pts], ROCK[1])
        pen.polygon([(x * 0.45 - 0.08, y * 0.45 + 0.1) for x, y in pts], ROCK[2])
        pen.ellipse(-0.12, 0.14, 0.05, 0.04, ROCK[3])

    return render_sprite(32, draw)


def bush(variant):
    rng = np.random.default_rng(400 + variant)
    blobs = [(rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), rng.uniform(0.14, 0.24)) for _ in range(9)]

    def draw(pen):
        for x, y, r in blobs:
            pen.ellipse(x, y, r, r, BUSH[0])
        for x, y, r in blobs:
            pen.ellipse(x - 0.02, y + 0.02, r * 0.75, r * 0.75, BUSH[1 + (int(r * 100) % 2)], highlight=BUSH[3])

    return render_sprite(40, draw, outline=DARK_GREEN_OUTLINE)


def shadow(_variant):
    """Soft black ellipse with an alpha gradient (not palette-snapped; drawn translucent by the game)."""
    size = 64
    yy, xx = np.mgrid[0:size, 0:size]
    d = np.sqrt(((xx - 31.5) / 30.0) ** 2 + ((yy - 31.5) / 30.0) ** 2)
    alpha = np.clip(1.0 - d, 0.0, 1.0) ** 0.8 * 150
    arr = np.zeros((size, size, 4), dtype=np.uint8)
    arr[..., 3] = alpha.astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


OBJECTS = {
    "spruce": (96, 3, spruce),
    "birch": (80, 3, birch),
    "rock": (32, 4, rock),
    "bush": (40, 4, bush),
    "shadow": (64, 1, shadow),
}


def strip(name):
    size, count, make = OBJECTS[name]
    out = Image.new("RGBA", (size * count, size), (0, 0, 0, 0))
    for i in range(count):
        out.paste(make(i), (i * size, 0))
    return out
