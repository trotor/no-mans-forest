"""The shared 90s-style palette. Every sprite pixel is snapped to one of these colours."""
import numpy as np
from PIL import Image

OUTLINE = (22, 20, 18)

PALETTE = [
    # outlines and dark shades
    OUTLINE, (38, 36, 30), (56, 52, 44),
    # grass
    (54, 78, 36), (70, 98, 44), (88, 118, 52), (108, 138, 62), (132, 158, 78),
    # forest floor
    (44, 52, 30), (60, 64, 36), (78, 74, 44), (98, 88, 54),
    # conifer greens
    (22, 40, 26), (32, 56, 34), (46, 76, 42), (64, 98, 52),
    # birch greens
    (78, 112, 48), (104, 140, 60), (136, 168, 80),
    # swamp and water
    (58, 70, 56), (74, 86, 66), (92, 102, 76), (46, 58, 58), (64, 80, 80),
    # earth and road
    (92, 72, 50), (116, 92, 64), (140, 114, 80), (164, 138, 100),
    # rock greys
    (84, 84, 80), (108, 108, 102), (134, 132, 124), (162, 160, 150),
    # Finnish M36 grey-green
    (72, 78, 62), (94, 100, 80), (118, 124, 100), (146, 150, 124),
    # Soviet khaki
    (104, 90, 56), (132, 116, 74), (160, 142, 96), (186, 168, 120),
    # Soviet helmet green
    (60, 76, 46), (82, 100, 60),
    # skin
    (150, 108, 80), (192, 146, 110), (222, 182, 146),
    # wood, gun metal, leather
    (96, 62, 36), (58, 58, 62), (120, 96, 60),
    # accents
    (220, 200, 90), (230, 228, 210), (160, 40, 30),
]

_ARRAY = np.array(PALETTE, dtype=np.int32)


def nearest_indices(rgb):
    diff = rgb[..., None, :].astype(np.int32) - _ARRAY
    return np.argmin((diff * diff).sum(axis=-1), axis=-1)


def quantize(image):
    """Snaps colours to the palette; alpha becomes 0 or 255 (threshold 128)."""
    arr = np.array(image.convert("RGBA"))
    rgb = _ARRAY[nearest_indices(arr[..., :3])].astype(np.uint8)
    alpha = np.where(arr[..., 3] >= 128, 255, 0).astype(np.uint8)
    rgb[alpha == 0] = 0
    return Image.fromarray(np.dstack([rgb, alpha]), "RGBA")
