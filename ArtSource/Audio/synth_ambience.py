# Synthesizes the clips that replaced synth_sounds.py's footsteps, engine, oven hum and city bed (2026-10-14: those had
# sub-bass thumps), plus the farm and factory beds, horns, birds, clanks and the menu music. Needs numpy.
# Usage: python -I synth_ambience.py <output dir>   (the project keeps them in Assets/Audio/Resources/Audio)
import sys, os, wave
import numpy as np

OUT = sys.argv[1]
SR = 22050
rng = np.random.default_rng(20261014)


def write(name, x, sr=SR, peak=0.8):
    x = np.asarray(x, dtype=float)
    if x.ndim == 1:
        x = x - x.mean()
    else:
        x = x - x.mean(axis=0)
    m = np.abs(x).max()
    if m > 0:
        x = x / m * peak
    ch = 1 if x.ndim == 1 else x.shape[1]
    data = (np.clip(x, -1, 1) * 32767).astype('<i2')
    with wave.open(os.path.join(OUT, name + '.wav'), 'wb') as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(data.tobytes())
    print('wrote', name, f'{len(x) / sr:.2f}s', 'ch', ch)


def band(f, lo=None, hi=None, order=2):
    g = np.ones_like(f)
    with np.errstate(divide='ignore', invalid='ignore'):
        if hi is not None:
            g *= 1.0 / np.sqrt(1.0 + (f / hi) ** (2 * order))
        if lo is not None:
            r = np.where(f > 0, lo / np.maximum(f, 1e-9), 1e9)
            g *= 1.0 / np.sqrt(1.0 + r ** (2 * order))
    return g


def shaped_noise(n, shape, sr=SR):
    """Periodic (loopable) noise of n samples whose spectrum is shaped by shape(f)."""
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / sr)
    y = np.fft.irfft(spec * shape(f), n)
    return y / (y.std() + 1e-12)


def filt(x, shape, sr=SR, circular=False):
    n = len(x)
    m = n if circular else int(2 ** np.ceil(np.log2(n * 2)))
    f = np.fft.rfftfreq(m, 1 / sr)
    y = np.fft.irfft(np.fft.rfft(x, m) * shape(f), m)
    return y[:n]


def lfo(n, max_hz, sr=SR):
    """Smooth periodic random modulation in [-1, 1] with content below max_hz."""
    y = shaped_noise(n, lambda f: (f > 0) * band(f, hi=max_hz, order=4), sr)
    return y / (np.abs(y).max() + 1e-12)


def add_wrap(buf, ev, start):
    n = len(buf)
    idx = (np.arange(len(ev)) + int(start)) % n
    np.add.at(buf, idx, ev)


def tone_track(freq, sr=SR):
    """Phase-integrated tone for a per-sample frequency array."""
    return np.cumsum(2 * np.pi * freq / sr)


def circ_reverb(x, seconds, decay, sr=SR, wet=0.3, stereo=False, lp=5000):
    n = len(x)
    L = min(int(seconds * sr), len(x))
    outs = []
    for c in range(2 if stereo else 1):
        ir = rng.standard_normal(L) * np.exp(-np.arange(L) / (decay * sr))
        ir = filt(ir, lambda f: band(f, lo=120, hi=lp))
        ir /= np.sqrt((ir ** 2).sum())
        h = np.zeros(n)
        h[:L] = ir
        src = x if x.ndim == 1 else x[:, c]
        y = np.fft.irfft(np.fft.rfft(src) * np.fft.rfft(h), n)
        outs.append(y)
    if stereo:
        dry = x if x.ndim == 2 else np.stack([x, x], 1)
        wetsig = np.stack(outs, 1)
        return dry * (1 - wet) + wetsig * wet * (np.abs(dry).std() / (wetsig.std() + 1e-12))
    return x * (1 - wet) + outs[0] * wet * (x.std() / (outs[0].std() + 1e-12))


def soft(x, drive=1.5):
    return np.tanh(x * drive) / np.tanh(drive)


def env_adsr(n, a, r, sr=SR):
    e = np.ones(n)
    na, nr = max(1, int(a * sr)), max(1, int(r * sr))
    e[:na] = np.linspace(0, 1, na)
    e[-nr:] *= np.linspace(1, 0, nr)
    return e


