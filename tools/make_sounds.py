"""
Makes the game's sound effects from scratch, so no recorded or borrowed sounds
are needed and there is no licence to worry about. Each sound is built from
noise and simple tones, shaped with envelopes and filters, and given the echo
of a steep Norwegian valley. They are written as 16-bit mono WAV files in
assets/sounds/.

    python3 tools/make_sounds.py

Change a number and run it again to try a different sound. Needs numpy and
scipy (pip install numpy scipy).

The weapons are the ones used in Saltdal in May 1940:

    British    Rifle No. 1 Mk III (Lee-Enfield), Bren gun, 2-inch and 3-inch
               mortars, 25-pounder field guns
    Norwegian  Krag-Jorgensen rifle, Colt M/29 machine gun, 8 cm mortar
    German     Kar 98k rifle, MG 34, MP 40, 8 cm Granatwerfer 34, 75 mm
               mountain guns, Ju 87 Stuka dive bombers with their siren

What makes each one sound like itself, roughly:

    - A bolt-action rifle is one hard crack, then the rattle of the bolt being
      worked: up, back, forward, down. The Lee-Enfield's bolt is short and
      quick, the Krag's long and smooth, the Mauser's heavy.
    - The Bren fires about 500 rounds a minute: an even, slow "dut-dut-dut".
      The MG 34 fires about 900: a tearing rip. The game plays one round at a
      time at each gun's own rate (see data/weapons.json), so these are single
      rounds with a short tail.
    - The MP 40 fires pistol bullets slower than sound, so it has no crack:
      a duller pop and the clack of its heavy bolt.
    - Shells and bombs have a sharp crack at the start, a deep thump, falling
      earth and stones, and a long rumble rolling back off the mountains.
    - The Stuka has a deep, rough V12 drone and, as it dives, the wailing
      "Jericho trumpet" siren driven by a small propeller on its leg.
"""
import os
import wave

import numpy as np
from scipy import signal

RATE = 32000
OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "sounds")
rng = np.random.default_rng(1940)


# ---------------------------------------------------------------- building blocks

def n_of(seconds):
    return int(round(seconds * RATE))


def t(seconds):
    return np.arange(n_of(seconds)) / RATE


def noise(seconds):
    return rng.standard_normal(n_of(seconds))


def lowpass(x, hz, order=2):
    b, a = signal.butter(order, min(hz, RATE * 0.45), "low", fs=RATE)
    return signal.lfilter(b, a, x)


def highpass(x, hz, order=2):
    b, a = signal.butter(order, hz, "high", fs=RATE)
    return signal.lfilter(b, a, x)


def bandpass(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo, min(hi, RATE * 0.45)], "band", fs=RATE)
    return signal.lfilter(b, a, x)


def decay(seconds, rate):
    return np.exp(-t(seconds) * rate)


def fade_in(x, seconds=0.001):
    n = min(len(x), max(1, n_of(seconds)))
    x = x.copy()
    x[:n] *= np.linspace(0, 1, n)
    return x


def pad(x, seconds):
    return np.concatenate([x, np.zeros(n_of(seconds))])


def place(out, x, at):
    """Add x into out starting at `at` seconds, growing out if needed."""
    i = n_of(at)
    if i + len(x) > len(out):
        out = np.concatenate([out, np.zeros(i + len(x) - len(out))])
    out[i:i + len(x)] += x
    return out


def thump(seconds, f0, f1, rate):
    """A low sine whose pitch falls from f0 to f1: the push of air."""
    x = t(seconds)
    freq = f1 + (f0 - f1) * np.exp(-x * 30)
    return np.sin(2 * np.pi * np.cumsum(freq) / RATE) * decay(seconds, rate)


def ring(seconds, freqs, rate):
    """Metal ringing: a few damped sine partials."""
    x = t(seconds)
    return sum(np.sin(2 * np.pi * f * x + rng.uniform(0, 6)) for f in freqs) * decay(seconds, rate)


