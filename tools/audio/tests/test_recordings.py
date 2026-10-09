import unittest

import numpy as np

from tools.audio.recordings import crack, distant, explosion, onsets, slice_shots

RATE = 48000


def shots(times, length=3.0):
    """Silence with a decaying noise burst at each time."""
    rng = np.random.default_rng(0)
    out = np.zeros(int(length * RATE))
    for t in times:
        i = int(t * RATE)
        n = int(0.2 * RATE)
        out[i:i + n] += rng.standard_normal(n) * np.exp(-np.arange(n) / RATE * 30) * 0.5
    return out


class RecordingTests(unittest.TestCase):
    def test_onsets_find_each_shot_once(self):
        found = onsets(shots([0.2, 1.0, 2.1]), RATE)
        self.assertEqual(len(found), 3)
        for f, t in zip(found, [0.2, 1.0, 2.1]):
            self.assertAlmostEqual(f / RATE, t, delta=0.01)

    def test_slices_stop_before_the_next_shot_and_fade_out(self):
        clips = slice_shots(shots([0.2, 0.9]), RATE)
        self.assertEqual(len(clips), 2)
        self.assertLess(len(clips[0]) / RATE, 0.7 + 0.01)
        self.assertAlmostEqual(float(clips[0][-1]), 0.0, delta=1e-6)

    def test_distant_is_duller_and_longer(self):
        rng = np.random.default_rng(1)
        near = shots([0.0], 0.5)
        far = distant(near, RATE, rng)
        self.assertGreater(len(far), len(near) + RATE)
        spectrum_near = np.abs(np.fft.rfft(near))
        spectrum_far = np.abs(np.fft.rfft(far[: len(near)]))
        bright = slice(len(spectrum_near) // 4, None)  # above 6 kHz
        self.assertLess(spectrum_far[bright].sum() / spectrum_far.sum(), 0.2 * spectrum_near[bright].sum() / spectrum_near.sum())

    def test_synthesised_sounds_are_audible_and_finite(self):
        rng = np.random.default_rng(2)
        for sound in (crack(RATE, rng), explosion(RATE, rng), explosion(RATE, rng, far=True)):
            self.assertTrue(np.all(np.isfinite(sound)))
            self.assertGreater(np.max(np.abs(sound)), 0.01)


if __name__ == "__main__":
    unittest.main()
