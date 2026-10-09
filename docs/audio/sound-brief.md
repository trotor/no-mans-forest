# Sound brief: effects for "No Man's Forest"

You are making sound effects for a 2D top-down tactics game set in the Finnish Continuation War. The sounds go straight
into the game through an import tool, so please follow the technical rules. When a rule and "sounding bigger" conflict,
the rule wins.

## 1. The game and the sound

- **Setting:** summer 1942 on the shore of Lake Onega, Russian Karelia. Pine and spruce forest, bogs, dirt roads, a
  railway. Small fights between a Finnish strike detachment (7 men) and a Soviet reconnaissance party (9 men).
- **Era:** only 1940s infantry weapons and kit. No modern rifles, no suppressors, no electronic sounds, no sci-fi.
- **Place:** outdoors in a forest. Dry, close sound with a short natural tail. No hall or cave reverb, no slap-back from
  buildings. Distant sounds roll through the trees.
- **Content:** effects only. No music, no voices, no shouting, no breathing in any effect. (Voices come later, in their
  own brief.)
- **Style:** realistic, not cinematic. A rifle shot is a sharp report and a short forest echo, not a thunderclap with a
  sub-bass boom.

## 2. Technical rules

- **Format:** wav or mp3, 44.1 or 48 kHz. Mono is preferred for effects; stereo is fine, the tool folds it to mono.
- **One sound per file.** Start the sound at the start of the file. Leading and trailing silence is trimmed by the tool
  anyway, but don't pad the files with seconds of room tone.
- **Loudness:** don't worry about levels. The tool normalises every file. Avoid clipping.
- **File names:** `<cue>.<version>.<ext>`, using the cue names in the table below, for example `shot.mosin_m39.1.mp3`,
  `shot.mosin_m39.2.mp3`, `footstep.forest.3.wav`.
- **Versions:** each version is a different take (a different generation), not the same file exported twice. The game
  picks a version at random and never plays the same one twice in a row, so variety matters more than perfection.

## 3. Licence

The game is open source and its sounds are shared under CC BY-SA 4.0. Before files go into the public repository:

- Use a plan whose terms let you own or freely redistribute the output (usually a paid plan), or check the service's
  terms and make sure they allow redistribution under CC BY-SA.
- Note the service, the plan and the date. The import tool asks for them and writes them into
  `content/core/audio/SOURCES.md` next to each file.

## 4. Round 1 (about 20 files)

These make the game come alive. One version each is enough to start:

- `shot.suomi_kp31`, `shot.mosin_m39`, `shot.lahti_saloranta`, `shot.ppsh41`, `shot.mosin_9130`, `shot.dp27`
- `shot_far.rifle`, `shot_far.smg`, `shot_far.lmg`
- `grenade.explode`, `grenade.explode_far`, `grenade.throw`
- `bullet.crack` (two versions)
- `impact.wood`, `impact.ground`
- `body.fall`
- `ui.click`, `ui.paper`

## 5. Round 2

The remaining versions up to the counts in the table, plus the reloads, footsteps, crawling, melee, looting, and the
water and body impacts.

## 6. The cues

Lengths are rough targets. Prompts are written for text-to-sound tools; adjust them freely if a tool needs a different
style, but keep the facts.