def click(hard=1.0, tone=3200):
    """One metallic click or clack of a gun's action."""
    length = 0.035
    snap = highpass(noise(length), 1800) * decay(length, 260)
    body = ring(length, [tone, tone * 1.47, tone * 2.3], 160) * 0.5
    knock = lowpass(noise(length), 900) * decay(length, 120) * 0.8
    return fade_in(snap + body + knock) * hard


def valley(x, size=1.0, wet=0.35, slap=0.42, echoes=1.0):
    """
    The echo of the valley: a smooth reverberant tail plus a couple of
    distinct echoes off the mountainsides, each duller than the last.
    size stretches the tail; slap is the delay to the first echo; echoes
    scales how loud the distinct echoes are.
    """
    tail_len = 1.8 * size
    ir = noise(tail_len) * decay(tail_len, 3.2 / size)
    ir = lowpass(ir, 2200) * 0.6 + lowpass(ir, 500)
    ir[:n_of(0.03)] = 0  # the tail starts a little after the shot
    rev = signal.fftconvolve(x, ir)[: len(x) + len(ir)]
    rev = rev / (np.max(np.abs(rev)) + 1e-9) * np.max(np.abs(x))
    out = pad(x, tail_len + slap * 3)
    out[: len(rev)] += rev * wet
    for d, g, hz in ((slap, 0.32, 1800), (slap * 1.9, 0.18, 1100), (slap * 2.7, 0.10, 700)):
        out = place(out, lowpass(x, hz) * g * echoes, d)
    return out


def trim(x, floor=0.002):
    """Cut the silent end off."""
    peak = np.max(np.abs(x)) + 1e-9
    loud = np.nonzero(np.abs(x) > peak * floor)[0]
    end = loud[-1] + 1 if len(loud) else len(x)
    x = x[:end].copy()
    f = min(len(x), n_of(0.05))
    x[-f:] *= np.linspace(1, 0, f)
    return x


def save(name, x, peak=0.9, cut=True):
    if cut:
        x = trim(x)
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    print(f"{name}.wav  {len(x) / RATE:.2f} s")


# ---------------------------------------------------------------- small arms

def shot(length, crack_hz, body_lo, body_hi, body_rate, boom_hz, boom, supersonic=True):
    """
    One round leaving the barrel: the supersonic crack of the bullet (rifle
    calibres only), the muzzle blast and a low push of air.
    """
    out = np.zeros(n_of(length))
    if supersonic:
        # The bullet's crack: a very short, very bright N-shaped wave
        cl = 0.006
        crack = highpass(noise(cl), crack_hz, 4) * decay(cl, 500)
        crack += np.linspace(1, -1, n_of(cl)) * decay(cl, 300) * 1.5
        out = place(out, crack * 1.2, 0)
    blast = bandpass(noise(length), body_lo, body_hi) * decay(length, body_rate) * 3.0
    blast += highpass(noise(length), 2500) * decay(length, body_rate * 3) * 1.2
    out = place(out, fade_in(blast, 0.0007), 0.002)
    out = place(out, thump(length, boom_hz * 2, boom_hz, body_rate * 0.8) * boom, 0.002)
    return np.tanh(out * 1.3)


def bolt(pattern):
    """The bolt being worked: (seconds, loudness, pitch) for each clack."""
    out = np.zeros(1)
    for at, hard, tone in pattern:
        out = place(out, click(hard, tone), at)
    return out


def rifle(name, crack_hz, body_lo, body_hi, boom_hz, boom, bolt_pattern, bolt_at):
    x = shot(0.5, crack_hz, body_lo, body_hi, 11, boom_hz, boom)
    x = valley(x, 1.1, 0.35, 0.45)
    # The bolt is close to the man, so it is dry and quiet
    x = place(x, bolt(bolt_pattern) * 0.22, bolt_at)
    save(name, x)


def lee_enfield():
    # .303: a hard, bright crack; the short cock-on-close bolt is very quick
    rifle("rifle_lee_enfield", 3500, 350, 2600, 70, 0.9,
          [(0, 0.8, 3600), (0.07, 0.6, 2900), (0.22, 0.9, 3300), (0.28, 1.0, 2500)], 0.65)


