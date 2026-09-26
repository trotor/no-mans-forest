"""Top-down map objects: trees, rocks, bushes and a soft drop shadow."""
import math

import numpy as np
from PIL import Image, ImageDraw

from .raster import SS, lighter, render_sprite

CONIFER = [(22, 40, 26), (32, 56, 34), (46, 76, 42), (64, 98, 52)]
BIRCH = [(64, 98, 52), (78, 112, 48), (104, 140, 60)]
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


BLOOD = [(110, 24, 20), (140, 32, 26)]


def blood(variant):
    rng = np.random.default_rng(500 + variant)
    blobs = [(rng.uniform(-0.35, 0.35), rng.uniform(-0.3, 0.3), rng.uniform(0.12, 0.28)) for _ in range(7)]

    def draw(pen):
        for x, y, r in blobs:
            pen.ellipse(x, y, r, r * 0.8, BLOOD[0])
        for x, y, r in blobs[:3]:
            pen.ellipse(x, y, r * 0.5, r * 0.4, BLOOD[1])

    return render_sprite(64, draw, outline=(56, 18, 14))


PINE = [(46, 76, 42), (64, 98, 52), (78, 112, 48), (104, 140, 60)]
BARK = (150, 92, 54)
WOOD = [(96, 62, 36), (120, 96, 60), (164, 138, 100)]
FLOOR = [(44, 52, 30), (60, 64, 36), (78, 74, 44)]
FERN = [(54, 78, 36), (70, 98, 44), (88, 118, 52)]
MOSS = [(70, 98, 44), (88, 118, 52), (108, 138, 62)]
GRASS = [(70, 98, 44), (88, 118, 52), (108, 138, 62), (132, 158, 78)]
FLOWERS = [(230, 228, 210), (220, 200, 90), (130, 96, 150)]
SEDGE = [(58, 70, 56), (74, 86, 66), (92, 102, 76), (108, 138, 62)]
POOL = [(46, 58, 58), (64, 80, 80), (92, 102, 76)]


def pine(variant):
    """A Scots pine from above: a few round, open tufts of lighter green, a glimpse of the red-brown bark between them."""
    rng = np.random.default_rng(500 + variant)
    tufts = [(rng.uniform(-0.8, 0.8), rng.uniform(-0.8, 0.8), rng.uniform(0.3, 0.5)) for _ in range(7 + variant)]
    tufts = [t for t in tufts if math.hypot(t[0], t[1]) < 0.9]

    def draw(pen):
        pen.line(0, 0, 0.5, -0.4, 0.1, BARK)
        pen.line(0, 0, -0.45, 0.35, 0.08, BARK)
        for x, y, r in tufts:
            pen.ellipse(x + 0.05, y - 0.05, r, r, PINE[0])
        for x, y, r in tufts:
            pen.ellipse(x, y, r * 0.78, r * 0.78, PINE[1 + int(r * 10) % 2], highlight=PINE[3])
        pen.ellipse(0, 0, 0.12, 0.12, BARK)

    return render_sprite(96, draw, outline=DARK_GREEN_OUTLINE)


def stump(variant):
    def draw(pen):
        pen.ellipse(0, 0, 0.28, 0.26, WOOD[0])
        pen.ellipse(-0.02, 0.02, 0.2, 0.18, WOOD[2 - variant % 2])
        pen.ellipse(-0.02, 0.02, 0.07, 0.06, WOOD[1])
        for a in (0.4, 2.2, 4.0):
            pen.line(0.2 * math.cos(a), 0.2 * math.sin(a), 0.38 * math.cos(a), 0.38 * math.sin(a), 0.06, WOOD[0])

    return render_sprite(32, draw)


def log(variant):
    """A fallen tree lying east–west, 3 m long (the game rotates and stretches it); a broken stub of a branch or two."""
    rng = np.random.default_rng(600 + variant)

    def draw(pen):
        pen.polygon([(-1.45, -0.16), (1.45, -0.12), (1.45, 0.12), (-1.45, 0.17)], WOOD[0])
        pen.polygon([(-1.4, -0.09), (1.4, -0.06), (1.4, 0.04), (-1.4, 0.06)], WOOD[1])
        pen.ellipse(1.45, 0, 0.1, 0.13, WOOD[2])
        for _ in range(2 + variant % 2):
            x = rng.uniform(-1.0, 1.0)
            side = 1 if rng.random() < 0.5 else -1
            pen.line(x, 0, x + rng.uniform(-0.2, 0.2), side * rng.uniform(0.3, 0.45), 0.06, WOOD[0])
        pen.ellipse(-1.2, 0.05, 0.12, 0.08, MOSS[1])

    return render_sprite(96, draw)