# ---------------------------------------------------------------- footsteps
def footstep(seed):
    r = np.random.default_rng(seed)
    n = int(0.26 * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    # Heel: a short scuffed noise burst, mid band, quick decay.
    heel = r.standard_normal(n) * np.exp(-t / r.uniform(0.010, 0.016))
    heel = filt(heel, lambda f: band(f, lo=r.uniform(350, 550), hi=r.uniform(2600, 3800), order=2))
    # A little body, well above sub-bass, kept small so it does not thump.
    body = np.sin(2 * np.pi * r.uniform(140, 190) * t) * np.exp(-t / 0.012) * r.uniform(0.15, 0.25)
    x += heel / (np.abs(heel).max() + 1e-9) + body
    # Toe roll / scuff a moment later: quieter and brighter.
    d = int(r.uniform(0.045, 0.075) * SR)
    m = n - d
    tt = np.arange(m) / SR
    toe = r.standard_normal(m) * (1 - np.exp(-tt / 0.003)) * np.exp(-tt / r.uniform(0.018, 0.03))
    toe = filt(toe, lambda f: band(f, lo=900, hi=r.uniform(4500, 6500), order=2))
    x[d:] += toe / (np.abs(toe).max() + 1e-9) * r.uniform(0.3, 0.5)
    # Grit: a few tiny ticks.
    for _ in range(r.integers(3, 7)):
        p = int(r.uniform(0.0, 0.1) * SR)
        k = r.standard_normal(40) * np.exp(-np.arange(40) / 6)
        x[p:p + 40] += filt(k, lambda f: band(f, lo=2500, hi=8000)) * r.uniform(0.05, 0.12)
    x = filt(x, lambda f: band(f, lo=90, order=3))
    x *= env_adsr(n, 0.001, 0.05)
    return x


for i in range(6):
    write(f'footstep_{i + 1}', footstep(100 + i), peak=0.7)


# ---------------------------------------------------------------- vehicle engine loop (2 s, 110 whole cycles of 55 Hz)
def engine():
    dur, f0 = 2.0, 55.0
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    # Harmonic engine note: weak fundamental, body in harmonics 2-8, so pitch is heard without sub-bass pulses.
    amps = {1: 0.12, 2: 0.55, 3: 0.45, 4: 0.6, 5: 0.3, 6: 0.32, 7: 0.15, 8: 0.2, 10: 0.08, 12: 0.06}
    for h, a in amps.items():
        x += a * np.sin(2 * np.pi * f0 * h * t + rng.uniform(0, 2 * np.pi))
    # Cycle-to-cycle roughness: a periodic gain wobble (content up to ~30 Hz, loops exactly).
    x *= 1 + 0.12 * lfo(n, 30)
    # Intake and exhaust breath.
    x += 0.35 * shaped_noise(n, lambda f: band(f, lo=150, hi=700, order=2)) * (1 + 0.3 * np.sin(2 * np.pi * f0 * 2 * t))
    # Tyres on asphalt: broad mid-high hiss, which carries the pass-by.
    x += 0.4 * shaped_noise(n, lambda f: band(f, lo=400, hi=3000, order=1) * (f > 0) / np.sqrt(np.maximum(f, 1) / 400))
    x = filt(x, lambda f: band(f, lo=60, order=4), circular=True)
    return x


write('engine', engine(), peak=0.75)


# ---------------------------------------------------------------- oven hum loop (4 s)
def hum():
    n = int(4.0 * SR)
    t = np.arange(n) / SR
    x = 0.25 * np.sin(2 * np.pi * 60 * t) + 0.6 * np.sin(2 * np.pi * 120 * t + 0.4) + 0.2 * np.sin(2 * np.pi * 240 * t + 1.1) \
        + 0.08 * np.sin(2 * np.pi * 360 * t + 2.0)
    x += 0.35 * shaped_noise(n, lambda f: band(f, lo=250, hi=2500, order=2)) * (1 + 0.1 * lfo(n, 2))
    return filt(x, lambda f: band(f, lo=45, order=3), circular=True)


write('hum', hum(), peak=0.7)


# ---------------------------------------------------------------- city bed (40 s loop)
def pass_by(n_ev, bright, f_engine):
    t = np.arange(n_ev) / SR
    tc = t[-1] / 2
    w = t[-1] / 4.5
    envl = np.exp(-0.5 * ((t - tc) / w) ** 2)
    # Approaching is brighter and higher, receding darker and lower: crossfade two filtered noises.
    k = 0.5 * (1 + np.tanh((t - tc) / (w * 0.4)))
    hi_n = filt(rng.standard_normal(n_ev), lambda f: band(f, lo=500, hi=bright, order=2))
    lo_n = filt(rng.standard_normal(n_ev), lambda f: band(f, lo=250, hi=bright * 0.45, order=2))
    hi_n /= hi_n.std() + 1e-9
    lo_n /= lo_n.std() + 1e-9
    noise = (1 - k) * hi_n + k * lo_n
    freq = f_engine * (1 + 0.05 * np.tanh(-(t - tc) / (w * 0.4)))
    ph = tone_track(freq)
    tone = 0.5 * np.sin(2 * ph) + 0.35 * np.sin(3 * ph) + 0.25 * np.sin(4 * ph)
    return (noise * 0.8 + tone * 0.4) * envl


def city():
    n = int(40.0 * SR)
    # Traffic rumble: no sub-bass (high-passed at 70 Hz), weight around 100-300 Hz, gently swelling.
    rumble = shaped_noise(n, lambda f: band(f, lo=70, hi=350, order=3) + 0.25 * band(f, lo=350, hi=1500, order=1))
    rumble *= 1 + 0.25 * lfo(n, 0.15)
    air = shaped_noise(n, lambda f: band(f, lo=1500, hi=7000, order=2)) * 0.12
    x = rumble * 0.6 + air
    cars = np.zeros(n)
    t0 = 0.0
    while t0 < 40.0:
        dur = rng.uniform(3.0, 6.5)
        ev = pass_by(int(dur * SR), rng.uniform(2500, 5000), rng.uniform(40, 75)) * rng.uniform(0.25, 0.9)
        add_wrap(cars, ev, t0 * SR)
        t0 += rng.uniform(1.2, 4.0)
    x += cars * 0.55
    x = circ_reverb(x, 1.5, 0.35, wet=0.2)
    x = filt(x, lambda f: band(f, lo=60, order=4), circular=True)
    x /= np.abs(x).max()
    return soft(x, 1.2)


write('city', city(), peak=0.8)


# ---------------------------------------------------------------- farm bed (40 s loop)
def chirp(kind, r):
    if kind == 0:  # quick rising tweet
        d = r.uniform(0.06, 0.12)
        n = int(d * SR)
        t = np.arange(n) / SR
        f = np.linspace(r.uniform(2600, 3200), r.uniform(3800, 4800), n)
    elif kind == 1:  # falling whistle
        d = r.uniform(0.15, 0.3)
        n = int(d * SR)
        t = np.arange(n) / SR
        f = np.linspace(r.uniform(3200, 3800), r.uniform(2000, 2500), n)
    else:  # trill note
        d = r.uniform(0.03, 0.05)
        n = int(d * SR)
        t = np.arange(n) / SR
        f = np.full(n, r.uniform(4200, 5600))
    f = f * (1 + 0.02 * np.sin(2 * np.pi * r.uniform(25, 45) * t))
    e = np.sin(np.pi * np.linspace(0, 1, n)) ** 1.5
    ph = tone_track(f)
    return (np.sin(ph) + 0.12 * np.sin(2 * ph)) * e


def bird_phrase(r):
    kind = r.integers(0, 3)
    out = []
    count = r.integers(2, 6) if kind != 2 else r.integers(6, 14)
    for _ in range(count):
        out.append(chirp(kind, r))
        out.append(np.zeros(int(r.uniform(0.03, 0.12) * SR) if kind != 2 else int(0.025 * SR)))
    return np.concatenate(out)


def farm():
    n = int(40.0 * SR)
    gust = 0.5 + 0.5 * lfo(n, 0.12)
    wind = shaped_noise(n, lambda f: band(f, lo=120, hi=900, order=2)) * (0.35 + 0.65 * gust)
    flutter = 0.5 + 0.5 * lfo(n, 18)
    rustle = shaped_noise(n, lambda f: band(f, lo=2000, hi=7000, order=2)) * (gust ** 2) * (0.3 + 0.7 * flutter) * 0.35
    x = wind * 0.5 + rustle
    birds = np.zeros(n)
    r = np.random.default_rng(7)
    t0 = 0.5
    while t0 < 40.0:
        add_wrap(birds, bird_phrase(r) * r.uniform(0.25, 0.8), t0 * SR)
        t0 += r.uniform(1.5, 5.0)
    birds = circ_reverb(birds, 0.8, 0.15, wet=0.25, lp=8000)
    # Crickets: a soft pulsed 4.6 kHz buzz in a few bursts.
    t = np.arange(n) / SR
    cr = np.sin(2 * np.pi * 4600 * t) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 30 * t))) \
        * np.clip(lfo(n, 0.3), 0, None) ** 2
    x += birds * 0.35 + filt(cr, lambda f: band(f, lo=3000, hi=6000), circular=True) * 0.04
    x = filt(x, lambda f: band(f, lo=80, order=4), circular=True)
    x /= np.abs(x).max()
    return soft(x, 1.1)


