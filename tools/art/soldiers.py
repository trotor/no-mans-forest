"""Top-down WW2 soldiers: a small vector body model rendered to 8-direction animation sheets."""
import math

from PIL import Image

from .raster import darker, lighter, render_sprite

CELL = 64
DIRECTIONS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
ANIMATIONS = [("idle", 1, 0), ("walk", 6, 120), ("run", 6, 180), ("crouch", 1, 0), ("prone", 1, 0), ("crawl", 4, 60)]
MAX_FRAMES = 6

BOOT = (56, 52, 44)
WOOD = (96, 62, 36)
METAL = (58, 58, 62)
LEATHER = (120, 96, 60)

FACTIONS = {
    # uniform ramp dark..light, helmet colour, helmet highlight
    "finnish": {"uniform": [(72, 78, 62), (94, 100, 80), (118, 124, 100), (146, 150, 124)],
                "helmet": (94, 100, 80), "helmet_light": (118, 124, 100)},
    "soviet": {"uniform": [(104, 90, 56), (132, 116, 74), (160, 142, 96), (186, 168, 120)],
               "helmet": (60, 76, 46), "helmet_light": (82, 100, 60)},
}


def _rifle(pen, butt, muzzle):
    """Rifle from butt to muzzle: wooden stock for the first 55 %, metal barrel after it."""
    mx = butt[0] + (muzzle[0] - butt[0]) * 0.55
    my = butt[1] + (muzzle[1] - butt[1]) * 0.55
    pen.line(butt[0], butt[1], mx, my, 0.07, WOOD)
    pen.line(mx, my, muzzle[0], muzzle[1], 0.04, METAL)


def _helmet(pen, colours, x, y):
    """Dark rim, dome and a highlight toward the light."""
    pen.ellipse(x, y, 0.16, 0.16, darker(colours["helmet"], 22))
    pen.ellipse(x, y, 0.125, 0.125, colours["helmet"], highlight=colours["helmet_light"])


def _upright(pen, colours, stride, lean, crouch):
    u = colours["uniform"]
    skin = (192, 146, 110)
    y0 = lean
    # legs first: they show in front of and behind the torso while striding
    if crouch:
        for side in (-1, 1):
            pen.ellipse(side * 0.13, y0 + 0.2, 0.085, 0.14, u[0])
            pen.ellipse(side * 0.13, y0 + 0.33, 0.07, 0.055, BOOT)
    else:
        for side, offset in ((-1, stride), (1, -stride)):
            pen.ellipse(side * 0.11, offset * 0.8, 0.08, 0.15, u[0])
            if abs(offset) > 0.05:
                pen.ellipse(side * 0.11, offset * 0.8 + math.copysign(0.13, offset), 0.07, 0.065, BOOT)
    # pack and rolled blanket on the back
    pen.ellipse(0.0, y0 - 0.15, 0.2, 0.075, darker(u[0], 8))
    pen.ellipse(0.07, y0 - 0.12, 0.1, 0.065, LEATHER)
    # shoulders and upper arms
    pen.ellipse(0.0, y0, 0.27, 0.15, u[1], highlight=u[2])
    for side in (-1, 1):
        pen.ellipse(side * 0.25, y0 + 0.04, 0.075, 0.11, u[0] if side > 0 else u[1])
    # rifle held diagonally across the chest (port arms)
    _rifle(pen, (0.2, y0 - 0.04), (-0.2, y0 + 0.5))
    # forearms and hands on the rifle
    pen.line(0.24, y0 + 0.1, 0.1, y0 + 0.16, 0.08, u[0])
    pen.line(-0.24, y0 + 0.1, -0.1, y0 + 0.3, 0.08, u[1])
    pen.ellipse(0.1, y0 + 0.16, 0.045, 0.045, skin)
    pen.ellipse(-0.1, y0 + 0.3, 0.045, 0.045, skin)
    _helmet(pen, colours, 0.0, y0 + 0.03)


def _prone(pen, colours, phase):
    u = colours["uniform"]
    swing = 0.12 * math.sin(phase)
    for side, s in ((-1, swing), (1, -swing)):
        pen.ellipse(side * (0.11 + abs(s) * 0.4), -0.45 + s, 0.075, 0.33, u[0])
        pen.ellipse(side * (0.11 + abs(s) * 0.4), -0.82 + s, 0.07, 0.07, BOOT)
    pen.ellipse(0.0, 0.18, 0.22, 0.34, u[1], highlight=u[2])
    pen.ellipse(0.1, 0.0, 0.09, 0.07, LEATHER)
    for side, s in ((1, -swing), (-1, swing)):
        pen.line(side * 0.2, 0.36, side * 0.12, 0.66 + s, 0.09, u[1])
    _rifle(pen, (0.08, 0.34), (0.02, 0.98))
    _helmet(pen, colours, 0.0, 0.58)


def frame(faction, animation, direction, index):
    colours = FACTIONS[faction]
    frames = dict((name, n) for name, n, _ in ANIMATIONS)[animation]
    phase = 2 * math.pi * index / frames

    def draw(pen):
        if animation == "idle":
            _upright(pen, colours, 0.0, 0.0, crouch=False)
        elif animation == "walk":
            _upright(pen, colours, 0.3 * math.sin(phase), 0.02, crouch=False)
        elif animation == "run":
            _upright(pen, colours, 0.42 * math.sin(phase), 0.06, crouch=False)
        elif animation == "crouch":
            _upright(pen, colours, 0.0, 0.05, crouch=True)
        elif animation == "prone":
            _prone(pen, colours, 0.0)
        else:
            _prone(pen, colours, phase)

    return render_sprite(CELL, draw, angle_deg=45 * direction)


def sheet(faction):
    out = Image.new("RGBA", (CELL * MAX_FRAMES, CELL * 8 * len(ANIMATIONS)), (0, 0, 0, 0))
    for a, (name, frames, _) in enumerate(ANIMATIONS):
        for d in range(8):
            for f in range(frames):
                out.paste(frame(faction, name, d, f), (f * CELL, (a * 8 + d) * CELL))
    return out


def sheet_meta():
    return {
        "cellSize": CELL,
        "pixelsPerMetre": 32,
        "directions": DIRECTIONS,
        "animations": {name: {"row": a * 8, "frames": frames, "strideCm": stride}
                       for a, (name, frames, stride) in enumerate(ANIMATIONS)},
    }