def krag():
    # 6.5 mm: lighter and a touch higher; the long, smooth bolt of the Krag
    rifle("rifle_krag", 4200, 450, 3200, 85, 0.6,
          [(0, 0.6, 3900), (0.12, 0.5, 3100), (0.36, 0.7, 3500), (0.45, 0.8, 2800)], 0.75)


def kar98k():
    # 7.92 mm Mauser: heavier and deeper; a stiff bolt with a hard cock
    rifle("rifle_kar98k", 3200, 300, 2300, 60, 1.1,
          [(0, 1.0, 3300), (0.09, 0.7, 2600), (0.27, 0.8, 3000), (0.34, 1.0, 2300)], 0.7)


def automatic(name, length, crack_hz, body_lo, body_hi, rate, boom_hz, boom, action_tone, supersonic=True,
              size=0.8, wet=0.25, peak=0.85):
    """One round of an automatic weapon; the game plays them at the gun's rate of fire."""
    x = shot(length, crack_hz, body_lo, body_hi, rate, boom_hz, boom, supersonic)
    # The working parts slamming back and forward
    x = place(x, click(0.25, action_tone), 0.012)
    # Fainter distinct echoes, so a long burst does not turn to mush
    x = valley(x, size, wet, 0.42, echoes=0.35)
    save(name, x, peak)


def bren():
    # A slow, even beat; the heavy piston and bolt give each round a thud
    automatic("mg_bren", 0.2, 3400, 300, 2400, 18, 65, 1.1, 1900)


def colt_m29():
    # The Norwegian Colt (Browning-type) machine gun, 7.92 mm on a tripod
    automatic("mg_colt", 0.18, 3300, 320, 2500, 20, 70, 1.0, 2200)


def mg34():
    # A fast, tearing gun: each round short and hard so they do not smear
    automatic("mg_mg34", 0.11, 3300, 350, 2600, 34, 75, 0.9, 2600, size=0.7, wet=0.2)


def mp40():
    # 9 mm, slower than sound: no crack, a duller pop and a big clacking bolt
    automatic("smg_mp40", 0.14, 0, 250, 1700, 30, 90, 0.7, 1500, supersonic=False,
              size=0.5, wet=0.18, peak=0.7)


def ricochet():
    # A bullet glancing off a stone: a quick whine falling in pitch
    length = 0.55
    x = t(length)
    freq = 2600 * np.exp(-x * 1.4) + 500
    whine = np.sin(2 * np.pi * np.cumsum(freq) / RATE)
    whine *= np.minimum(1, x * 60) * decay(length, 5.5)
    hit = highpass(noise(0.02), 2000) * decay(0.02, 300)
    save("ricochet", place(whine * 0.6 + bandpass(noise(length), 1500, 5000) * decay(length, 9) * 0.15, hit, 0), 0.4)


# ---------------------------------------------------------------- mortars

def mortar(name, tone, size):
    # The hollow "thoomp" of a bomb leaving the tube, and the tube ringing
    length = 0.6
    pop = thump(length, tone * 2.2, tone, 9) * 1.4
    puff = lowpass(noise(length), 700) * decay(length, 16) * 1.6
    tube = ring(length, [tone * 7.3, tone * 11.1], 14) * 0.08
    x = fade_in(pop + puff + tube, 0.003)
    save(name, valley(x, size, 0.3, 0.45), 0.8)


# ---------------------------------------------------------------- explosions

