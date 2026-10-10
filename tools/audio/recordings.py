"""Cut real gunshot recordings into single shots, derive distant versions, and synthesise the few sounds no recording covers.

    python3 -m tools.audio.recordings <"Prepared SFX Library" folder> <out folder>

The input is The Free Firearm Sound Library (Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney; CC0),
https://opengameart.org/content/the-free-firearm-sound-library: 96 kHz range recordings, single shots taken from the
"gunshot, near distance" files. The game plays one sound per round, so an automatic weapon's rounds make its burst.
The out folder gets <cue>.<n>.wav files (48 kHz) for `python3 -m tools.audio`.
"""
import pathlib
import sys

import numpy as np
import soundfile as sf
from scipy import signal

# Which recording stands in for which weapon: the same weapon, or the same or a close cartridge.
SOURCES = {
    "Mosin Nagant/M_21P.wav": ["shot.mosin_m39", "shot.mosin_9130", "shot.rifle"],
    "PPSh/P_30P.wav": ["shot.ppsh41"],
    "Carl Gustav M45/G_31P.wav": ["shot.suomi_kp31", "shot.smg"],  # 9 mm, as the Suomi
    "AK-47/C_28P.wav": ["shot.lahti_saloranta", "shot.dp27", "shot.lmg"],  # the nearest automatic weapon
}
FAR = {
    "Mosin Nagant/M_21P.wav": "shot_far.rifle",
    "PPSh/P_30P.wav": "shot_far.smg",
    "AK-47/C_28P.wav": "shot_far.lmg",
}
RATE = 48000


def mono(samples):
    return samples.mean(axis=1) if samples.ndim == 2 else samples


def onsets(samples, rate, gap_s=0.4, rel=0.5):
    """Sample indices where a shot starts: the level first crosses `rel` of the loudest peak, at least `gap_s` apart."""
    level = np.abs(samples)
    loud = np.nonzero(level > rel * level.max())[0]
    found = []
    for i in loud:
        if not found or i - found[-1] > gap_s * rate:
            found.append(int(i))
    return found


def slice_shots(samples, rate, max_s=1.2, pre_s=0.005, fade_s=0.1):
    """One clip per shot, up to the next shot or `max_s`, faded out at the end."""
    starts = onsets(samples, rate)
    clips = []
    for k, start in enumerate(starts):
        begin = max(0, start - int(pre_s * rate))
        end = min(len(samples), begin + int(max_s * rate))
        if k + 1 < len(starts):
            end = min(end, starts[k + 1] - int(pre_s * rate))
        clip = samples[begin:end].copy()
        fade = min(len(clip), int(fade_s * rate))
        clip[len(clip) - fade:] *= np.linspace(1, 0, fade)
        clips.append(clip)
    return clips


def forest_reverb(rate, seconds, rng):
    """An impulse response of a forest: diffuse, darkening, decaying noise."""
    n = int(seconds * rate)
    t = np.arange(n) / rate
    ir = rng.standard_normal(n) * np.exp(-t * 5.0 / seconds)
    b, a = signal.butter(2, 2500 / (rate / 2), "low")
    return signal.lfilter(b, a, ir) / np.sqrt(n)


def distant(clip, rate, rng, cutoff_hz=700, tail_s=1.6):
    """The same shot heard a few hundred metres off through trees: no crack, a dull thump and a rolling tail."""
    b, a = signal.butter(4, cutoff_hz / (rate / 2), "low")
    dull = signal.lfilter(b, a, clip)
    wet = signal.fftconvolve(dull, forest_reverb(rate, tail_s, rng))[: len(dull) + int(tail_s * rate)]
    out = np.zeros(len(wet))
    out[: len(dull)] += 0.6 * dull
    out += 2.5 * wet
    return out


