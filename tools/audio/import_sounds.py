"""Bring AI-made sound files into the game (spec docs/superpowers/specs/2026-10-09-sound-design.md).

Each file is named <cue>.<version>.<ext> after a cue in content/core/audio/sounds.yaml. It is trimmed of silence, folded
to mono (except music and ambience), normalised to a -1 dBFS peak and written as Ogg Vorbis into <audio>/<bus>/. The cue's
`files` list in sounds.yaml gets the file and SOURCES.md gets a row saying where it came from.
"""
import pathlib
import re

import numpy as np
import soundfile as sf
import yaml

INPUT_SUFFIXES = {".wav", ".mp3", ".ogg", ".flac"}
STEREO_BUSES = {"music", "ambience"}


def cue_of(filename):
    """'shot.mosin_m39.2.mp3' -> ('shot.mosin_m39', 2)."""
    stem = pathlib.Path(filename).stem
    cue, _, version = stem.rpartition(".")
    if not cue or not version.isdigit():
        raise ValueError(f"{filename}: name it <cue>.<version>.<ext>, e.g. shot.mosin_m39.1.mp3")
    return cue, int(version)


def trim(samples, rate, threshold_db=-50):
    """Cut leading and trailing silence (below the threshold, relative to full scale)."""
    level = np.max(np.abs(samples), axis=1)
    loud = np.nonzero(level > 10 ** (threshold_db / 20))[0]
    if len(loud) == 0:
        return samples[:0]
    return samples[loud[0]:loud[-1] + 1]


def to_mono(samples):
    return samples.mean(axis=1, keepdims=True)


def normalise(samples, peak_db=-1):
    peak = np.max(np.abs(samples)) if len(samples) else 0
    return samples if peak == 0 else samples * (10 ** (peak_db / 20) / peak)


def import_folder(raw, audio_root, service, licence, today):
    """Import every sound file in `raw`; returns the written .ogg paths."""
    raw, audio_root = pathlib.Path(raw), pathlib.Path(audio_root)
    catalogue = audio_root / "sounds.yaml"
    cues = (yaml.safe_load(catalogue.read_text()) or {}).get("cues") or {}
    files = sorted(p for p in raw.iterdir() if p.suffix.lower() in INPUT_SUFFIXES)
    plan = []
    for path in files:  # check every name before touching anything
        cue, version = cue_of(path.name)
        if cue not in cues:
            raise ValueError(f"{path.name}: no cue '{cue}' in {catalogue}")
        plan.append((path, cue, version, cues[cue].get("bus", "sfx")))

    written = []
    for path, cue, version, bus in plan:
        samples, rate = sf.read(path, always_2d=True)
        samples = trim(samples, rate)
        if bus not in STEREO_BUSES:
            samples = to_mono(samples)
        samples = normalise(samples)
        relative = f"{bus}/{cue}.{version}.ogg"
        out = audio_root / relative
        out.parent.mkdir(parents=True, exist_ok=True)
        sf.write(out, samples, rate, format="OGG", subtype="VORBIS")
        _list_file(catalogue, cue, relative)
        _record_source(audio_root / "SOURCES.md", relative, service, today, licence)
        written.append(out)
    return written


def _list_file(catalogue, cue, relative):
    """Add the file to the cue's line in sounds.yaml, keeping every other line as it is."""
    pattern = re.compile(rf"^(  {re.escape(cue)}:\s*\{{.*?files:\s*\[)([^\]]*)(\].*)$", re.MULTILINE)
    text = catalogue.read_text()
    match = pattern.search(text)
    if match is None:
        raise ValueError(f"{catalogue}: cue '{cue}' must be on one line with a files: [...] list")
    listed = [f.strip() for f in match.group(2).split(",") if f.strip()]
    if relative not in listed:
        listed.append(relative)
    listed.sort(key=lambda f: (f.rsplit(".", 2)[0], int(f.rsplit(".", 2)[1])))
    line = match.group(1) + ", ".join(listed) + match.group(3)
    catalogue.write_text(text[:match.start()] + line + text[match.end():])


def _record_source(sources, relative, service, today, licence):
    """One row per file: a re-import replaces the old row."""
    lines = sources.read_text().splitlines()
    lines = [line for line in lines if not line.startswith(f"| {relative} |")]
    lines.append(f"| {relative} | {service} | {today} | see docs/audio/sound-brief.md | {licence} |")
    sources.write_text("\n".join(lines) + "\n")