def explosion(name, length, crack, body_hz, rate, sub_hz, sub, debris, size, wet):
    """
    A crack as the case bursts, a deep thump, the roar of the blast, then
    earth, stones and splinters falling, all rolling back off the valley sides.
    """
    roar = lowpass(noise(length), body_hz, 2) * decay(length, rate) * 3.0
    bright = highpass(noise(length), 1500) * decay(length, rate * 4) * crack
    low = thump(length, sub_hz * 3, sub_hz, rate * 0.7) * sub
    x = fade_in(roar + bright + low, 0.002)
    # Falling earth and stones: a patter of little ticks and thuds
    fall = np.zeros(n_of(length + 1.5))
    for _ in range(int(debris)):
        at = 0.15 + rng.gamma(2.0, 0.25)
        if at > length + 1.3:
            continue
        g = rng.uniform(0.05, 0.25) * np.exp(-at * 1.2)
        piece = click(g, rng.uniform(600, 2500)) if rng.random() < 0.5 else lowpass(noise(0.05), 600) * decay(0.05, 60) * g
        fall = place(fall, piece, at)
    x = place(pad(x, 0.1), fall * 0.6, 0)
    x = np.tanh(x * 1.6)
    save(name, valley(x, size, wet, 0.55), 0.95)


# ---------------------------------------------------------------- incoming

def incoming_shell():
    # A field or mountain gun shell coming down: a tearing, falling whistle
    length = 1.2
    x = t(length)
    freq = 1300 - 650 * (x / length) ** 1.5
    tone = np.sin(2 * np.pi * np.cumsum(freq) / RATE)
    tear = bandpass(noise(length), 500, 2500) * 0.6
    y = (tone * 0.5 + tear) * np.minimum(1, x * 2.5) * (0.25 + 0.75 * (x / length) ** 2)
    save("incoming_shell", y, 0.4, cut=False)


def incoming_mortar():
    # A mortar bomb: a short, soft rushing swish just before it lands
    length = 0.9
    x = t(length)
    centre = 900 + 600 * x / length
    y = np.zeros(n_of(length))
    n = noise(length)
    for i in range(0, len(y), 512):  # the swish rises as it falls closer
        seg = slice(i, i + 512)
        y[seg] = bandpass(n[max(0, i - 2048):i + 512], centre[i] * 0.7, centre[i] * 1.4)[-len(n[seg]):]
    y *= (x / length) ** 2.5
    save("incoming_mortar", y, 0.35, cut=False)


def incoming_bomb():
    # An aircraft bomb: a long, loud, screaming whistle
    length = 2.0
    x = t(length)
    freq = 1700 - 1000 * (x / length) ** 1.3
    vib = 1 + 0.01 * np.sin(2 * np.pi * 7 * x)
    tone = np.sin(2 * np.pi * np.cumsum(freq * vib) / RATE)
    tone += 0.3 * np.sin(2 * np.pi * np.cumsum(freq * 2 * vib) / RATE)
    air = bandpass(noise(length), 800, 4000) * 0.3
    y = (tone + air) * np.minimum(1, x * 2) * (0.3 + 0.7 * x / length)
    save("incoming_bomb", y, 0.45, cut=False)


def distant_gun():
    # A gun firing far behind the hills: a deep, muffled boom and rumble
    length = 1.5
    boom = lowpass(noise(length), 250, 4) * decay(length, 4) * 3
    boom += thump(length, 90, 40, 5) * 2
    save("distant_gun", valley(fade_in(boom, 0.01), 1.8, 0.6, 0.7), 0.5)


# ---------------------------------------------------------------- aircraft

def make_loop(y, seconds=0.25):
    """Crossfade the end into the start so the loop does not click."""
    f = n_of(seconds)
    body = y[:-f].copy()
    tail = y[-f:]
    body[:f] = body[:f] * np.linspace(0, 1, f) + tail * np.linspace(1, 0, f)
    return body


def aircraft():
    # Ju 87: a Jumo 211 V12 at cruise, deep and rough, with the propeller beat
    length = 4.25
    x = t(length)
    firing = 2300 / 60 * 6  # V12 four-stroke: six firings per turn
    prop = 2300 / 60 / 1.55 * 3  # geared three-blade propeller
    wobble = 1 + 0.004 * np.sin(2 * np.pi * 0.7 * x)
    ph = 2 * np.pi * np.cumsum(firing * wobble) / RATE
    engine = sum(np.sin(k * ph + rng.uniform(0, 6)) / k ** 0.8 for k in range(1, 9))
    engine *= 1 + 0.35 * np.sin(2 * np.pi * np.cumsum(prop * wobble) / RATE)
    rough = lowpass(noise(length), 350, 2) * 1.2
    exhaust = bandpass(noise(length), 150, 900) * (0.6 + 0.4 * np.sin(ph / 2) ** 2)
    y = lowpass(engine * 0.6 + rough + exhaust * 0.8, 2500)
    save("aircraft", make_loop(y), 0.6, cut=False)


