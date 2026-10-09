"""Cut real gunshot recordings into single shots, derive distant versions, and synthesise the few sounds no recording covers.

    python3 -m tools.audio.recordings <tabasco sounds folder> <out folder>

The input is "Gunshot sounds" by Vincent Sevedge (Tabasco), https://opengameart.org/content/gunshot-sounds
(CC BY 3.0 per the licence file in the archive): range recordings of a Mosin-Nagant, an SKS and a CZ-52 pistol
(7.62x25 Tokarev, the PPSh-41's cartridge). The out folder gets <cue>.<n>.wav files for `python3 -m tools.audio`.
"""
import pathlib
import sys

import numpy as np
import soundfile as sf
from scipy import signal

# Which recording stands in for which weapon: the same or a close cartridge.
SOURCES = {
    "mosin.wav": ["shot.mosin_m39", "shot.mosin_9130", "shot.rifle"],
    "sks.wav": ["shot.lahti_saloranta", "shot.dp27", "shot.lmg"],
    "cz.wav": ["shot.suomi_kp31", "shot.ppsh41", "shot.smg"],
}
FAR = {"mosin.wav": "shot_far.rifle", "sks.wav": "shot_far.lmg", "cz.wav": "shot_far.smg"}
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


def build(tabasco, out, seed=1942):
    tabasco, out = pathlib.Path(tabasco), pathlib.Path(out)
    out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(seed)
    for name, cues in SOURCES.items():
        samples, rate = sf.read(tabasco / name, always_2d=True)
        clips = slice_shots(mono(samples), rate)
        for cue in cues:
            for k, clip in enumerate(clips[:4], start=1):
                sf.write(out / f"{cue}.{k}.wav", clip, rate)
        for k, clip in enumerate(clips[:3], start=1):
            sf.write(out / f"{FAR[name]}.{k}.wav", distant(clip, rate, rng), rate)
    for k in range(1, 5):
        sf.write(out / f"bullet.crack.{k}.wav", crack(RATE, rng), RATE)
    for k in range(1, 4):
        sf.write(out / f"grenade.explode.{k}.wav", explosion(RATE, rng), RATE)
        sf.write(out / f"grenade.explode_far.{k}.wav", explosion(RATE, rng, far=True), RATE)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    build(sys.argv[1], sys.argv[2])
