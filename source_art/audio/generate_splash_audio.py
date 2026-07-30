"""Generate the two deterministic Solarix splash sound-design assets."""

from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np


SAMPLE_RATE = 48_000
RNG = np.random.default_rng(0x50_4F_4C_41_52_49_58)
PROJECT_ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = PROJECT_ROOT / "assets" / "audio" / "branding"


def envelope(length: int, attack: float, release: float) -> np.ndarray:
    result = np.ones(length)
    attack_samples = min(length, int(attack * SAMPLE_RATE))
    release_samples = min(length, int(release * SAMPLE_RATE))
    if attack_samples:
        result[:attack_samples] = np.sin(
            np.linspace(0, math.pi / 2, attack_samples)
        ) ** 2
    if release_samples:
        result[-release_samples:] = np.cos(
            np.linspace(0, math.pi / 2, release_samples)
        ) ** 2
    return result


def add_tone(
    mix: np.ndarray,
    start: float,
    duration: float,
    frequency: float,
    level: float,
    decay: float = 2.0,
    pan: float = 0.0,
    partials: tuple[tuple[float, float], ...] = (),
) -> None:
    offset = int(start * SAMPLE_RATE)
    count = min(int(duration * SAMPLE_RATE), len(mix) - offset)
    if count <= 0:
        return
    time = np.arange(count) / SAMPLE_RATE
    voice = np.sin(2 * math.pi * frequency * time)
    for ratio, gain in partials:
        voice += gain * np.sin(2 * math.pi * frequency * ratio * time)
    voice *= np.exp(-decay * time) * envelope(count, 0.012, min(0.5, duration / 2))
    left = math.sqrt((1 - pan) / 2)
    right = math.sqrt((1 + pan) / 2)
    mix[offset : offset + count, 0] += voice * level * left
    mix[offset : offset + count, 1] += voice * level * right


def add_sub_impact(mix: np.ndarray, start: float, level: float) -> None:
    offset = int(start * SAMPLE_RATE)
    count = min(int(1.45 * SAMPLE_RATE), len(mix) - offset)
    time = np.arange(count) / SAMPLE_RATE
    phase = 2 * math.pi * (59 * time - 18 * time**2)
    body = np.sin(phase) * np.exp(-3.4 * time)
    transient = RNG.normal(0, 1, count)
    transient = np.convolve(transient, np.ones(38) / 38, mode="same")
    transient *= np.exp(-28 * time)
    signal = (body + transient * 0.8) * envelope(count, 0.004, 0.35) * level
    mix[offset : offset + count] += signal[:, None]


def add_riser(mix: np.ndarray, duration: float, level: float) -> None:
    count = min(int(duration * SAMPLE_RATE), len(mix))
    time = np.arange(count) / SAMPLE_RATE
    noise = RNG.normal(0, 1, count)
    smooth = np.convolve(noise, np.ones(90) / 90, mode="same")
    air = noise - np.convolve(noise, np.ones(12) / 12, mode="same")
    sweep_phase = 2 * math.pi * (78 * time + 150 * time**2)
    rise = (smooth * 1.6 + air * 0.11 + np.sin(sweep_phase) * 0.14)
    rise *= (time / max(duration, 0.001)) ** 2.2
    rise *= envelope(count, 0.08, 0.035) * level
    mix[:count, 0] += rise
    mix[:count, 1] += np.roll(rise, 73) * 0.93


def add_reverb(mix: np.ndarray, delays: tuple[tuple[float, float, float], ...]) -> None:
    dry = mix.copy()
    for seconds, gain, crossfeed in delays:
        samples = int(seconds * SAMPLE_RATE)
        if samples >= len(mix):
            continue
        mix[samples:, 0] += dry[:-samples, 0] * gain
        mix[samples:, 1] += dry[:-samples, 1] * gain
        mix[samples:, 0] += dry[:-samples, 1] * gain * crossfeed
        mix[samples:, 1] += dry[:-samples, 0] * gain * crossfeed


def finish(mix: np.ndarray, fade_in: float, fade_out: float) -> np.ndarray:
    mix *= envelope(len(mix), fade_in, fade_out)[:, None]
    peak = float(np.max(np.abs(mix)))
    if peak:
        mix *= 0.72 / peak
    return np.tanh(mix * 1.08) / np.tanh(1.08)


def create_mw_ident() -> np.ndarray:
    mix = np.zeros((int(3.65 * SAMPLE_RATE), 2), dtype=np.float64)
    add_riser(mix, 0.76, 0.045)

    # A gentle double pulse beneath the logo—felt more than heard.
    for start, level in ((0.20, 0.11), (0.48, 0.085)):
        add_tone(mix, start, 0.32, 74, level, decay=11.0)

    bell_partials = ((2.01, 0.27), (3.98, 0.11), (6.12, 0.045))
    add_tone(mix, 0.31, 2.45, 440.0, 0.22, decay=1.65, pan=-0.20,
             partials=bell_partials)
    add_tone(mix, 0.62, 2.30, 554.37, 0.18, decay=1.48, pan=0.20,
             partials=bell_partials)
    add_tone(mix, 0.91, 2.15, 659.25, 0.14, decay=1.34, pan=0.04,
             partials=bell_partials)

    # A quiet warm foundation keeps the mark human instead of overly digital.
    add_tone(mix, 0.28, 2.9, 110.0, 0.08, decay=0.72, partials=((2, 0.18),))
    add_reverb(mix, ((0.115, 0.20, 0.35), (0.238, 0.13, 0.55), (0.421, 0.075, 0.72)))
    return finish(mix, 0.05, 0.68)


def create_solarix_reveal() -> np.ndarray:
    mix = np.zeros((int(2.95 * SAMPLE_RATE), 2), dtype=np.float64)
    add_riser(mix, 0.83, 0.16)
    add_sub_impact(mix, 0.60, 0.46)

    metal = ((1.99, 0.32), (3.07, 0.16), (5.48, 0.075))
    add_tone(mix, 0.63, 2.15, 172.0, 0.22, decay=1.55, pan=-0.10, partials=metal)
    add_tone(mix, 0.69, 1.75, 258.0, 0.12, decay=2.15, pan=0.28,
             partials=((2.72, 0.26), (6.1, 0.06)))

    # Two restrained machine wake-up pulses during the logo hold.
    for start, pan in ((1.38, -0.38), (1.78, 0.38)):
        add_tone(mix, start, 0.42, 92.0, 0.13, decay=7.5, pan=pan,
                 partials=((2, 0.24), (4.01, 0.08)))
        add_tone(mix, start + 0.015, 0.24, 1180.0, 0.035, decay=14.0, pan=pan)

    add_reverb(mix, ((0.074, 0.15, 0.28), (0.196, 0.10, 0.62), (0.377, 0.06, 0.80)))
    return finish(mix, 0.025, 0.46)


def write_wave(path: Path, samples: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = np.round(np.clip(samples, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(SAMPLE_RATE)
        output.writeframes(pcm.tobytes())


if __name__ == "__main__":
    write_wave(OUTPUT_DIR / "mw_collective_ident.wav", create_mw_ident())
    write_wave(OUTPUT_DIR / "solarix_logo_reveal.wav", create_solarix_reveal())
    print(f"Wrote splash audio to {OUTPUT_DIR}")
