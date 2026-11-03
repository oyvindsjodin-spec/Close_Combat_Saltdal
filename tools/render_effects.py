#!/usr/bin/env python3
"""Paint the battle effects: smoke puffs, fireballs, flames, the spray of
earth thrown up by a shell, muzzle flashes and a soft glow. Each picture is
made from layered noise and shaded with light from the north-west, like the
painted ground and the soldiers, so the effects look painted rather than
drawn with flat circles.

Most are drawn in white or grey so the game can tint them (brown dust,
black oily smoke, pale gun smoke). Fire and flashes carry their own colours.

    data/sprites/effects.png    the pictures, 128 x 128 pixels each
    data/sprites/effects.json   where each one is

Usage:
    python3 tools/render_effects.py
    python3 tools/render_effects.py --preview FILE   (a sheet on a grass colour to look at)

Needs numpy, scipy and Pillow.
"""
import argparse, json, math, os

import numpy as np
from PIL import Image
from scipy import ndimage

CELL = 128
COLS = 8
SS = 2          # painted at twice the size, then scaled down
N = CELL * SS
OUT = os.path.join(os.path.dirname(__file__), "..", "data", "sprites")

yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
U = (xx + 0.5) / N * 2 - 1     # -1 .. 1, east
V = (yy + 0.5) / N * 2 - 1     # -1 .. 1, south
R = np.hypot(U, V)
A = np.arctan2(V, U)


