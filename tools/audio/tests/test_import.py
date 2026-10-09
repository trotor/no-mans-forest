import pathlib
import tempfile
import unittest

import numpy as np
import soundfile as sf

from tools.audio.import_sounds import cue_of, import_folder, normalise, to_mono, trim

RATE = 44100


def tone(seconds, rate=RATE, amp=0.3):
    t = np.arange(int(seconds * rate)) / rate
    return amp * np.sin(2 * np.pi * 440 * t)


def padded(seconds=1.0, pad=0.3):
    silence = np.zeros(int(pad * RATE))
    return np.concatenate([silence, tone(seconds), silence])


SOUNDS_YAML = """# test catalogue
cues:
  shot.rifle: { bus: sfx, files: [], priority: 70, loud: true }
  ui.click: { bus: ui, files: [] }

surfaces: { forest: forest }
"""


class HelperTests(unittest.TestCase):
    def test_cue_of_reads_the_cue_and_version(self):
        self.assertEqual(cue_of("shot.mosin_m39.2.mp3"), ("shot.mosin_m39", 2))
        self.assertEqual(cue_of("ui.click.1.wav"), ("ui.click", 1))

    def test_cue_of_refuses_a_name_without_version(self):
        with self.assertRaises(ValueError):
            cue_of("shot.rifle.wav")

    def test_trim_cuts_the_silence(self):
        out = trim(padded()[:, None], RATE)
        self.assertAlmostEqual(len(out) / RATE, 1.0, delta=0.02)

    def test_stereo_becomes_mono(self):
        stereo = np.stack([tone(0.1), tone(0.1) * 0.5], axis=1)
        mono = to_mono(stereo)
        self.assertEqual(mono.shape[1], 1)
        self.assertAlmostEqual(float(np.max(np.abs(mono))), 0.3 * 0.75, delta=1e-3)

    def test_normalise_sets_the_peak(self):
        out = normalise(tone(0.1)[:, None])
        self.assertAlmostEqual(float(np.max(np.abs(out))), 10 ** (-1 / 20), delta=1e-3)


class ImportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = pathlib.Path(self.tmp.name)
        self.raw = root / "raw"
        self.raw.mkdir()
        self.audio = root / "audio"
        self.audio.mkdir()
        (self.audio / "sounds.yaml").write_text(SOUNDS_YAML)
        (self.audio / "SOURCES.md").write_text("| File | Service | Date | Prompt | Licence |\n|---|---|---|---|---|\n")

    def tearDown(self):
        self.tmp.cleanup()

    def test_import_writes_ogg_lists_it_and_records_the_source(self):
        sf.write(self.raw / "shot.rifle.1.wav", np.stack([padded(0.5)] * 2, axis=1), RATE)
        written = import_folder(self.raw, self.audio, "TestFX Pro", "CC BY-SA 4.0", "2026-10-09")
        out = self.audio / "sfx" / "shot.rifle.1.ogg"
        self.assertEqual(written, [out])
        data, rate = sf.read(out, always_2d=True)
        self.assertEqual(data.shape[1], 1)
        self.assertAlmostEqual(len(data) / rate, 0.5, delta=0.03)
        yaml_text = (self.audio / "sounds.yaml").read_text()
        self.assertIn("shot.rifle: { bus: sfx, files: [sfx/shot.rifle.1.ogg], priority: 70, loud: true }", yaml_text)
        self.assertIn("# test catalogue", yaml_text)
        self.assertIn("| sfx/shot.rifle.1.ogg | TestFX Pro | 2026-10-09 |", (self.audio / "SOURCES.md").read_text())

    def test_importing_again_replaces_and_does_not_list_twice(self):
        sf.write(self.raw / "shot.rifle.1.wav", tone(0.2), RATE)
        import_folder(self.raw, self.audio, "A", "CC0", "2026-10-09")
        import_folder(self.raw, self.audio, "A", "CC0", "2026-10-10")
        yaml_text = (self.audio / "sounds.yaml").read_text()
        self.assertEqual(yaml_text.count("sfx/shot.rifle.1.ogg"), 1)
        sources = (self.audio / "SOURCES.md").read_text()
        self.assertEqual(sources.count("sfx/shot.rifle.1.ogg"), 1)
        self.assertIn("2026-10-10", sources)

    def test_second_version_is_added_to_the_list(self):
        sf.write(self.raw / "shot.rifle.1.wav", tone(0.2), RATE)
        sf.write(self.raw / "shot.rifle.2.wav", tone(0.2), RATE)
        import_folder(self.raw, self.audio, "A", "CC0", "2026-10-09")
        self.assertIn("files: [sfx/shot.rifle.1.ogg, sfx/shot.rifle.2.ogg]", (self.audio / "sounds.yaml").read_text())

    def test_unknown_cue_is_refused(self):
        sf.write(self.raw / "nope.1.wav", tone(0.2), RATE)
        with self.assertRaisesRegex(ValueError, "nope.1.wav"):
            import_folder(self.raw, self.audio, "A", "CC0", "2026-10-09")


if __name__ == "__main__":
    unittest.main()