write('farm', farm(), peak=0.8)


# ---------------------------------------------------------------- one-shots: birds, clanks, honks
def bird_one(seed):
    r = np.random.default_rng(seed)
    x = np.concatenate([bird_phrase(r), np.zeros(int(0.2 * SR))])
    return x


for i in range(3):
    write(f'bird_{i + 1}', bird_one(300 + i), peak=0.6)


def clank(seed, length=0.9):
    r = np.random.default_rng(seed)
    n = int(length * SR)
    t = np.arange(n) / SR
    base = r.uniform(380, 700)
    x = np.zeros(n)
    for ratio, a, tau in ((1.0, 1.0, 0.25), (2.76, 0.6, 0.15), (5.40, 0.4, 0.08), (8.93, 0.25, 0.05), (1.5, 0.3, 0.3)):
        x += a * np.sin(2 * np.pi * base * ratio * r.uniform(0.98, 1.02) * t) * np.exp(-t / (tau * r.uniform(0.7, 1.3)))
    hit = r.standard_normal(n) * np.exp(-t / 0.006)
    x += filt(hit, lambda f: band(f, lo=800, hi=6000)) * 0.6
    x = filt(x, lambda f: band(f, lo=200, order=3))
    x = circ_reverb(np.concatenate([x, np.zeros(int(0.6 * SR))]), 0.6, 0.18, wet=0.3)
    return x * env_adsr(len(x), 0.0005, 0.2)