def noise(rng, octaves=5, base=4, persistence=0.55):
    """Fractal value noise, 0..1, tiling-free, N x N."""
    out = np.zeros((N, N), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        g = base * 2 ** o
        grid = rng.random((g + 1, g + 1)).astype(np.float32)
        out += amp * ndimage.zoom(grid, N / (g + 1), order=3, mode="nearest")[:N, :N]
        total += amp
        amp *= persistence
    out /= total
    out -= out.min()
    return out / max(out.max(), 1e-6)


def noise_tall(rng, octaves=5, base=3, persistence=0.55):
    """Like noise(), but repeating seamlessly top to bottom, for scrolling."""
    out = np.zeros((N, N), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        g = base * 2 ** o
        grid = rng.random((g, g + 1)).astype(np.float32)
        big = ndimage.zoom(np.vstack([grid] * 3), (3 * N / (3 * g), N / (g + 1)), order=3, mode="grid-wrap")
        out += amp * big[N:2 * N, :N]
        total += amp
        amp *= persistence
    out /= total
    out -= out.min()
    return out / max(out.max(), 1e-6)


def smoothstep(a, b, x):
    d = np.where(np.abs(b - a) < 1e-6, 1e-6, b - a)
    t = np.clip((x - a) / d, 0, 1)
    return t * t * (3 - 2 * t)


def billow(rng, lumps=7, spread=0.35, lump_size=(0.28, 0.45)):
    """A cauliflower-shaped cloud: overlapping round lumps. Returns its height field."""
    h = np.zeros((N, N), np.float32)
    for i in range(lumps):
        a = rng.random() * math.tau
        d = spread * math.sqrt(rng.random()) if i else 0.0
        r = rng.uniform(*lump_size) * (1.25 if i == 0 else 1.0)
        cx, cy = math.cos(a) * d, math.sin(a) * d
        dd = np.hypot(U - cx, V - cy) / r
        h = np.maximum(h, np.sqrt(np.clip(1 - dd * dd, 0, 1)))
    return h


def shade(height, strength=1.0):
    """Light from the north-west and above on a height field: 0 (dark) .. 1 (lit)."""
    gy, gx = np.gradient(ndimage.gaussian_filter(height, 2 * SS))
    nx, ny, nz = -gx * N * 0.12 * strength, -gy * N * 0.12 * strength, np.ones_like(height)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    light = np.array([-1.0, -1.0, 1.3])
    light /= np.linalg.norm(light)
    lit = (nx * light[0] + ny * light[1] + nz * light[2]) / n
    return np.clip(lit, 0, 1)


def to_cell(rgb, alpha):
    img = np.dstack([np.clip(rgb, 0, 1), np.clip(alpha, 0, 1)[..., None]])
    im = Image.fromarray((img * 255).astype(np.uint8), "RGBA")
    return im.resize((CELL, CELL), Image.LANCZOS)


def grey(v):
    return np.dstack([v, v, v])


# ------------------------------------------------------------------ the pictures

def smoke(seed):
    """A puff of smoke, grey and white, darker on the south-east side and at its heart."""
    rng = np.random.default_rng(seed)
    h = billow(rng, lumps=rng.integers(6, 10), spread=0.38, lump_size=(0.22, 0.4))
    n = noise(rng, octaves=6, base=3)
    fine = noise(rng, octaves=4, base=12)
    h = ndimage.gaussian_filter(h, 3 * SS)
    dens = h * (0.45 + 0.75 * n) - 0.2 * fine
    alpha = smoothstep(0.05, 0.55, dens) * smoothstep(1.0, 0.75, R)
    lit = shade(h * 0.7 + n * 0.6 + 0.15 * fine, 1.1)
    v = 0.42 + 0.5 * lit + 0.1 * (fine - 0.5)
    # Wisps: a faint ragged fringe beyond the body
    wisp = smoothstep(0.55, 0.9, n) * smoothstep(0.98, 0.5, R) * 0.35
    alpha = np.maximum(alpha, wisp)
    return to_cell(grey(v), alpha * 0.95)


def fireball(seed):
    """A ball of burning gas: white-yellow heart, orange and red rolls, sooty edges."""
    rng = np.random.default_rng(seed)
    h = billow(rng, lumps=rng.integers(7, 11), spread=0.32, lump_size=(0.2, 0.36))
    n = noise(rng, octaves=6, base=4)
    heat = np.clip(h * (0.7 + 0.5 * n) - 0.05, 0, 1)
    lit = shade(h + 0.4 * n, 1.2)
    ramp = np.array([
        [0.10, 0.05, 0.03],   # soot
        [0.45, 0.10, 0.03],   # dark red
        [0.85, 0.30, 0.05],   # orange
        [1.00, 0.62, 0.15],   # yellow-orange
        [1.00, 0.90, 0.55],   # yellow
        [1.00, 1.00, 0.88],   # white
    ])
    t = np.clip(heat * 0.8 - 0.05 + 0.25 * (lit - 0.5) + 0.25 * (n - 0.5), 0, 1) * (len(ramp) - 1)
    i = np.clip(t.astype(int), 0, len(ramp) - 2)
    f = (t - i)[..., None]
    rgb = ramp[i] * (1 - f) + ramp[i + 1] * f
    rgb *= (0.75 + 0.35 * lit)[..., None]
    alpha = smoothstep(0.04, 0.35, h * (0.6 + 0.6 * n)) * smoothstep(1.0, 0.85, R)
    return to_cell(rgb, alpha)


def flame(seed, frame, frames):
    """A tongue of flame pointing up the picture (north), its base at the centre.
    The frames scroll the noise upward so the tongue licks and flickers."""
    rng = np.random.default_rng(seed)
    n = np.roll(noise_tall(rng, octaves=5, base=3), -int(frame / frames * N), axis=0)
    rng2 = np.random.default_rng(seed + 100 + frame)
    fine = noise(rng2, octaves=3, base=10)
    up = -V                                # 1 at the top
    # Height along the tongue, 0 at the base (centre of the cell) to 1 at the tip
    along = np.clip(up / 0.95, -0.4, 1)
    base = np.sqrt(np.clip(1 - (np.minimum(along, 0) / 0.32) ** 2, 0, 1))
    width = 0.3 * np.sqrt(np.clip(1 - along, 0, 1)) * base * (0.85 + 0.3 * n)
    sway = (n - 0.5) * 0.6 * np.clip(along + 0.1, 0, 1) ** 1.3
    x = (U - sway) / np.maximum(width, 1e-3)
    body = np.clip(1 - x * x, 0, 1) * (along > -0.32)
    body *= smoothstep(1.0, 0.55, along + 0.35 * (fine - 0.5) + 0.3 * (n - 0.5))
    core = body * body * smoothstep(0.55, 0.0, along + 0.25 * n)
    hot = body * smoothstep(1.0, 0.2, along + 0.3 * (n - 0.5))
    rgb = np.dstack([
        0.62 + 0.38 * hot,
        0.12 + 0.48 * hot + 0.32 * core,
        0.02 + 0.08 * hot + 0.55 * core * core,
    ])
    alpha = smoothstep(0.02, 0.35, body)
    return to_cell(rgb, alpha)


def burst(seed):
    """Earth thrown out by a shell, seen from above: dark streaks and clods
    radiating from the hole, thickest near the middle."""
    rng = np.random.default_rng(seed)
    rays = np.zeros(N, np.float32)
    k = rng.integers(14, 22)
    ang = np.linspace(-math.pi, math.pi, N, endpoint=False)
    for _ in range(k):
        a0 = rng.random() * math.tau - math.pi
        w = rng.uniform(0.05, 0.16)
        L = rng.uniform(0.55, 1.0)
        d = np.angle(np.exp(1j * (ang - a0)))
        rays = np.maximum(rays, L * np.exp(-(d / w) ** 2))
    reach = np.interp(A.ravel(), ang, rays, period=math.tau).reshape(N, N)
    reach = 0.42 + 0.55 * reach
    n = noise(rng, octaves=6, base=6)
    dens = smoothstep(reach, reach * 0.35, R + 0.25 * (n - 0.5))
    # Clods scattered along the rays
    clods = np.zeros((N, N), np.float32)
    for _ in range(90):
        a = rng.random() * math.tau
        r = rng.uniform(0.15, 0.95)
        cx, cy = math.cos(a) * r, math.sin(a) * r
        s = rng.uniform(0.012, 0.035)
        clods = np.maximum(clods, np.clip(1 - np.hypot(U - cx, V - cy) / s, 0, 1) * 1.5)
    alpha = np.clip(dens * (0.8 + 0.6 * n) + clods, 0, 1) * smoothstep(1.0, 0.9, R)
    h = dens + 0.6 * clods + 0.3 * n
    lit = shade(h, 2.0)
    v = 0.35 + 0.65 * lit
    rgb = np.dstack([v * 1.0, v * 0.86, v * 0.68])   # earthy; tinted further in the game
    return to_cell(rgb, alpha)


def muzzle(seed):
    """A muzzle flash pointing east: a bright cone forward and two small side jets."""
    rng = np.random.default_rng(seed)
    n = noise(rng, octaves=4, base=6)
    m = np.zeros((N, N), np.float32)
    lobes = [(0.0, 0.9, 0.3), (rng.uniform(0.7, 1.1), 0.45, 0.22), (-rng.uniform(0.7, 1.1), 0.45, 0.22)]
    for a0, L, w in lobes:
        d = np.angle(np.exp(1j * (A - a0)))
        r_edge = L * np.exp(-(d / w) ** 2) * (0.75 + 0.5 * n)
        lobe = np.clip(1 - R / np.maximum(r_edge, 1e-4), 0, 1) ** 0.7
        m = np.maximum(m, lobe * (np.abs(d) < 1.2))
    m = np.maximum(m, smoothstep(0.28, 0.0, R))
    core = smoothstep(0.35, 0.95, m)
    rgb = np.dstack([np.ones_like(m), 0.55 + 0.45 * core, 0.15 + 0.75 * core])
    return to_cell(rgb, m)


def glow():
    a = np.exp(-(R * 2.2) ** 2) * smoothstep(1.0, 0.85, R)
    return to_cell(grey(np.ones_like(R)), a)


def build():
    cells = []                                  # (name, image)
    for i in range(6): cells.append((f"smoke{i}", smoke(10 + i)))
    for i in range(4): cells.append((f"fireball{i}", fireball(30 + i)))
    for i in range(8): cells.append((f"flame{i}", flame(50, i, 8)))
    for i in range(8): cells.append((f"flameb{i}", flame(51, i, 8)))
    for i in range(3): cells.append((f"burst{i}", burst(70 + i)))
    for i in range(3): cells.append((f"muzzle{i}", muzzle(90 + i)))
    cells.append(("glow", glow()))
    rows = (len(cells) + COLS - 1) // COLS
    sheet = Image.new("RGBA", (COLS * CELL, rows * CELL), (0, 0, 0, 0))
    index = {}
    for k, (name, im) in enumerate(cells):
        r, c = divmod(k, COLS)
        sheet.paste(im, (c * CELL, r * CELL))
        index[name] = [r, c]
    return sheet, {"cell": CELL, "sprites": index}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview")
    args = ap.parse_args()
    sheet, meta = build()
    if args.preview:
        bg = Image.new("RGBA", sheet.size, (92, 104, 58, 255))
        bg.alpha_composite(sheet)
        bg.convert("RGB").save(args.preview)
        return
    os.makedirs(OUT, exist_ok=True)
    sheet.save(os.path.join(OUT, "effects.png"), optimize=True)
    with open(os.path.join(OUT, "effects.json"), "w") as f:
        json.dump(meta, f, indent=1)
    print("wrote", os.path.join(OUT, "effects.png"))


if __name__ == "__main__":
    main()
