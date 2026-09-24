"""Builds a game soldier sheet from individual frame images (e.g. made by an external image AI).

Frames are PNG files named <animation>_<direction>_<frame>.png, for example walk_E_3.png, in one folder.
Any square size that is a multiple of 64 works (64, 128, 256 ...); larger frames are downsampled.
Missing W / SW / NW frames are mirrored from E / SE / NE (note: mirroring also mirrors the light and the rifle hand).
Other missing frames fall back to a related pose (run -> walk -> idle, crawl -> prone -> idle, crouch -> idle,
frame n -> frame 0), so no soldier ever becomes invisible. Colours are snapped to the game palette.

    python3 -m tools.art.assemble_sheet <frames folder> content/core/art/soldiers/finnish.png [--keep-colours]
    python3 -m tools.art.assemble_sheet --portraits finnish <folder> content/core/art/portraits/finnish.png
"""
import argparse
import pathlib

from PIL import Image, ImageOps

from .palette import quantize
from .raster import add_outline
from .soldiers import ANIMATIONS, CELL, DIRECTIONS, MAX_FRAMES

MIRRORS = {"W": "E", "SW": "SE", "NW": "NE"}
FALLBACKS = {"run": ["walk", "idle"], "walk": ["idle"], "crouch": ["idle"], "crawl": ["prone", "idle"], "prone": ["idle"], "idle": []}


def load_frame(folder, animation, direction, index, keep_colours=False):
    """Returns the 64x64 RGBA frame, mirroring from the opposite side if needed, or None if absent."""
    path = folder / f"{animation}_{direction}_{index}.png"
    mirrored = False
    if not path.exists() and direction in MIRRORS:
        path = folder / f"{animation}_{MIRRORS[direction]}_{index}.png"
        mirrored = True
    if not path.exists():
        return None
    img = Image.open(path).convert("RGBA")
    _require_transparent_background(img, path.name)
    if img.width != img.height or img.width % CELL:
        raise ValueError(f"{path.name}: frames must be square and a multiple of {CELL} px, got {img.size}")
    if img.width != CELL:
        img = img.resize((CELL, CELL), Image.BOX)
    if mirrored:
        img = ImageOps.mirror(img)
    if keep_colours:
        return img
    # Frames that already have a dark outline keep it; add one only where the art has none.
    snapped = quantize(img)
    return snapped if _has_outline(snapped) else add_outline(snapped)


def _require_transparent_background(img, name):
    alpha = img.getchannel("A")
    w, h = img.size
    corners = [alpha.getpixel(p) for p in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1))]
    if alpha.getextrema()[0] > 0 or all(c > 0 for c in corners):
        raise ValueError(f"{name}: the background must be transparent (found an opaque background)")


def _has_outline(img):
    """True when most edge pixels (opaque pixels touching transparency) are dark, i.e. the art already has an outline."""
    import numpy as np

    arr = np.array(img)
    solid = arr[..., 3] > 0
    padded = np.pad(solid, 1, constant_values=False)
    touches_air = ~padded[:-2, 1:-1] | ~padded[2:, 1:-1] | ~padded[1:-1, :-2] | ~padded[1:-1, 2:]
    edge = solid & touches_air
    if not edge.any():
        return False
    dark = arr[..., :3].astype(int).sum(axis=-1) < 160
    return (dark & edge).sum() / edge.sum() >= 0.6


def assemble(folder, keep_colours=False):
    folder = pathlib.Path(folder)
    sheet = Image.new("RGBA", (CELL * MAX_FRAMES, CELL * 8 * len(ANIMATIONS)), (0, 0, 0, 0))
    missing = []
    loaded = {}
    for name, frames, _ in ANIMATIONS:
        for direction in DIRECTIONS:
            for f in range(frames):
                frame = load_frame(folder, name, direction, f, keep_colours)
                if frame is None:
                    missing.append(f"{name}_{direction}_{f}.png")
                else:
                    loaded[(name, direction, f)] = frame

    def resolve(name, direction, f):
        for candidate in [name] + FALLBACKS[name]:
            for index in (f, 0):
                if (candidate, direction, index) in loaded:
                    return loaded[(candidate, direction, index)]
        return None

    for a, (name, frames, _) in enumerate(ANIMATIONS):
        for d, direction in enumerate(DIRECTIONS):
            for f in range(frames):
                frame = resolve(name, direction, f)
                if frame is not None:
                    sheet.paste(frame, (f * CELL, (a * 8 + d) * CELL))
    return sheet, missing


def assemble_portraits(folder, faction):
    """Joins portrait_<faction>_0..7.png (64 px or an exact multiple) into the game's 512x64 strip."""
    folder = pathlib.Path(folder)
    strip = Image.new("RGBA", (64 * 8, 64), (0, 0, 0, 0))
    for i in range(8):
        path = folder / f"portrait_{faction}_{i}.png"
        img = Image.open(path).convert("RGBA")
        if img.width != img.height or img.width % 64:
            raise ValueError(f"{path.name}: portraits must be square and a multiple of 64 px, got {img.size}")
        strip.paste(quantize(img.resize((64, 64), Image.BOX)), (i * 64, 0))
    return strip


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("frames")
    parser.add_argument("out")
    parser.add_argument("--keep-colours", action="store_true", help="do not snap colours to the game palette")
    parser.add_argument("--portraits", metavar="FACTION", help="join portrait_<FACTION>_0..7.png instead of soldier frames")
    args = parser.parse_args()
    if args.portraits:
        assemble_portraits(args.frames, args.portraits).save(args.out, optimize=True)
        print(f"wrote {args.out}")
        return
    sheet, missing = assemble(args.frames, args.keep_colours)
    sheet.save(args.out, optimize=True)
    print(f"wrote {args.out}")
    if missing:
        print(f"{len(missing)} frames missing (filled from related poses), e.g. {', '.join(missing[:5])}")


if __name__ == "__main__":
    main()
