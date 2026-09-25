"""Seamless 128x128 terrain textures (4 x 4 m at 32 px/m)."""
import numpy as np
from PIL import Image

from .raster import dither_ramp, periodic_noise

SIZE = 128

GRASS = [(54, 78, 36), (70, 98, 44), (88, 118, 52), (108, 138, 62), (132, 158, 78)]
FOREST = [(44, 52, 30), (60, 64, 36), (78, 74, 44), (98, 88, 54)]
SWAMP = [(58, 70, 56), (74, 86, 66), (92, 102, 76)]
WATER = [(46, 58, 58), (64, 80, 80)]
LAKE = [(38, 54, 70), (48, 66, 84), (58, 78, 96)]
ROAD = [(92, 72, 50), (116, 92, 64), (140, 114, 80), (164, 138, 100)]


def _speckle(arr, rng, count, colours, length=1, vertical=True):
    """Short strokes that wrap around the edges, so the texture stays seamless."""
    for _ in range(count):
        x, y = rng.integers(0, SIZE, size=2)
        colour = colours[rng.integers(0, len(colours))]
        for i in range(length):
            arr[(y + (i if vertical else 0)) % SIZE, (x + (0 if vertical else i)) % SIZE] = colour


def grass(seed):
    rng = np.random.default_rng(seed)
    v = 0.6 * periodic_noise(SIZE, 4, seed) + 0.4 * periodic_noise(SIZE, 16, seed + 1)
    arr = dither_ramp(v, GRASS)
    _speckle(arr, rng, 260, [GRASS[0], GRASS[1]], length=2)
    _speckle(arr, rng, 120, [GRASS[4]], length=2)
    _speckle(arr, rng, 10, [(220, 200, 90), (230, 228, 210)])
    return Image.fromarray(arr, "RGB")


def forest(seed):
    rng = np.random.default_rng(seed)
    v = 0.5 * periodic_noise(SIZE, 4, seed) + 0.5 * periodic_noise(SIZE, 32, seed + 1)
    arr = dither_ramp(v, FOREST)
    _speckle(arr, rng, 360, [(38, 36, 30), FOREST[0]])
    _speckle(arr, rng, 40, [(96, 62, 36), (120, 96, 60)], length=3, vertical=False)
    _speckle(arr, rng, 60, [(64, 98, 52)], length=2)
    return Image.fromarray(arr, "RGB")


def swamp(seed):
    rng = np.random.default_rng(seed)
    base = periodic_noise(SIZE, 4, seed)
    detail = periodic_noise(SIZE, 16, seed + 1)
    arr = dither_ramp(0.5 * base + 0.5 * detail, SWAMP)
    pools = periodic_noise(SIZE, 4, seed + 2) < 0.35
    water = dither_ramp(detail, WATER)
    arr[pools] = water[pools]
    _speckle(arr, rng, 110, [(108, 138, 62), (92, 102, 76)], length=3)
    return Image.fromarray(arr, "RGB")


def road(seed):
    rng = np.random.default_rng(seed)
    v = 0.55 * periodic_noise(SIZE, 8, seed) + 0.45 * periodic_noise(SIZE, 32, seed + 1)
    arr = dither_ramp(v, ROAD)
    _speckle(arr, rng, 150, [(108, 108, 102), (134, 132, 124), (84, 84, 80)])
    return Image.fromarray(arr, "RGB")


def water(seed):
    """Lake water: dark, cold blue-grey with soft ripples and a few glints."""
    rng = np.random.default_rng(seed)
    v = 0.6 * periodic_noise(SIZE, 4, seed) + 0.4 * periodic_noise(SIZE, 16, seed + 1)
    arr = dither_ramp(v, LAKE)
    _speckle(arr, rng, 70, [(96, 122, 138), (84, 108, 126)], length=4, vertical=False)
    return Image.fromarray(arr, "RGB")


TEXTURES = {"grass": grass, "forest": forest, "swamp": swamp, "road": road, "water": water}
