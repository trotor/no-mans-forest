"""64x64 JA2-style portrait busts: helmet, face, collar."""
import numpy as np
from PIL import Image, ImageDraw

from .palette import quantize
from .raster import SS, add_outline

SKINS = [[(150, 108, 80), (192, 146, 110)], [(192, 146, 110), (222, 182, 146)]]
HELMETS = {"finnish": [(72, 78, 62), (94, 100, 80), (118, 124, 100)],
           "soviet": [(60, 76, 46), (82, 100, 60), (78, 112, 48)]}
COLLARS = {"finnish": [(72, 78, 62), (94, 100, 80)], "soviet": [(104, 90, 56), (132, 116, 74)]}
BACKGROUND = (56, 52, 44)


def portrait(faction, index):
    rng = np.random.default_rng(1000 * (1 if faction == "finnish" else 2) + index)
    s = SS
    img = Image.new("RGBA", (64 * s, 64 * s), (*BACKGROUND, 255))
    d = ImageDraw.Draw(img)
    skin_dark, skin = SKINS[index % 2]
    helmet = HELMETS[faction]
    collar = COLLARS[faction]
    jaw = rng.integers(-2, 3)
    eye_gap = 7 + rng.integers(-1, 2)
    mouth = 5 + rng.integers(-1, 3)

    def box(x0, y0, x1, y1):
        return [x0 * s, y0 * s, x1 * s, y1 * s]

    # shoulders and collar
    d.polygon([(6 * s, 64 * s), (14 * s, 48 * s), (50 * s, 48 * s), (58 * s, 64 * s)], fill=(*collar[1], 255))
    d.polygon([(24 * s, 48 * s), (32 * s, 56 * s), (40 * s, 48 * s)], fill=(*collar[0], 255))
    # neck and face
    d.rectangle(box(27, 40, 37, 50), fill=(*skin_dark, 255))
    d.ellipse(box(19 - jaw / 2, 18, 45 + jaw / 2, 49), fill=(*skin, 255))
    d.ellipse(box(17, 30, 21, 38), fill=(*skin_dark, 255))
    d.ellipse(box(43, 30, 47, 38), fill=(*skin_dark, 255))
    # shade the side away from the light, then eyes, brows, nose, mouth
    d.chord(box(33, 18, 45 + jaw / 2, 49), 270, 90, fill=(*skin_dark, 255))
    brow = 28 - rng.integers(0, 2)
    brow_thick = 1 + rng.integers(0, 2)
    for side in (-1, 1):
        cx = 32 + side * eye_gap / 2 * 1.2
        d.rectangle(box(cx - 2, 31, cx + 2, 33), fill=(230, 228, 210, 255))
        d.rectangle(box(cx - 1, 31, cx + 1, 33), fill=(38, 36, 30, 255))
        d.rectangle(box(cx - 3, brow, cx + 3, brow + brow_thick), fill=(56, 52, 44, 255))
    d.polygon([(32 * s, 33 * s), (30 * s, 39 * s), (33 * s, 39 * s)], fill=(*skin_dark, 255))
    style = rng.integers(0, 3)
    if style == 0:
        d.line([(32 - mouth / 2) * s, 43 * s, (32 + mouth / 2) * s, 43 * s], fill=(96, 62, 36, 255), width=s)
    elif style == 1:
        d.line([(32 - mouth / 2) * s, 44 * s, 32 * s, 43 * s, (32 + mouth / 2) * s, 44 * s], fill=(96, 62, 36, 255), width=s)
    else:
        d.rectangle(box(32 - mouth / 2, 43, 32 + mouth / 2, 44), fill=(150, 108, 80, 255))
    if rng.random() < 0.3:  # moustache
        d.rectangle(box(28, 40, 36, 41), fill=(56, 52, 44, 255))
    # chin strap
    d.line([19 * s, 30 * s, 24 * s, 46 * s], fill=(*helmet[0], 255), width=s)
    d.line([45 * s, 30 * s, 40 * s, 46 * s], fill=(*helmet[0], 255), width=s)
    if rng.random() < 0.5:  # stubble
        for _ in range(40):
            x, y = rng.uniform(22, 42), rng.uniform(40, 47)
            d.point((x * s, y * s), fill=(*skin_dark, 255))
    # helmet: Finnish M40 is lower with a flared rim, Soviet SSh-40 a rounder dome
    if faction == "finnish":
        d.chord(box(14, 6, 50, 38), 180, 360, fill=(*helmet[1], 255))
        d.rectangle(box(12, 22, 52, 26), fill=(*helmet[0], 255))
    else:
        d.chord(box(15, 4, 49, 40), 180, 360, fill=(*helmet[1], 255))
        d.rectangle(box(15, 21, 49, 25), fill=(*helmet[0], 255))
        d.polygon([(30 * s, 13 * s), (34 * s, 13 * s), (32 * s, 17 * s)], fill=(160, 40, 30, 255))
    d.ellipse(box(20, 8, 28, 12), fill=(*helmet[2], 255))

    small = img.resize((64, 64), Image.BOX)
    return add_outline(quantize(small))


def strip(faction):
    out = Image.new("RGBA", (512, 64), (0, 0, 0, 0))
    for i in range(8):
        out.paste(portrait(faction, i), (i * 64, 0))
    return out