def crack(rate, rng):
    """A supersonic bullet passing close: a sharp N-wave and a short bright hiss."""
    n = int(0.15 * rate)
    out = np.zeros(n)
    half = int(0.0004 * rate)
    out[:half] = np.linspace(0, 1, half)
    out[half:3 * half] = np.linspace(1, -1, 2 * half)
    out[3 * half:4 * half] = np.linspace(-1, 0, half)
    t = np.arange(n) / rate
    hiss = rng.standard_normal(n) * np.exp(-t * 60)
    b, a = signal.butter(2, 3000 / (rate / 2), "high")
    out += 0.35 * signal.lfilter(b, a, hiss)
    return out + 0.15 * signal.fftconvolve(out, forest_reverb(rate, 0.3, rng))[:n]


def explosion(rate, rng, far=False):
    """A hand grenade: a hard blast, a low thump, falling earth; far off only the dull roll is left."""
    seconds = 2.2 if far else 1.6
    n = int(seconds * rate)
    t = np.arange(n) / rate
    blast = rng.standard_normal(n) * np.exp(-t * 18)
    thump = np.sin(2 * np.pi * np.cumsum(30 + 60 * np.exp(-t * 12)) / rate) * np.exp(-t * 6)
    debris = np.zeros(n)
    for _ in range(80):
        at = int(rng.uniform(0.05, 0.9) * n)
        length = int(rng.uniform(0.002, 0.01) * rate)
        debris[at:at + length] += rng.standard_normal(min(length, n - at)) * rng.uniform(0.05, 0.25) * np.exp(-t[at] * 3)
    b, a = signal.butter(2, (900 if far else 5000) / (rate / 2), "low")
    out = signal.lfilter(b, a, blast) + 1.2 * thump + (0 if far else 0.6) * debris
    return out + (3.0 if far else 1.2) * signal.fftconvolve(out, forest_reverb(rate, 1.5, rng))[:n]


def whistle(rate, rng, seconds=2.0):
    """A mortar bomb coming down: a thin whistle falling in pitch and swelling, cut off at the burst."""
    n = int(seconds * rate)
    t = np.arange(n) / rate
    freq = 1400 - 700 * (t / seconds) ** 1.5
    tone = np.sin(2 * np.pi * np.cumsum(freq) / rate)
    air = rng.standard_normal(n)
    b, a = signal.butter(2, [600 / (rate / 2), 3000 / (rate / 2)], "band")
    out = (0.8 * tone + 0.25 * signal.lfilter(b, a, air)) * (0.15 + 0.85 * (t / seconds) ** 2)
    fade = int(0.01 * rate)
    out[:fade] *= np.linspace(0, 1, fade)
    return out


def build(library, out, seed=1942):
    library, out = pathlib.Path(library), pathlib.Path(out)
    out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(seed)
    for name, cues in SOURCES.items():
        samples, rate = sf.read(library / name, always_2d=True)
        samples = mono(samples)
        if rate != RATE:
            samples = signal.resample_poly(samples, RATE, rate)
            rate = RATE
        clips = slice_shots(samples, rate, max_s=1.5)
        for cue in cues:
            for k, clip in enumerate(clips[:4], start=1):
                sf.write(out / f"{cue}.{k}.wav", clip, rate)
        if name in FAR:
            for k, clip in enumerate(clips[:3], start=1):
                sf.write(out / f"{FAR[name]}.{k}.wav", distant(clip, rate, rng), rate)
    for k in range(1, 5):
        sf.write(out / f"bullet.crack.{k}.wav", crack(RATE, rng), RATE)
    for k in range(1, 4):
        sf.write(out / f"grenade.explode.{k}.wav", explosion(RATE, rng), RATE)
        sf.write(out / f"grenade.explode_far.{k}.wav", explosion(RATE, rng, far=True), RATE)
    for k in range(1, 3):
        sf.write(out / f"shell.incoming.{k}.wav", whistle(RATE, rng), RATE)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    build(sys.argv[1], sys.argv[2])
