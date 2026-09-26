"""The boot splash (1280 x 720) and the window icon (256 x 256): spruce silhouettes against a dawn sky, the title."""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

GAME = Path(__file__).resolve().parents[2] / "src" / "Nmf.Game"
FONTS = ["/System/Library/Fonts/Supplemental/Georgia Bold.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
         "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"]


def font(size):
    for path in FONTS:
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def spruce(draw, x, base, height, colour):
    """A spruce seen from the side: a narrow stack of drooping tiers."""
    tiers = max(6, int(height / 14))
    for i in range(tiers):
        t = i / tiers
        y = base - height * t
        half = (1 - t) * height * 0.23 + 2
        draw.polygon([(x - half, y), (x + half, y), (x, y - height / tiers * 1.8)], fill=colour)
    draw.rectangle([x - 2, base - 4, x + 2, base + 6], fill=colour)


def sky(w, h):
    y = np.linspace(0, 1, h)[:, None]
    top, mid, low = np.array([24, 30, 38]), np.array([86, 78, 70]), np.array([196, 150, 96])
    colour = np.where(y < 0.55, top + (mid - top) * (y / 0.55), mid + (low - mid) * ((y - 0.55) / 0.45))
    return Image.fromarray(np.repeat(colour[:, None, :], w, axis=1).astype(np.uint8), "RGB")


def forest(image, rng, rows):
    draw = ImageDraw.Draw(image)
    w, h = image.size
    for base, (lo, hi), colour, gap in rows:
        x = -20.0
        while x < w + 20:
            spruce(draw, x, h * base + rng.uniform(-6, 6), rng.uniform(lo, hi), colour)
            x += rng.uniform(*gap)


def splash():
    w, h = 1280, 720
    rng = np.random.default_rng(1942)
    image = sky(w, h)
    # Mist over the lake shore, then three rows of spruce, farther ones paler.
    mist = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mist).rectangle([0, int(h * 0.62), w, int(h * 0.74)], fill=90)
    image = Image.composite(Image.new("RGB", (w, h), (170, 150, 130)), image, mist.filter(ImageFilter.GaussianBlur(30)))
    forest(image, rng, [(0.70, (60, 110), (70, 72, 70), (14, 30)),
                        (0.82, (110, 190), (40, 46, 44), (22, 44)),
                        (1.02, (220, 360), (14, 18, 16), (40, 80))])
    draw = ImageDraw.Draw(image)
    title = "NO MAN'S FOREST"
    big = font(92)
    tw = draw.textlength(title, font=big)
    draw.text(((w - tw) / 2 + 3, h * 0.18 + 3), title, font=big, fill=(10, 10, 8))
    draw.text(((w - tw) / 2, h * 0.18), title, font=big, fill=(232, 222, 196))
    small = font(30)
    sub = "Jatkosota 1942  ·  Continuation War 1942"
    sw = draw.textlength(sub, font=small)
    draw.text(((w - sw) / 2, h * 0.18 + 112), sub, font=small, fill=(214, 196, 160))
    return image


def icon():
    size = 256
    image = sky(size, size)
    draw = ImageDraw.Draw(image)
    spruce(draw, size * 0.30, size * 0.98, size * 0.62, (30, 38, 32))
    spruce(draw, size * 0.72, size * 0.98, size * 0.52, (40, 48, 42))
    spruce(draw, size * 0.52, size * 1.02, size * 0.9, (12, 16, 14))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=40, fill=255)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(image, (0, 0), mask)
    return out


def main():
    splash().save(GAME / "splash.png", optimize=True)
    icon().save(GAME / "icon.png", optimize=True)
    print(f"wrote {GAME / 'splash.png'} and {GAME / 'icon.png'}")


if __name__ == "__main__":
    sys.exit(main())
