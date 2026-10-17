# Synthesizes the extra ambience clips (wind, night crickets, sparrow chirps, a distant bell, a plane passing high over, an
# indoor crowd murmur and cutlery clinks) as 22.05 kHz mono 16-bit WAVs under assets/Audio/Resources/Audio, the format of the
# other clips. Loops are built so their ends meet. Deterministic: rerunning writes the same files. Needs numpy.
# Run from the bevy folder: python tools/ambient_sounds.py
import os
import wave

import numpy as np

RATE = 22050
OUT = os.path.join("assets", "Audio", "Resources", "Audio")
rng = np.random.default_rng(20261017)


def write(name, signal, peak=0.7):
    signal = np.asarray(signal, dtype=np.float64)
    signal = signal / max(1e-9, np.max(np.abs(signal))) * peak
    data = (np.clip(signal, -1, 1) * 32767).astype("<i2").tobytes()
    with wave.open(os.path.join(OUT, f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data)


def seconds(t):
    return np.arange(int(t * RATE)) / RATE


def lowpass(x, cutoff):
    """A one-pole low-pass run twice (12 dB per octave)."""
    a = np.exp(-2 * np.pi * cutoff / RATE)
    for _ in range(2):
        y = np.empty_like(x)
        acc = 0.0
        for i, v in enumerate(x):
            acc = (1 - a) * v + a * acc
            y[i] = acc
        x = y
    return x


def bandpass(x, low, high):
    return lowpass(x, high) - lowpass(x, low)


def fft_filter(x, low, high, tilt=0.0):
    """A brick-wall band in the frequency domain (fast for long loops), optionally tilted toward the low end; wraps cleanly."""
    spectrum = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1 / RATE)
    gain = ((freqs >= low) & (freqs <= high)).astype(float)
    if tilt:
        gain *= (np.maximum(freqs, low) / low) ** -tilt
    return np.fft.irfft(spectrum * gain, len(x))


def loop_smooth(length, rate_hz, depth, seed):
    """A slow random envelope that wraps around (sum of whole-cycle sines)."""
    t = seconds(length)
    local = np.random.default_rng(seed)
    env = np.zeros_like(t)
    for k in range(1, 6):
        cycles = max(1, round(rate_hz * length * k / 3))
        env += local.uniform(0.3, 1.0) / k * np.sin(2 * np.pi * cycles * t / length + local.uniform(0, 2 * np.pi))
    env = env / np.max(np.abs(env))
    return 1 - depth + depth * (0.5 + 0.5 * env)


def wind():
    length = 24.0
    noise = rng.standard_normal(int(length * RATE))
    body = fft_filter(noise, 40, 900, tilt=1.2)
    whistle = fft_filter(noise, 500, 1400) * 0.25
    gust = loop_smooth(length, 0.12, 0.85, 1)
    return (body + whistle * gust) * gust


def crickets():
    length = 8.0
    t = seconds(length)
    out = np.zeros_like(t)
    for c in range(5):
        carrier = 4100 + c * 170 + rng.uniform(-40, 40)
        pulse_rate = 60.0
        chirp_every = rng.uniform(0.55, 0.9)
        offset = rng.uniform(0, chirp_every)
        gain = rng.uniform(0.3, 1.0) * (0.6 if c > 1 else 1.0)
        phase_in_chirp = ((t + offset) % chirp_every)
        chirping = phase_in_chirp < 0.12
        pulses = 0.5 + 0.5 * np.sin(2 * np.pi * pulse_rate * t)
        out += gain * np.sin(2 * np.pi * carrier * t) * pulses ** 2 * chirping
    # Soften the chirp edges.
    return lowpass(out, 6000) * loop_smooth(length, 0.2, 0.3, 7)


