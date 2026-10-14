# Synthesizes the game's placeholder sound effects and loops from code (standard library only), so every clip is original
# and license-free. Loops are made seamless by crossfading their tail into their head; tonal parts use whole cycles per loop.
# Usage: python -I synth_sounds.py <output dir>   (the project keeps them in Assets/Audio/Resources/Audio)
import math
import os
import random
import struct
import sys
import wave

RATE = 22050


def write(path, samples, peak=0.9):
    top = max(1e-9, max(abs(x) for x in samples))
    scale = peak / top
    with wave.open(path, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, x * scale)) * 32767)) for x in samples))


def noise(n, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def lowpass(x, cutoff):
    a = 1.0 - math.exp(-2.0 * math.pi * cutoff / RATE)
    y, out = 0.0, []
    for v in x:
        y += a * (v - y)
        out.append(y)
    return out


def highpass(x, cutoff):
    low = lowpass(x, cutoff)
    return [a - b for a, b in zip(x, low)]


def seamless(x, length, fade):
    # x holds length + fade samples; the head is blended with what follows the tail.
    out = list(x[:length])
    for i in range(fade):
        t = i / fade
        out[i] = x[i] * math.sqrt(t) + x[length + i] * math.sqrt(1.0 - t)
    return out


def tone(n, freq, phase=0.0):
    return [math.sin(2.0 * math.pi * freq * i / RATE + phase) for i in range(n)]


def env_decay(n, seconds, attack=0.002):
    a = max(1, int(attack * RATE))
    return [min(1.0, i / a) * math.exp(-i / (seconds * RATE)) for i in range(n)]


def ui_click(rng):
    n = int(0.05 * RATE)
    body = [math.sin(2 * math.pi * 1800 * i / RATE) * math.exp(-i / (0.008 * RATE)) for i in range(n)]
    tick = highpass(noise(n, rng), 3000)
    return [b + 0.4 * t * math.exp(-i / (0.002 * RATE)) for i, (b, t) in enumerate(zip(body, tick))]


def ui_open(rng):
    # A soft two-note rise for opening a panel or the phone.
    n = int(0.18 * RATE)
    out = []
    for i in range(n):
        f = 660 if i < n * 0.45 else 990
        local = i if i < n * 0.45 else i - int(n * 0.45)
        out.append(math.sin(2 * math.pi * f * i / RATE) * math.exp(-local / (0.05 * RATE)) * min(1, local / 40))
    return out


def cash(rng):
    n = int(1.1 * RATE)
    clack = lowpass(noise(n, rng), 2500)
    out = [0.0] * n
    for i in range(n):
        out[i] += 0.8 * clack[i] * math.exp(-i / (0.015 * RATE))
    # Two bell strikes with inharmonic partials.
    for start, base in ((0.06, 1568.0), (0.16, 2093.0)):
        s = int(start * RATE)
        for ratio, amp, decay in ((1.0, 1.0, 0.45), (2.76, 0.4, 0.2), (5.4, 0.2, 0.08)):
            for i in range(s, n):
                k = i - s
                out[i] += 0.45 * amp * math.sin(2 * math.pi * base * ratio * k / RATE) * math.exp(-k / (decay * RATE)) * min(1, k / 30)
    return out


def footstep(rng):
    n = int(0.16 * RATE)
    scuff = lowpass(highpass(noise(n, rng), 300), rng.uniform(1400, 2200))
    thump_f = rng.uniform(70, 100)
    out = []
    for i in range(n):
        e1 = math.exp(-i / (0.025 * RATE)) * min(1, i / 20)
        e2 = math.exp(-i / (0.04 * RATE)) * min(1, i / 60)
        out.append(0.8 * scuff[i] * e1 + 0.9 * math.sin(2 * math.pi * thump_f * i / RATE) * e2)
    return out


def place(rng):
    n = int(0.35 * RATE)
    knock = lowpass(noise(n, rng), 900)
    out = []
    for i in range(n):
        out.append(1.0 * math.sin(2 * math.pi * 65 * i / RATE) * math.exp(-i / (0.07 * RATE)) * min(1, i / 30)
                   + 0.9 * math.sin(2 * math.pi * 210 * i / RATE) * math.exp(-i / (0.03 * RATE))
                   + 1.2 * knock[i] * math.exp(-i / (0.02 * RATE)))
    return out


def sizzle(rng):
    length, fade = 4 * RATE, RATE // 2
    n = length + fade
    hiss = highpass(noise(n, rng), 2500)
    # Slow bubbling of the hiss level plus random fat pops.
    level = lowpass(noise(n, rng), 6)
    out = [h * (0.35 + 2.5 * abs(l)) for h, l in zip(hiss, level)]
    for _ in range(140):
        at = rng.randrange(n - 400)
        size = rng.uniform(0.3, 1.0)
        for k in range(rng.randrange(60, 300)):
            out[at + k] += size * rng.uniform(-1, 1) * math.exp(-k / 40)
    return seamless(out, length, fade)


def hum(rng):
    # Oven: mains hum and a fan, very steady.
    length, fade = 4 * RATE, RATE // 2
    n = length + fade
    fan = lowpass(noise(n, rng), 500)
    out = []
    for i in range(n):
        t = i / RATE
        out.append(0.5 * math.sin(2 * math.pi * 60 * t) + 0.3 * math.sin(2 * math.pi * 120 * t)
                   + 0.12 * math.sin(2 * math.pi * 180 * t) + 1.8 * fan[i])
    return seamless(out, length, fade)


def mixer(rng):
    length, fade = 2 * RATE, RATE // 4
    n = length + fade
    air = lowpass(highpass(noise(n, rng), 400), 3000)
    out = []
    for i in range(n):
        t = i / RATE
        wobble = 1.0 + 0.25 * math.sin(2 * math.pi * 8 * t)
        out.append(wobble * (0.5 * math.sin(2 * math.pi * 230 * t) + 0.25 * math.sin(2 * math.pi * 460 * t)
                             + 0.1 * math.sin(2 * math.pi * 690 * t)) + 0.5 * air[i])
    return seamless(out, length, fade)


def engine(rng):
    length, fade = 2 * RATE, RATE // 4
    n = length + fade
    rumble = lowpass(noise(n, rng), 180)
    out = []
    for i in range(n):
        t = i / RATE
        firing = 0.5 + 0.5 * math.sin(2 * math.pi * 25 * t)
        out.append(firing ** 3 * (0.6 * math.sin(2 * math.pi * 50 * t) + 0.3 * math.sin(2 * math.pi * 100 * t))
                   + 0.25 * math.sin(2 * math.pi * 37.5 * t) + 3.0 * rumble[i])
    return seamless(out, length, fade)


def city(rng):
    # Distant traffic rumble, the odd passing car and a few birds; quiet and long so the repeat is hard to hear.
    length, fade = 16 * RATE, 2 * RATE
    n = length + fade
    out = [3.0 * v for v in lowpass(lowpass(noise(n, rng), 250), 250)]
    for _ in range(4):
        at, dur = rng.randrange(0, n - 4 * RATE), rng.uniform(2.0, 3.5)
        swoosh = lowpass(noise(int(dur * RATE), rng), 700)
        for k, v in enumerate(swoosh):
            out[at + k] += 0.35 * v * math.sin(math.pi * k / len(swoosh)) ** 2
    for _ in range(9):
        at = rng.randrange(0, n - RATE)
        base = rng.uniform(2800, 4200)
        for chirp in range(rng.randrange(2, 5)):
            start = at + chirp * int(0.12 * RATE)
            m = int(0.07 * RATE)
            phase = 0.0
            for k in range(m):
                f = base * (1 + 0.35 * math.sin(math.pi * k / m))
                phase += 2 * math.pi * f / RATE
                out[start + k] += 0.05 * math.sin(phase) * math.sin(math.pi * k / m)
    return seamless(out, length, fade)


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(target, exist_ok=True)
    rng = random.Random(20261013)
    clips = {
        "ui_click": (ui_click, 0.5),
        "ui_open": (ui_open, 0.5),
        "cash": (cash, 0.8),
        "place": (place, 0.9),
        "sizzle": (sizzle, 0.7),
        "hum": (hum, 0.7),
        "mixer": (mixer, 0.7),
        "engine": (engine, 0.8),
        "city": (city, 0.8),
    }
    for name, (make, peak) in clips.items():
        write(os.path.join(target, name + ".wav"), make(rng), peak)
    for i in range(1, 5):
        write(os.path.join(target, f"footstep_{i}.wav"), footstep(rng), 0.8)


if __name__ == "__main__":
    main()
