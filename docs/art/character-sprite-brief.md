# Art brief: WW2 soldier sprites for "No Man's Forest"

You are drawing pixel-art sprites for a 2D top-down tactics game in the spirit of *Close Combat* and *Jagged Alliance 2*. Your images will be placed directly into the game, so please follow the technical rules exactly. When a rule and "looking nicer" conflict, the rule wins.

## 1. The game and the look

- **Setting:** Finnish Continuation War, summer 1942. Birch and spruce forest, swamps, dirt roads.
- **Camera:** straight down from above (true top-down, *not* isometric and *not* three-quarter view). You see the top of the helmet, the shoulders, arms, the weapon and, while walking, the legs and boots below the body.
- **Style:** late-1990s hand-pixelled game art (JA2, Close Combat 3, Commandos). The requirements:
  - crisp pixels with no anti-aliased blur
  - a limited earthy palette
  - a 1-pixel dark outline around every figure
  - simple two- or three-tone shading
- **Light:** comes from the north-west (top-left of the image). Highlights are on the top-left side of shapes, and shade is on the bottom-right. Keep this the same in every direction. Do **not** rotate the lighting with the soldier.
- **Scale:** 32 pixels = 1 metre. A standing soldier is about 18–20 px across the shoulders and the helmet is about 9–10 px wide. A soldier lying prone is about 55–58 px long.

## 2. The two factions

