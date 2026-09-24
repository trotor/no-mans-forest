"""Builds a game soldier sheet from individual frame images (e.g. made by an external image AI).

Frames are PNG files named <animation>_<direction>_<frame>.png, for example walk_E_3.png, in one folder.
Any square size that is a multiple of 64 works (64, 128, 256 ...); larger frames are downsampled.
Missing W / SW / NW frames are mirrored from E / SE / NE. Colours are snapped to the game palette.

    python3 -m tools.art.assemble_sheet <frames folder> content/core/art/soldiers/finnish.png [--keep-colours]
"""
import argparse
import pathlib

from PIL import Image, ImageOps

from .palette import quantize
from .raster import add_outline
from .soldiers import ANIMATIONS, CELL, DIRECTIONS, MAX_FRAMES

MIRRORS = {"W": "E", "SW": "SE", "NW": "NE"}


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


def _has_outline(img):
    px = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a and r + g + b < 120:
                return True
    return False


def assemble(folder, keep_colours=False):
    folder = pathlib.Path(folder)
    sheet = Image.new("RGBA", (CELL * MAX_FRAMES, CELL * 8 * len(ANIMATIONS)), (0, 0, 0, 0))
    missing = []
    for a, (name, frames, _) in enumerate(ANIMATIONS):
        for d, direction in enumerate(DIRECTIONS):
            for f in range(frames):
                frame = load_frame(folder, name, direction, f, keep_colours)
                if frame is None:
                    missing.append(f"{name}_{direction}_{f}.png")
                    continue
                sheet.paste(frame, (f * CELL, (a * 8 + d) * CELL))
    return sheet, missing


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("frames")
    parser.add_argument("out")
    parser.add_argument("--keep-colours", action="store_true", help="do not snap colours to the game palette")
    args = parser.parse_args()
    sheet, missing = assemble(args.frames, args.keep_colours)
    sheet.save(args.out, optimize=True)
    print(f"wrote {args.out}")
    if missing:
        print(f"{len(missing)} frames missing (left empty), e.g. {', '.join(missing[:5])}")


if __name__ == "__main__":
    main()