for i in range(3):
    write(f'clank_{i + 1}', clank(400 + i), peak=0.7)


def horn_segment(dur, f1, f2):
    n = int(dur * SR)
    t = np.arange(n) / SR
    bend = 1 - 0.03 * np.exp(-t / 0.03)
    x = np.zeros(n)
    for f in (f1, f2):
        ph = tone_track(f * bend * (1 + 0.003 * np.sin(2 * np.pi * 7 * t)))
        for h in range(1, 9):
            x += np.sin(h * ph) / h ** 1.1
    x = filt(x, lambda f: band(f, lo=250, hi=2800, order=2))
    return x * env_adsr(n, 0.012, 0.05)


def honk(pattern, f1, f2):
    parts = []
    for on, off in pattern:
        parts.append(horn_segment(on, f1, f2))
        parts.append(np.zeros(int(off * SR)))
    x = np.concatenate(parts + [np.zeros(int(0.85 * SR))])
    return circ_reverb(x, 0.8, 0.2, wet=0.18)


write('honk_1', honk([(0.35, 0.0)], 405, 510), peak=0.7)
write('honk_2', honk([(0.12, 0.09), (0.28, 0.0)], 350, 440), peak=0.7)
write('honk_3', honk([(0.7, 0.0)], 300, 377), peak=0.7)


# ---------------------------------------------------------------- factory bed (30 s loop)
def factory():
    n = int(30.0 * SR)
    t = np.arange(n) / SR
    hum_ = 0.5 * np.sin(2 * np.pi * 120 * t) + 0.25 * np.sin(2 * np.pi * 240 * t + 1) + 0.1 * np.sin(2 * np.pi * 360 * t + 2)
    vent = shaped_noise(n, lambda f: band(f, lo=200, hi=2500, order=1)) * (1 + 0.15 * lfo(n, 0.3))
    whir = np.sin(2 * np.pi * 410 * t + 3 * np.sin(2 * np.pi * (1 / 3) * t)) * 0.12 * (0.6 + 0.4 * lfo(n, 0.2))
    x = hum_ * 0.35 + vent * 0.35 + whir
    events = np.zeros(n)
    r = np.random.default_rng(11)
    t0 = 0.7
    while t0 < 30.0:
        if r.random() < 0.75:
            ev = clank(500 + int(t0 * 10), r.uniform(0.5, 1.0)) * r.uniform(0.15, 0.45)
        else:  # pneumatic hiss
            m = int(r.uniform(0.4, 0.9) * SR)
            ev = filt(r.standard_normal(m), lambda f: band(f, lo=2500, hi=8000)) * env_adsr(m, 0.01, 0.3) * 0.25
            ev /= np.abs(ev).max() / 0.3
        add_wrap(events, ev, t0 * SR)
        t0 += r.uniform(1.0, 3.5)
    x += events
    x = circ_reverb(x, 2.0, 0.5, wet=0.3)
    x = filt(x, lambda f: band(f, lo=70, order=4), circular=True)
    x /= np.abs(x).max()
    return soft(x, 1.2)