**Finnish soldier (sheet name `finnish`)**
- Grey-green M36 field uniform (colours around #5E6450, #767C64 and #92967C).
- Grey-green M40 steel helmet: German-style shape, from above a rounded dome with a slightly flared rim.
- Mosin-Nagant M/39 rifle: long, a wooden stock (#603E24) and a dark metal barrel (#3A3A3E).
- Bread bag or small pack on the back (#78603C), and dark leather boots.

**Soviet soldier (sheet name `soviet`)**
- Khaki gymnastyorka tunic and trousers (#84744A, #A08E60, #BAA878).
- Green SSh-40 helmet (#3C4C2E, #52643C): from above a round dome, slightly larger and rounder than the Finnish one.
- Mosin-Nagant 91/30 rifle, a veshchmeshok pack on the back, and dark boots.

Soldiers carry the rifle at the ready, diagonally across the chest (port arms), muzzle forward and a little to the left. Both hands are on the rifle.

## 3. Palette

Use these colours (hex). Small deviations are fine because our import tool snaps every pixel to the nearest palette colour, but staying close keeps your shading intact.

```
#161412 #26241E #38342C  outlines, darkest shades
#484E3E #5E6450 #767C64 #92967C  Finnish uniform (dark to light)
#685A38 #84744A #A08E60 #BAA878  Soviet uniform
#3C4C2E #52643C  Soviet helmet
#966C50 #C0926E #DEB692  skin
#603E24 #3A3A3E #78603C  rifle wood, gun metal, leather
#364E24 #46622C #587634 #6C8A3E #849E4E  greens (for context only)
#A0281E  red star on the Soviet helmet (portraits only)
```

## 4. What to deliver

### 4a. Soldier frames (the important part)

**One PNG file per frame.**
- 64 × 64 pixels (or an exact 2×, 4× or 8× enlargement: 128, 256 or 512 px square, and we downscale). Do **not** deliver 32 × 32; a prone soldier does not fit.
- **Transparent background.** No ground, no shadow, no text, no frame or border.
- **Centred:** the unit's position (roughly between the shoulders when standing, the hips when prone) sits at the exact centre pixel (32, 32).
- **Facing "N" means the soldier looks toward the top of the image.**
- **File name:** `<animation>_<direction>_<frame>.png`, for example `walk_NE_3.png`.

**Directions:** `N NE E SE S SW W NW`, in 45° steps clockwise. **Minimum set:** N, NE, E, SE and S. We create W, SW and NW by mirroring E, SE and NE. Mirroring also flips the light (it would come from the top-right) and puts the rifle in the other hand, so please draw all 8 directions when you can.

**Animations:**

| Animation | Frames | Pose |
|---|---|---|
| `idle` | 1 (frame 0) | Standing still, rifle at port arms, feet together under the body (hardly visible from above). |
| `walk` | 6 (0–5) | Walking cycle. Legs alternate forward and back (one boot shows in front of the body, the other behind), with a slight body sway. Frame 0 is legs together, frame 1 is the left leg forward, frame 3 is together again and frame 4 is the right leg forward. |
| `run` | 6 (0–5) | Like walk but with a longer stride and the body leaning forward, the rifle held a bit lower. |
| `crouch` | 1 | Kneeling or crouched: the body is lower and more compact, the knees show in front of the body, the rifle is ready. |
| `prone` | 1 | Lying flat on the stomach, the body pointing in the facing direction. The helmet and rifle are at the front, the rifle held forward in both hands, and the legs are straight behind with the boots at the back. It fills most of the 64 px height when facing N. |
| `crawl` | 4 (0–3) | Prone with a crawl cycle. Opposite arm and leg move forward together: frames 0 and 2 are neutral, frame 1 has the left arm and right leg forward, and frame 3 has the right arm and left leg forward. |

**Totals:** 6 animations; frame counts 1 + 6 + 6 + 1 + 1 + 4 = 19 per direction; with the 5 minimum directions that is 95 PNG files per faction.

If you can only produce part of this, that is fine: our import tool fills missing poses from related ones (run from walk, crawl from prone, anything from idle) so soldiers never disappear. The priority is:
1. `idle` in 5 directions
2. `walk`
3. `prone`
4. `crawl`
5. `run`
6. `crouch`

Missing frames are filled from related poses.

### 4b. Portraits

- **Size and style:** 64 × 64 px, JA2-style head-and-shoulders portraits, front view.
- **Content:** helmet on (Finnish M40 or Soviet SSh-40 with a small red star), collar of the uniform visible, and a plain dark background (#38342C).
- **Variety:** 8 different men per faction, with different faces, ages, expressions, stubble or a moustache.
- **Naming:** `portrait_finnish_0.png` … `portrait_finnish_7.png`, and the same for `soviet`. Deliver 64 px or an exact 2×/4× enlargement. (We join them into a 512 × 64 strip with `python3 -m tools.art.assemble_sheet --portraits finnish <folder> content/core/art/portraits/finnish.png`.)

## 5. Quality checklist before delivering

- [ ] True top-down view in every soldier frame. You can see the top of the helmet, not the face.
- [ ] The background is fully transparent and there are no semi-transparent edge pixels (hard edges only). Frames with an opaque background are rejected by the import tool.
- [ ] There is a 1 px dark outline around each figure.
- [ ] The figure is centred and has the same size in every frame and direction; it does not jump around between frames.
- [ ] N faces up, E faces right, S faces down.
- [ ] The light is always from the top-left.
- [ ] The file names follow `<animation>_<direction>_<frame>.png`.

## 6. How we import it (for the developer)

Put the frames of one faction in a folder and run from the repository root:

```bash
python3 -m tools.art.assemble_sheet <folder> content/core/art/soldiers/finnish.png
```

The tool downsamples enlarged frames, mirrors the missing W, SW and NW directions, fills other missing frames from related poses, snaps colours to the palette (use `--keep-colours` to skip this), adds an outline where there is none, rejects frames with an opaque background, and reports what was missing. The game checks that every image is at least as large as its layout needs and stops with a clear message otherwise. Start the game with `tools/run_game.sh` to see the result. The layout contract is in `docs/superpowers/specs/2026-09-24-pixel-art-design.md` §5.

## 7. Suggested prompt (one frame at a time works best)

> Pixel art game sprite, strict top-down view from directly above, a Finnish WW2 soldier (1942) in grey-green M36 uniform and grey-green M40 steel helmet, carrying a Mosin-Nagant rifle diagonally across the chest, small bread bag on the back, walking, left leg forward, facing up (north). 64×64 pixels, 32 pixels per metre, 1-pixel dark outline, limited earthy 1990s palette, light from the top-left, crisp pixels without anti-aliasing, transparent background, figure centred, no ground, no shadow, no text.

Change "facing up (north)" to "facing up-right (north-east)", "facing right (east)", "facing down-right (south-east)" or "facing down (south)", and the pose phrase to the animation and frame you are drawing. For the Soviet soldier, use "Soviet WW2 soldier in khaki gymnastyorka and green SSh-40 helmet".