def fern(variant):
    rng = np.random.default_rng(700 + variant)
    fronds = 5 + variant

    def draw(pen):
        for i in range(fronds):
            a = 2 * math.pi * i / fronds + rng.uniform(-0.2, 0.2)
            x, y = 0.42 * math.cos(a), 0.42 * math.sin(a)
            pen.polygon([(0, 0), (x + 0.08 * math.sin(a), y - 0.08 * math.cos(a)), (x * 1.05, y * 1.05),
                         (x - 0.08 * math.sin(a), y + 0.08 * math.cos(a))], FERN[i % 2])
            pen.line(0, 0, x * 0.9, y * 0.9, 0.03, FERN[2])

    return render_sprite(32, draw, outline=DARK_GREEN_OUTLINE)


def moss(variant):
    rng = np.random.default_rng(800 + variant)
    blobs = [(rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), rng.uniform(0.08, 0.18)) for _ in range(10)]

    def draw(pen):
        for x, y, r in blobs:
            pen.ellipse(x, y, r, r * 0.8, MOSS[int(r * 100) % 3])

    return render_sprite(32, draw, outline=FLOOR[1])


def tuft(variant):
    rng = np.random.default_rng(900 + variant)

    def draw(pen):
        for _ in range(7):
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(0.18, 0.34)
            pen.line(0, 0, r * math.cos(a), r * math.sin(a), 0.035, GRASS[rng.integers(0, 4)])

    return render_sprite(24, draw, outline=GRASS[0])


def flowers(variant):
    rng = np.random.default_rng(1000 + variant)
    colour = FLOWERS[variant % 3]

    def draw(pen):
        for _ in range(4):
            x, y = rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25)
            pen.line(x, y, x + 0.05, y + 0.1, 0.025, GRASS[1])
            pen.ellipse(x, y, 0.05, 0.05, colour)

    return render_sprite(24, draw, outline=GRASS[0])


def sedge(variant):
    rng = np.random.default_rng(1100 + variant)

    def draw(pen):
        pen.ellipse(0, 0, 0.26, 0.22, SEDGE[0])
        for _ in range(12):
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(0.2, 0.42)
            pen.line(0, 0, r * math.cos(a), r * math.sin(a), 0.03, SEDGE[1 + rng.integers(0, 3)])

    return render_sprite(32, draw, outline=SEDGE[0])


def cotton(variant):
    rng = np.random.default_rng(1200 + variant)

    def draw(pen):
        for _ in range(5):
            x, y = rng.uniform(-0.28, 0.28), rng.uniform(-0.28, 0.28)
            pen.line(x, y, x + 0.03, y + 0.12, 0.025, SEDGE[2])
            pen.ellipse(x, y, 0.06, 0.05, FLOWERS[0])

    return render_sprite(24, draw, outline=SEDGE[0])


def pool(variant):
    rng = np.random.default_rng(1300 + variant)
    n = 10
    radii = rng.uniform(0.45, 0.7, size=n)
    pts = [(radii[i] * math.cos(2 * math.pi * i / n), radii[i] * 0.8 * math.sin(2 * math.pi * i / n)) for i in range(n)]

    def draw(pen):
        pen.polygon(pts, POOL[1])
        pen.polygon([(x * 0.8, y * 0.8) for x, y in pts], POOL[0])
        pen.ellipse(-0.18, -0.12, 0.12, 0.05, POOL[2])

    return render_sprite(48, draw, outline=SEDGE[0])


def puddle(variant):
    rng = np.random.default_rng(1400 + variant)

    def draw(pen):
        pen.ellipse(0, 0, 0.45, 0.22 + 0.05 * variant, POOL[1])
        pen.ellipse(rng.uniform(-0.1, 0.1), 0, 0.3, 0.14, POOL[0])

    return render_sprite(40, draw, outline=WOOD[0])


OBJECTS = {
    "spruce": (96, 3, spruce),
    "birch": (80, 3, birch),
    "pine": (96, 3, pine),
    "rock": (32, 4, rock),
    "bush": (40, 4, bush),
    "shadow": (64, 1, shadow),
    "blood": (64, 2, blood),
    "stump": (32, 2, stump),
    "log": (96, 3, log),
    "fern": (32, 3, fern),
    "moss": (32, 3, moss),
    "tuft": (24, 4, tuft),
    "flowers": (24, 3, flowers),
    "sedge": (32, 3, sedge),
    "cotton": (24, 2, cotton),
    "pool": (48, 2, pool),
    "puddle": (40, 2, puddle),
}


def strip(name):
    size, count, make = OBJECTS[name]
    out = Image.new("RGBA", (size * count, size), (0, 0, 0, 0))
    for i in range(count):
        out.paste(make(i), (i * size, 0))
    return out