def stuka_siren():
    # The "Jericho trumpet": a wail rising as the dive speeds the siren's
    # little propeller, the engine screaming with it, then the pull-out.
    length = 3.6
    x = t(length)
    rise = np.clip(x / 2.8, 0, 1)
    freq = 260 + 520 * rise ** 0.8
    freq *= 1 + 0.012 * np.sin(2 * np.pi * 5.5 * x)
    ph = 2 * np.pi * np.cumsum(freq) / RATE
    siren = np.sin(ph) + 0.45 * np.sin(2 * ph) + 0.25 * np.sin(3 * ph) + 0.12 * np.sin(5 * ph)
    siren = np.tanh(siren * 1.5)
    level = np.minimum(1, x * 1.2) * np.where(x < 3.0, 1, np.exp(-(x - 3.0) * 6))
    roar = lowpass(noise(length), 900) * (0.4 + 0.6 * rise) * np.where(x < 3.0, 1, np.exp(-(x - 3.0) * 3))
    y = siren * level * 0.7 + roar * 0.8
    save("stuka_siren", valley(y, 1.2, 0.25, 0.5), 0.6)


# ---------------------------------------------------------------- the rest

def fire():
    # Crackling flames: a low roar with random pops and snaps of timber, made to loop
    length = 4.25
    roar = lowpass(noise(length), 220, 2) * 2
    roar *= 1 + 0.3 * lowpass(noise(length), 2, 1) * 20
    pops = np.zeros(n_of(length))
    for _ in range(90):
        at = rng.integers(0, len(pops) - 2000)
        n = rng.integers(100, 900)
        pop = highpass(noise(n / RATE), 1500) * decay(n / RATE, 120)
        pops[at:at + len(pop)] += pop * rng.uniform(0.2, 1.0)
    for _ in range(4):  # a beam snapping now and then
        at = rng.integers(0, len(pops) - 8000)
        crack = lowpass(noise(0.25), 1500) * decay(0.25, 18) * 2.5
        pops[at:at + len(crack)] += crack
    save("fire", make_loop(roar + pops), 0.5, cut=False)


def dig():
    # An entrenching tool biting into stony earth, and the spoil thrown aside
    n = noise(0.45)
    scrape = bandpass(n, 700, 3500) * decay(0.45, 9)
    thud = lowpass(noise(0.45), 220) * decay(0.45, 25) * 2.5
    stone = click(0.4, 2100)
    x = place(fade_in(scrape + thud), stone, 0.03)
    x = place(x, lowpass(noise(0.2), 1200) * decay(0.2, 14) * 0.6, 0.3)
    save("dig", x, 0.5)


if __name__ == "__main__":
    lee_enfield()
    krag()
    kar98k()
    bren()
    colt_m29()
    mg34()
    mp40()
    ricochet()
    mortar("mortar_light", 170, 0.8)  # 2-inch: small and quick
    mortar("mortar", 105, 1.0)        # 3-inch, 8 cm bombekaster, Granatwerfer 34
    explosion("blast_small", 1.2, 1.2, 900, 7, 50, 1.0, 25, 1.0, 0.4)    # mortar bombs
    explosion("blast_shell", 1.6, 1.0, 600, 5, 42, 1.6, 45, 1.4, 0.5)    # field and mountain guns
    explosion("blast_bomb", 2.8, 0.8, 380, 2.6, 32, 2.4, 90, 2.0, 0.6)   # aircraft bombs
    incoming_shell()
    incoming_mortar()
    incoming_bomb()
    distant_gun()
    aircraft()
    stuka_siren()
    fire()
    dig()
