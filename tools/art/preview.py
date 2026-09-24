"""Writes an upscaled contact sheet of the generated art for eyeballing:

    python3 -m tools.art.preview /tmp/nmf-art-preview.png
"""
import sys

from PIL import Image

from . import objects, portraits, soldiers, terrain

SCALE = 3


def main(out_path):
    rows = []
    for faction in soldiers.FACTIONS:
        rows.append([soldiers.frame(faction, "idle", d, 0) for d in range(8)])
        rows.append([soldiers.frame(faction, "walk", 2, f) for f in range(6)]
                    + [soldiers.frame(faction, "crouch", 4, 0), soldiers.frame(faction, "prone", 3, 0)])
        rows.append([portraits.portrait(faction, i) for i in range(8)])
    rows.append([objects.OBJECTS[n][2](0) for n in ("spruce", "birch", "rock", "bush")])
    rows.append([make(10).convert("RGBA") for make in terrain.TEXTURES.values()])
    width = max(sum(im.width for im in row) + 8 * len(row) for row in rows)
    height = sum(max(im.height for im in row) + 8 for row in rows)
    sheet = Image.new("RGBA", (width, height), (70, 98, 44, 255))
    y = 0
    for row in rows:
        x = 0
        for im in row:
            sheet.alpha_composite(im, (x, y))
            x += im.width + 8
        y += max(im.height for im in row) + 8
    sheet.resize((width * SCALE, height * SCALE), Image.NEAREST).save(out_path)
    print(f"wrote {out_path}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "/tmp/nmf-art-preview.png")