def chirp(seed):
    local = np.random.default_rng(seed)
    t_total = 0.0
    parts = []
    for _ in range(local.integers(2, 5)):
        length = local.uniform(0.05, 0.12)
        t = seconds(length)
        f0, f1 = local.uniform(3200, 4200), local.uniform(4600, 6200)
        if local.random() < 0.5:
            f0, f1 = f1, f0
        freq = f0 + (f1 - f0) * (t / length) ** 0.7
        phase = 2 * np.pi * np.cumsum(freq) / RATE
        env = np.sin(np.pi * t / length) ** 1.5
        parts.append(np.sin(phase) * env + 0.2 * np.sin(2 * phase) * env)
        gap = local.uniform(0.04, 0.12)
        parts.append(np.zeros(int(gap * RATE)))
        t_total += length + gap
    out = np.concatenate(parts + [np.zeros(int(0.2 * RATE))])
    return out


def bell():
    length = 7.0
    t = seconds(length)
    out = np.zeros_like(t)
    partials = [(0.5, 1.0, 1.6), (1.0, 0.8, 1.2), (1.19, 0.5, 1.0), (1.5, 0.45, 0.9), (2.0, 0.35, 0.7), (2.74, 0.2, 0.5), (3.0, 0.15, 0.4)]
    base = 392.0
    for strike in (0.0, 2.2):
        local = np.where(t >= strike, t - strike, 0)
        on = t >= strike
        for ratio, gain, decay in partials:
            out += on * gain * np.sin(2 * np.pi * base * ratio * local) * np.exp(-local / (decay * 1.6))
    # Far away: dull the top and add a soft echo.
    out = lowpass(out, 1800)
    echo = np.concatenate([np.zeros(int(0.23 * RATE)), out[: -int(0.23 * RATE)]])
    return out + 0.35 * echo


def plane():
    length = 14.0
    t = seconds(length)
    noise = rng.standard_normal(len(t))
    rumble = fft_filter(noise, 30, 420, tilt=1.0)
    whine = np.sin(2 * np.pi * np.cumsum(620 - 60 * (t / length)) / RATE) * 0.05
    swell = np.sin(np.pi * t / length) ** 2.2
    return (rumble + whine) * swell


def murmur():
    length = 16.0
    t = seconds(length)
    out = np.zeros_like(t)
    for voice in range(9):
        local = np.random.default_rng(100 + voice)
        noise = local.standard_normal(len(t))
        low, high = local.uniform(250, 450), local.uniform(1100, 2200)
        band = fft_filter(noise, low, high, tilt=0.6)
        # Syllables at a talking rate, phrases with pauses; whole cycles so the loop wraps.
        syllables = 0.5 + 0.5 * np.sin(2 * np.pi * round(local.uniform(3.5, 5.5) * length) * t / length + local.uniform(0, 6.28))
        phrases = np.clip(np.sin(2 * np.pi * round(local.uniform(0.15, 0.35) * length) * t / length + local.uniform(0, 6.28)) * 1.6 + 0.4, 0, 1)
        out += band * syllables ** 1.5 * phrases * local.uniform(0.4, 1.0)
    return out * loop_smooth(length, 0.1, 0.25, 11)


def clink(seed):
    local = np.random.default_rng(seed)
    length = 0.6
    t = seconds(length)
    out = np.zeros_like(t)
    for _ in range(local.integers(1, 3)):
        start = local.uniform(0.0, 0.15)
        on = t >= start
        lt = np.where(on, t - start, 0)
        base = local.uniform(2200, 3400)
        for ratio, gain in ((1.0, 1.0), (2.76, 0.5), (5.4, 0.25), (8.9, 0.12)):
            out += on * gain * np.sin(2 * np.pi * base * ratio * lt) * np.exp(-lt * local.uniform(14, 24) * ratio ** 0.5)
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    write("wind", wind(), 0.6)
    write("crickets", crickets(), 0.5)
    for i in range(3):
        write(f"chirp_{i + 1}", chirp(30 + i), 0.55)
    write("bell", bell(), 0.6)
    write("plane", plane(), 0.6)
    write("murmur", murmur(), 0.55)
    for i in range(3):
        write(f"clink_{i + 1}", clink(60 + i), 0.5)
    print(f"wrote ambience clips to {OUT}")


if __name__ == "__main__":
    main()