| Cue | Versions | Length | Prompt |
|---|---|---|---|
| `shot.suomi_kp31` | 3 | 0.3 s | Single gunshot from a Suomi KP/-31 9mm submachine gun, WW2, sharp pistol-calibre crack, close range, outdoors in a pine forest, short natural echo, no music |
| `shot.mosin_m39` | 3 | 0.8 s | Single gunshot from a Finnish M/39 Mosin-Nagant bolt-action rifle, 7.62x53R, WW2, heavy rifle report, close range, outdoors in a pine forest, short echo through the trees |
| `shot.lahti_saloranta` | 3 | 0.4 s | One round of a Lahti-Saloranta M/26 light machine gun, WW2, full-power rifle cartridge, close range, outdoors in a forest, single shot only |
| `shot.ppsh41` | 3 | 0.3 s | Single gunshot from a PPSh-41 submachine gun, 7.62x25mm, WW2, high-pitched snappy report, close range, outdoors in a forest |
| `shot.mosin_9130` | 3 | 0.8 s | Single gunshot from a Mosin-Nagant 91/30 bolt-action rifle, WW2, heavy rifle report, close range, outdoors in a pine forest, short echo |
| `shot.dp27` | 3 | 0.4 s | One round of a DP-27 light machine gun, WW2, rifle cartridge, close range, outdoors in a forest, single shot only |
| `shot_far.rifle` | 3 | 1.2 s | Distant rifle shot about 300 metres away through dense forest, WW2, dull thump with a long rolling echo, no crack |
| `shot_far.smg` | 3 | 1.0 s | Distant submachine gun shot about 300 metres away through forest, muffled pop with a rolling echo |
| `shot_far.lmg` | 3 | 1.2 s | Distant light machine gun shot about 300 metres away through forest, dull thump with a rolling echo |
| `bullet.crack` | 4 | 0.2 s | Supersonic rifle bullet passing just overhead, sharp crack and snap, no gunshot, outdoors |
| `impact.ground` | 3 | 0.3 s | Bullet striking forest soil and moss, short thud with a spray of dirt |
| `impact.wood` | 3 | 0.3 s | Bullet striking a pine tree trunk, sharp wooden thock with splinters |
| `impact.water` | 3 | 0.3 s | Bullet striking lake water, short plunk and splash |
| `impact.body` | 3 | 0.3 s | Bullet hitting a soldier wearing a wool uniform and gear, dull muffled thud, no gore, no voice |
| `reload.rifle` | 2 | 1.8 s | WW2 bolt-action rifle reload: bolt opened, a five-round stripper clip pushed in, bolt closed, outdoors |
| `reload.smg` | 2 | 1.5 s | WW2 submachine gun reload: empty drum magazine pulled out, new one clicked in, bolt cocked |
| `reload.lmg` | 2 | 2.0 s | WW2 light machine gun reload: magazine released, new magazine seated, bolt pulled back |
| `grenade.throw` | 2 | 0.5 s | WW2 stick grenade thrown by hand, fuse cord pull and a cloth swish, no voice |
| `grenade.explode` | 3 | 1.5 s | WW2 hand grenade exploding about 20 metres away in a forest, sharp blast, falling debris and dirt |
| `grenade.explode_far` | 3 | 2.0 s | WW2 hand grenade exploding about 300 metres away beyond a forest, muffled boom with a rolling echo |
| `body.fall` | 3 | 0.6 s | Soldier with rifle and gear collapsing onto a forest floor of moss and twigs, no voice |
| `melee.scuffle` | 2 | 1.5 s | Two soldiers wrestling on a forest floor, rifle stocks knocking, boots scraping, cloth tearing, no voices |
| `loot.rustle` | 2 | 1.0 s | Searching a fallen soldier's pockets and ammunition pouches, cloth, leather and a metal clink |
| `footstep.forest` | 4 | 0.3 s | One leather boot step on dry pine needles and twigs |
| `footstep.grass` | 4 | 0.3 s | One leather boot step in tall meadow grass |
| `footstep.road` | 4 | 0.3 s | One leather boot step on a dry dirt road with gravel |
| `footstep.swamp` | 4 | 0.3 s | One leather boot step in wet bog moss, squelch |
| `footstep.water` | 4 | 0.3 s | One leather boot step in shallow water, splash |
| `crawl.rustle` | 4 | 0.8 s | A soldier crawling through low undergrowth, cloth and gear dragging over moss and twigs |
| `ui.click` | 2 | 0.1 s | Soft pencil tick on paper |
| `ui.paper` | 2 | 0.4 s | A sheet of paper unfolded or folded |

## 7. Importing

Put the finished files in one folder and run, from the repository root:

```
pip install soundfile pyyaml
python3 -m tools.audio <folder> --service "<service and plan>" --licence "CC BY-SA 4.0"
```

The tool trims silence, folds effects to mono, normalises the peak to -1 dBFS, writes Ogg Vorbis files into
`content/core/audio/<bus>/`, lists them in `content/core/audio/sounds.yaml` and adds a row per file to
`content/core/audio/SOURCES.md`. Start the game and the sounds play. Importing a file with the same name again
replaces it.