write('factory', factory(), peak=0.8)


# ---------------------------------------------------------------- menu music (stereo, 44.1 kHz, 8 bars at 84 BPM)
def menu_music():
    sr = 44100
    bpm = 84
    beat = 60 / bpm
    bars = 8
    n = int(round(bars * 4 * beat * sr))
    out = np.zeros((n, 2))

    def midi(m):
        return 440.0 * 2 ** ((m - 69) / 12)

    def place(sig, start_s, pan=0.0):
        L = np.cos((pan + 1) * np.pi / 4)
        R = np.sin((pan + 1) * np.pi / 4)
        idx = (np.arange(len(sig)) + int(start_s * sr)) % n
        np.add.at(out[:, 0], idx, sig * L)
        np.add.at(out[:, 1], idx, sig * R)

    # Cmaj7, Am7, Fmaj7, G, Em7, Am7, Dm7, G7 (pad voicings, bass roots).
    chords = [([60, 64, 67, 71], 48), ([57, 60, 64, 67], 45), ([53, 57, 60, 64], 41), ([55, 59, 62, 67], 43),
              ([52, 55, 59, 62], 40), ([57, 60, 64, 67], 45), ([50, 53, 57, 60], 38), ([55, 59, 62, 65], 43)]
    bar = 4 * beat
    for b, (notes, root) in enumerate(chords):
        # Pad: soft detuned additive tones, long attack, release overlapping the next bar.
        dur = bar + 1.2
        m = int(dur * sr)
        tt = np.arange(m) / sr
        e = np.minimum(1, tt / 0.6) * np.clip((dur - tt) / 1.2, 0, 1)
        for note in notes:
            for det, pan in ((-0.004, -0.5), (0.004, 0.5)):
                f = midi(note) * (1 + det)
                s = sum(np.sin(2 * np.pi * f * h * tt + h) / h ** 2 for h in range(1, 6))
                place(s * e * 0.05, b * bar, pan)
        # Bass: root on beat 1, fifth on beat 4.
        for beat_i, note, length in ((0, root, 2.6 * beat), (3, root + 7, 0.9 * beat)):
            m = int(length * sr)
            tt = np.arange(m) / sr
            f = midi(note)
            s = (np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(4 * np.pi * f * tt)) * np.minimum(1, tt / 0.02) * np.exp(-tt / 1.2)
            s *= np.clip((length - tt) / 0.08, 0, 1)
            place(s * 0.22, b * bar + beat_i * beat, 0.0)
    # Mallet melody on eighths from a C-major pentatonic over each chord, sparse and deterministic.
    r = np.random.default_rng(84)
    scale = [72, 74, 76, 79, 81, 84]
    prev = 2
    for b, (notes, _) in enumerate(chords):
        chord_pcs = {x % 12 for x in notes}
        for s8 in range(8):
            if r.random() < (0.55 if s8 % 2 == 0 else 0.3):
                step = int(r.integers(-2, 3))
                prev = int(np.clip(prev + step, 0, len(scale) - 1))
                note = scale[prev]
                if s8 == 0 and note % 12 not in chord_pcs:
                    note = min((x + 12 for x in notes), key=lambda q: abs(q - note))
                m = int(1.2 * sr)
                tt = np.arange(m) / sr
                f = midi(note)
                s = (np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.5) + 0.2 * np.sin(2 * np.pi * f * 4 * tt) * np.exp(-tt / 0.06))
                s *= np.minimum(1, tt / 0.003)
                place(s * r.uniform(0.09, 0.14), b * bar + s8 * beat / 2, r.uniform(-0.35, 0.35))
    # Brushed shaker on off-beats.
    for k in range(bars * 4):
        m = int(0.12 * sr)
        tt = np.arange(m) / sr
        s = filt(rng.standard_normal(m), lambda f: band(f, lo=5000, hi=11000), sr=sr) * np.exp(-tt / 0.03)
        place(s * 0.035, k * beat + beat / 2, 0.25)
    out = circ_reverb(out, 2.2, 0.6, sr=sr, wet=0.28, stereo=True, lp=6000)
    out = np.stack([filt(out[:, c], lambda f: band(f, lo=40, order=3), sr=sr, circular=True) for c in range(2)], 1)
    out /= np.abs(out).max()
    return soft(out, 1.1), sr


music, msr = menu_music()
write('menu_music', music, sr=msr, peak=0.85)
