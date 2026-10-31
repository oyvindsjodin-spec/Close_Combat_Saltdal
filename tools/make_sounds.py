"""
Makes the game's sound effects from scratch, so no recorded or borrowed sounds
are needed. Each sound is built from noise and simple tones, shaped with
envelopes and filters, and written as a 16-bit mono WAV in assets/sounds/.

    python3 tools/make_sounds.py

Change a number and run it again to try a different sound. Needs numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "sounds")
rng = np.random.default_rng(1940)


def samples(seconds):
    return int(round(seconds * RATE))


def t(seconds):
    return np.arange(samples(seconds)) / RATE


def noise(seconds):
    return rng.uniform(-1, 1, samples(seconds))


def lowpass(x, cutoff):
    """One-pole low-pass filter; cutoff in Hz."""
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def decay(seconds, rate):
    return np.exp(-t(seconds) * rate)


def attack(x, seconds=0.002):
    n = min(len(x), int(seconds * RATE))
    x[:n] *= np.linspace(0, 1, n)
    return x


def echo(x, delay, gain, count=3):
    """Echoes off the valley sides."""
    out = np.concatenate([x, np.zeros(int(delay * RATE * count))])
    for k in range(1, count + 1):
        d = int(delay * RATE * k)
        out[d:d + len(x)] += lowpass(x, 1500 / k) * gain ** k
    return out


def save(name, x, peak=0.9):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    print(f"{name}.wav  {len(x) / RATE:.2f} s")


def gunshot(length, crack_hz, body_hz, body_rate, echo_gain):
    """A sharp crack followed by a duller boom that dies away."""
    n = noise(length)
    crack = highpass(n, crack_hz) * decay(length, 60)
    body = lowpass(n, body_hz) * decay(length, body_rate) * 2.5
    return echo(attack(crack + body), 0.23, echo_gain)


def rifle():
    save("rifle", gunshot(0.45, 2500, 900, 14, 0.35))


def pistol():
    # Sub-machine gun: lighter, snappier
    save("smg", gunshot(0.18, 3000, 1400, 30, 0.2), 0.7)


def machine_gun():
    # One round of a machine gun; the game repeats it for a burst
    save("mg", gunshot(0.16, 2200, 700, 28, 0.15), 0.8)


def mortar_launch():
    # The hollow "thoomp" of a bomb leaving the tube
    x = t(0.5)
    tone = np.sin(2 * np.pi * (140 - 80 * x) * x) * decay(0.5, 9)
    puff = lowpass(noise(0.5), 500) * decay(0.5, 18) * 2
    save("mortar", attack(tone + puff), 0.8)


def explosion(name, length, body_hz, rate, size):
    n = noise(length)
    boom = lowpass(n, body_hz) * decay(length, rate) * 3
    thud = np.sin(2 * np.pi * 45 * t(length)) * decay(length, rate * 1.5) * size
    debris = highpass(noise(length), 1800) * decay(length, rate * 2.5) * 0.4
    save(name, echo(attack(boom + thud + debris, 0.004), 0.31, 0.45, 4))


def aircraft():
    # A piston engine droning past: a loop the game fades in and out
    x = t(4.0)
    hum = (np.sin(2 * np.pi * 92 * x) + 0.6 * np.sin(2 * np.pi * 184 * x + 0.3)
           + 0.3 * np.sin(2 * np.pi * 276 * x)) * (0.75 + 0.25 * np.sin(2 * np.pi * 23 * x))
    rough = lowpass(noise(4.0), 400) * 1.5
    y = hum + rough
    fade = int(0.05 * RATE)  # smooth the loop's seam
    y[:fade] *= np.linspace(0, 1, fade)
    y[-fade:] *= np.linspace(1, 0, fade)
    save("aircraft", y, 0.6)


def fire():
    # Crackling flames: a low roar with random pops, made to loop
    length = 4.0
    roar = lowpass(noise(length), 250) * 2
    pops = np.zeros(int(length * RATE))
    for _ in range(70):
        at = rng.integers(0, len(pops) - 800)
        n = rng.integers(80, 600)
        pop = highpass(noise(n / RATE), 1500) * decay(n / RATE, 120)
        pops[at:at + len(pop)] += pop * rng.uniform(0.3, 1.0)
    y = roar + pops
    fade = int(0.08 * RATE)
    y[:fade] *= np.linspace(0, 1, fade)
    y[-fade:] *= np.linspace(1, 0, fade)
    save("fire", y, 0.5)


def dig():
    # A spade biting into earth
    n = noise(0.35)
    scrape = lowpass(highpass(n, 600), 3000) * decay(0.35, 10)
    thud = lowpass(noise(0.35), 200) * decay(0.35, 25) * 2
    save("dig", attack(scrape + thud), 0.5)


def whistle():
    # Incoming shell: a falling whistle, played just before it lands
    x = t(1.2)
    freq = 1400 - 700 * x / 1.2
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    y = np.sin(phase) * np.minimum(1, x * 3) * (0.4 + 0.6 * x / 1.2)
    save("incoming", y, 0.35)


if __name__ == "__main__":
    rifle()
    pistol()
    machine_gun()
    mortar_launch()
    explosion("blast_small", 1.0, 600, 7, 0.8)     # mortar bombs
    explosion("blast_shell", 1.4, 450, 5, 1.2)     # field and mountain guns
    explosion("blast_bomb", 2.4, 300, 3, 2.0)      # aircraft bombs
    aircraft()
    fire()
    dig()
    whistle()
