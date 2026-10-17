# Writes footstep_1..6.wav: soft shoe steps on paving, each a heel strike and a later, quieter toe roll of low band-limited
# noise over a faint body thump. Attacks are several milliseconds long and nothing above ~1.5 kHz is loud, because the
# earlier one-hit clicks with a 1 ms attack read as popcorn rather than feet.
# Usage: python synth_footsteps.py <target dir>
import math
import os
import random
import sys

from synth_sounds import RATE, highpass, lowpass, noise, write


def envelope(n, start, attack, decay):
    out = []
    for i in range(n):
        t = i - start
        if t < 0:
            out.append(0.0)
        else:
            out.append(min(1.0, t / max(1, attack * RATE)) * math.exp(-t / (decay * RATE)))
    return out


def footstep(rng):
    n = int(0.3 * RATE)
    heel = lowpass(lowpass(highpass(noise(n, rng), 140), rng.uniform(700, 950)), 1600)
    toe = lowpass(lowpass(highpass(noise(n, rng), 220), rng.uniform(1000, 1300)), 2000)
    grit = lowpass(highpass(noise(n, rng), 1800), 3500)
    toe_at = int(rng.uniform(0.055, 0.075) * RATE)
    heel_env = envelope(n, 0, 0.006, 0.03)
    toe_env = envelope(n, toe_at, 0.012, 0.045)
    grit_env = envelope(n, 0, 0.01, 0.08)
    thump_f = rng.uniform(55, 75)
    thump_env = envelope(n, 0, 0.008, 0.028)
    toe_level = rng.uniform(0.45, 0.6)
    out = []
    for i in range(n):
        thump = math.sin(2 * math.pi * thump_f * i / RATE) * thump_env[i]
        out.append(heel[i] * heel_env[i] + toe_level * toe[i] * toe_env[i] + 0.06 * grit[i] * grit_env[i] + 0.35 * thump)
    fade = int(0.03 * RATE)
    for i in range(fade):
        out[n - 1 - i] *= i / fade
    return out


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(target, exist_ok=True)
    rng = random.Random(20261017)
    for i in range(1, 7):
        write(os.path.join(target, f"footstep_{i}.wav"), footstep(rng), 0.7)


if __name__ == "__main__":
    main()
