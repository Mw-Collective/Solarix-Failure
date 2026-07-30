"""Generate deterministic, sample-free Solarix interface sounds."""

from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np


SAMPLE_RATE = 48_000
PROJECT_ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = PROJECT_ROOT / "assets" / "audio" / "ui"


def tone(
    duration: float,
    frequencies: tuple[tuple[float, float], ...],
    attack: float,
    decay: float,
) -> np.ndarray:
    count = int(duration * SAMPLE_RATE)
    time = np.arange(count) / SAMPLE_RATE
    signal = np.zeros(count)
    for frequency, level in frequencies:
        signal += np.sin(2 * math.pi * frequency * time) * level

    attack_samples = max(1, int(attack * SAMPLE_RATE))
    envelope = np.exp(-decay * time)
    envelope[:attack_samples] *= np.sin(
        np.linspace(0, math.pi / 2, attack_samples)
    ) ** 2
    signal *= envelope

    # Tiny stereo offset gives the UI dimension without making it spatially vague.
    stereo = np.column_stack((signal, np.roll(signal, 9) * 0.94))
    peak = float(np.max(np.abs(stereo)))
    if peak:
        stereo *= 0.62 / peak
    return np.tanh(stereo * 1.06) / np.tanh(1.06)


def hover_cue() -> np.ndarray:
    return tone(0.105, ((920.0, 0.72), (1380.0, 0.20)), 0.004, 31.0)


def focus_cue() -> np.ndarray:
    first = tone(0.155, ((510.0, 0.65), (765.0, 0.23)), 0.005, 22.0)
    count = len(first)
    time = np.arange(count) / SAMPLE_RATE
    organic_pulse = np.sin(2 * math.pi * (610.0 * time + 210.0 * time**2))
    organic_pulse *= np.exp(-28.0 * time) * 0.10
    first[:, 0] += organic_pulse
    first[:, 1] += np.roll(organic_pulse, 13) * 0.9
    return first


def press_cue() -> np.ndarray:
    cue = tone(
        0.225,
        ((168.0, 0.62), (504.0, 0.25), (1008.0, 0.10)),
        0.003,
        18.0,
    )
    time = np.arange(len(cue)) / SAMPLE_RATE
    confirmation = np.sin(2 * math.pi * 720.0 * time)
    confirmation *= np.exp(-26.0 * np.maximum(0.0, time - 0.045))
    confirmation *= time >= 0.045
    cue[:, 0] += confirmation * 0.12
    cue[:, 1] += confirmation * 0.11
    peak = float(np.max(np.abs(cue)))
    if peak:
        cue *= 0.68 / peak
    return cue


def write_wave(path: Path, samples: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = np.round(np.clip(samples, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(SAMPLE_RATE)
        output.writeframes(pcm.tobytes())


if __name__ == "__main__":
    write_wave(OUTPUT_DIR / "button_hover.wav", hover_cue())
    write_wave(OUTPUT_DIR / "button_focus.wav", focus_cue())
    write_wave(OUTPUT_DIR / "button_press.wav", press_cue())
    print(f"Wrote interface audio to {OUTPUT_DIR}")
