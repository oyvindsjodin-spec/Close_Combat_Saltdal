#!/usr/bin/env python3
"""Render the soldier sprites, the way the 1990s wargames did: small figures
pre-rendered from 3D shapes, lit from the north-west like the painted ground,
seen from above and a little from the south, with a soft shadow and a dark
edge so they read against grass and fields.

Every man is built from round shapes (ellipsoids and chains of spheres for
arms, legs and weapons) and ray-cast at four times the final size, then
scaled down. One sprite is made for each uniform, weapon, pose and one of 16
directions, all packed into one picture:

    data/sprites/soldiers.png    the sprites, 48 x 48 pixels each
    data/sprites/soldiers.json   where each one is

Usage:
    python3 tools/render_soldiers.py            (all, a few minutes)
    python3 tools/render_soldiers.py --preview FILE   (a sheet to look at)

Needs numpy, scipy and Pillow.
"""
import argparse, json, math, os
from multiprocessing import Pool

import numpy as np
from PIL import Image
from scipy import ndimage

SIZE = 64            # final sprite, pixels
METRES = 3.2         # the sprite covers this many metres square
CENTRE_W = -0.35     # the sprite centre sits this far up the screen from the man's feet (metres)
SS = 3               # supersampling
DIRS = 16
TILT = math.radians(18)               # seen a little from the south
LIGHT = np.array([-1.0, -1.0, 2.6])   # from the north-west and above (x east, y south, z up)
LIGHT /= np.linalg.norm(LIGHT)

UNIFORMS = {
    # cloth, webbing/kit, helmet, helmet shape, trousers
    "british":   dict(cloth=(0.56, 0.45, 0.28), kit=(0.62, 0.58, 0.41), helmet=(0.38, 0.38, 0.25), brim=True, legs=(0.50, 0.40, 0.25)),
    "german":    dict(cloth=(0.42, 0.46, 0.40), kit=(0.24, 0.20, 0.15), helmet=(0.31, 0.34, 0.30), brim=False, legs=(0.36, 0.38, 0.35)),
    "norwegian": dict(cloth=(0.40, 0.43, 0.33), kit=(0.45, 0.38, 0.26), helmet=(0.34, 0.37, 0.30), brim=False, legs=(0.36, 0.38, 0.30)),
}
WEAPONS = ["rifle", "smg", "mg"]
POSES = ["stand", "crouch", "prone", "dead"] + [f"walk{i}" for i in range(8)] + [f"crawl{i}" for i in range(4)]
SKIN = (0.82, 0.63, 0.50)
BOOT = (0.14, 0.11, 0.09)
WOOD = (0.40, 0.26, 0.14)
METAL = (0.16, 0.16, 0.15)


class Body:
    """Shapes in the man's own frame: x forward, y to his left, z up, metres."""

    def __init__(self):
        self.parts = []   # (centre, radii, colour)

    def ell(self, c, r, col):
        self.parts.append((np.array(c, float), np.array(r, float), np.array(col, float)))

    def limb(self, a, b, r0, r1, col, n=None):
        a, b = np.array(a, float), np.array(b, float)
        n = n or max(3, int(np.linalg.norm(b - a) / (min(r0, r1) * 0.9)) + 1)
        for i in range(n):
            t = i / (n - 1)
            r = r0 + (r1 - r0) * t
            self.ell(a + (b - a) * t, (r, r, r), col)


def weapon(b, kind, grip, muzzle_dir, height_drop=0.0):
    """A rifle, sub-machine gun or light machine gun from the grip forward."""
    g = np.array(grip, float)
    d = np.array(muzzle_dir, float); d /= np.linalg.norm(d)
    if kind == "rifle":
        b.limb(g - d * 0.30, g + d * 0.35, 0.04, 0.032, WOOD)
        b.limb(g + d * 0.35, g + d * 0.80, 0.024, 0.02, METAL)
    elif kind == "smg":
        b.limb(g - d * 0.05, g + d * 0.45, 0.035, 0.026, METAL)
        b.ell(g + d * 0.15 + np.array([0, 0, -0.08]), (0.02, 0.02, 0.07), METAL)
    else:
        b.limb(g - d * 0.30, g + d * 0.10, 0.045, 0.04, WOOD)
        b.limb(g + d * 0.10, g + d * 0.95, 0.034, 0.026, METAL)
        b.ell(g + d * 0.12 + np.array([0, 0, 0.06]), (0.07, 0.04, 0.05), METAL)   # magazine or feed cover


def helmet(b, u, head):
    h = np.array(head, float)
    if u["brim"]:
        # The British Brodie: a wide flat brim round a shallow bowl
        b.ell(h + [0, 0, 0.06], (0.19, 0.19, 0.035), u["helmet"])
        b.ell(h + [0, 0, 0.09], (0.12, 0.12, 0.07), u["helmet"])
    else:
        # The German Stahlhelm and the Norwegian helmet: a deep bowl, flared at the back
        b.ell(h + [-0.02, 0, 0.05], (0.15, 0.135, 0.10), u["helmet"])
        b.ell(h + [-0.07, 0, 0.0], (0.10, 0.14, 0.06), u["helmet"])


def build(uniform, wkind, pose):
    u = UNIFORMS[uniform]
    b = Body()
    if pose in ("stand", "crouch") or pose.startswith("walk"):
        crouch = pose == "crouch"
        phase = int(pose[4:]) / 8 * math.tau if pose.startswith("walk") else None
        dz = -0.45 if crouch else 0.0
        lean = 0.12 if crouch else 0.04
        swing = 0.28 * math.sin(phase) if phase is not None else 0.0
        lift = 0.06 * max(0.0, math.cos(phase)) if phase is not None else 0.0
        hip = 0.92 + dz
        # Legs and boots
        for side, s in ((1, swing), (-1, -swing)):
            y = 0.10 * side
            if crouch:
                knee = (0.30 if side > 0 else -0.05, y, 0.42)
                foot = (0.12 if side > 0 else -0.30, y, 0.06)
            else:
                knee = (s * 0.55, y, 0.50 + (lift if s > 0 else 0))
                foot = (s, y, 0.06 + (lift if s > 0 else 0))
            b.limb((0, y, hip), knee, 0.09, 0.075, u["legs"])
            b.limb(knee, foot, 0.075, 0.06, u["legs"])
            b.ell(np.array(foot) + [0.06, 0, -0.01], (0.12, 0.05, 0.05), BOOT)
        # Body, pack and webbing
        t = np.array([lean, 0, 1.25 + dz])
        b.ell(t, (0.13, 0.22, 0.30), u["cloth"])
        b.ell(t + [-0.17, 0, 0.05], (0.09, 0.16, 0.17), u["kit"])
        b.ell(t + [0.10, 0.12, -0.12], (0.05, 0.06, 0.06), u["kit"])
        b.ell(t + [0.10, -0.12, -0.12], (0.05, 0.06, 0.06), u["kit"])
        head = t + [0.05, 0, 0.40]
        b.ell(head, (0.10, 0.09, 0.11), SKIN)
        helmet(b, u, head)
        # Weapon held at the ready, pointing forward; arms to it
        grip = t + [0.22, -0.10, 0.02]
        weapon(b, wkind, grip, (1.0, 0.05, 0.12))
        arm_swing = -swing * 0.5 if wkind == "none" else 0.0
        for side in (1, -1):
            sh = t + [0.0, 0.21 * side, 0.20]
            hand = grip + ([0.35, 0.10, 0.04] if side > 0 else [0.0, 0.0, 0.0])
            elbow = (sh + hand) / 2 + [0.0, 0.10 * side, -0.12]
            b.limb(sh, elbow, 0.055, 0.05, u["cloth"])
            b.limb(elbow, hand + [arm_swing, 0, 0], 0.05, 0.045, u["cloth"])
            b.ell(hand, (0.04, 0.04, 0.04), SKIN)
    else:
        # Lying down, stretched out along x, head forward
        dead = pose == "dead"
        k = int(pose[5:]) / 4 * math.tau if pose.startswith("crawl") else None
        c = 0.12 * math.sin(k) if k is not None else 0.0
        z = 0.14
        b.ell((0.15, 0, z + 0.02), (0.34, 0.23, 0.14), u["cloth"])
        b.ell((0.02, 0, z + 0.13), (0.14, 0.13, 0.07), u["kit"])
        spread = 0.24 if dead else 0.13
        for side, s in ((1, c), (-1, -c)):
            hip = (-0.18, 0.09 * side, z)
            knee = (-0.55 + s, (spread * 0.8) * side, z - 0.03)
            foot = (-0.95 + s * 1.3, spread * side, z - 0.04)
            b.limb(hip, knee, 0.095, 0.08, u["legs"])
            b.limb(knee, foot, 0.08, 0.065, u["legs"])
            b.ell(np.array(foot) + [-0.06, 0, 0.0], (0.07, 0.06, 0.10), BOOT)
        head = np.array([0.58, 0.0 if not dead else 0.06, z + 0.08])
        b.ell(head, (0.10, 0.09, 0.10), SKIN)
        helmet(b, u, head + [0.02, 0, 0.0])
        if dead:
            for side in (1, -1):
                sh = (0.40, 0.20 * side, z)
                b.limb(sh, (0.55, 0.50 * side, z - 0.05), 0.055, 0.045, u["cloth"])
                b.ell((0.58, 0.55 * side, z - 0.06), (0.04, 0.04, 0.03), SKIN)
        else:
            grip = np.array([0.72, -0.10, z + 0.02])
            weapon(b, wkind, grip, (1.0, 0.02, 0.03))
            for side, s in ((1, -c), (-1, c)):
                sh = np.array((0.40, 0.20 * side, z + 0.04))
                hand = grip + ([0.32 + s, 0.10, 0.0] if side > 0 else [s, 0, 0])
                elbow = np.array((0.55 + s * 0.5, 0.30 * side, z - 0.04))
                b.limb(sh, elbow, 0.055, 0.05, u["cloth"])
                b.limb(elbow, hand, 0.05, 0.045, u["cloth"])
                b.ell(hand, (0.04, 0.04, 0.04), SKIN)
            if wkind == "mg":
                # Bipod legs down to the ground
                tip = grip + [0.75, 0.02, 0.0]
                b.limb(tip, tip + [0.05, 0.12, -0.15], 0.012, 0.012, METAL)
                b.limb(tip, tip + [0.05, -0.12, -0.15], 0.012, 0.012, METAL)
    return b


def render(body, angle):
    """Ray-cast one sprite: RGBA float array SIZE x SIZE."""
    n = SIZE * SS
    px = METRES / n
    # Screen: u east, w down the screen; a point (X, Y, Z) shows at w = Y cos t - Z sin t
    us = (np.arange(n) + 0.5) * px - METRES / 2
    ws = (np.arange(n) + 0.5) * px - METRES / 2 + CENTRE_W   # men stand a little below the centre
    U, Wd = np.meshgrid(us, ws)
    ct, st = math.cos(TILT), math.sin(TILT)
    # Looking north and down; rays start well above the man
    D = np.array([0.0, -st, -ct])
    O = np.stack([U, Wd * ct, -Wd * st], -1) - 3 * D
    # Into the man's frame: rotate by -angle about z (his forward is (cos a, sin a) on screen)
    ca, sa = math.cos(angle), math.sin(angle)
    # Screen frame has y pointing south; the man's left is (sin a, -cos a)
    def to_man(v):
        x = v[..., 0] * ca + v[..., 1] * sa
        y = v[..., 0] * sa - v[..., 1] * ca
        return np.stack([x, y, v[..., 2]], -1)
    def to_screen(v):
        X = v[..., 0] * ca + v[..., 1] * sa
        Y = v[..., 0] * sa - v[..., 1] * ca
        return np.stack([X, Y, v[..., 2]], -1)
    Om, Dm = to_man(O), to_man(D)
    best = np.full((n, n), np.inf)
    col = np.zeros((n, n, 3))
    nor = np.zeros((n, n, 3))
    for c, r, colour in body.parts:
        o = (Om - c) / r
        d = Dm / r
        a = (d * d).sum(-1)
        bq = 2 * (o * d).sum(-1)
        cq = (o * o).sum(-1) - 1
        disc = bq * bq - 4 * a * cq
        hit = disc > 0
        t = np.where(hit, (-bq - np.sqrt(np.maximum(disc, 0))) / (2 * a), np.inf)
        closer = hit & (t > 0) & (t < best)
        if not closer.any():
            continue
        best[closer] = t[closer]
        p = o[closer] + d * t[closer][:, None]
        nn = p / r
        nor[closer] = nn / np.linalg.norm(nn, axis=-1, keepdims=True)
        col[closer] = colour
    mask = np.isfinite(best)
    # Light: the normal back on the screen frame
    ns = to_screen(nor)
    lam = np.clip((ns * LIGHT).sum(-1), 0, 1)
    shade = 0.42 + 0.75 * lam
    rgb = col * shade[..., None]
    # Shadow on the ground, cast away from the light
    ground_y = Wd / ct
    G = np.stack([U, ground_y, np.zeros_like(U)], -1)
    Gm = to_man(G)
    Lm = to_man(LIGHT)
    shadow = np.zeros((n, n), bool)
    for c, r, _ in body.parts:
        o = (Gm - c) / r
        d = Lm / r
        a = (d * d).sum()
        bq = 2 * (o * d).sum(-1)
        cq = (o * o).sum(-1) - 1
        disc = bq * bq - 4 * a * cq
        t = (-bq + np.sqrt(np.maximum(disc, 0))) / (2 * a)
        shadow |= (disc > 0) & (t > 0)
    img = np.zeros((n, n, 4))
    img[..., 3] = np.where(shadow, 0.38, 0.0)
    img[mask, :3] = rgb[mask]
    img[mask, 3] = 1.0
    # Down to the final size
    small = img.reshape(SIZE, SS, SIZE, SS, 4)
    a = small[..., 3].mean(axis=(1, 3))
    body_a = mask.reshape(SIZE, SS, SIZE, SS).mean(axis=(1, 3))
    rgb_s = (small[..., :3] * mask.reshape(SIZE, SS, SIZE, SS, 1)).sum(axis=(1, 3)) / np.maximum(mask.reshape(SIZE, SS, SIZE, SS).sum(axis=(1, 3)), 1)[..., None]
    shadow_a = np.where(shadow, 0.38, 0.0).reshape(SIZE, SS, SIZE, SS).mean(axis=(1, 3))
    shadow_a = ndimage.gaussian_filter(shadow_a, 0.6)
    # A dark edge round the figure, as the old sprites had
    edge = np.clip(ndimage.maximum_filter(body_a, size=3) - body_a, 0, 1) * 0.8
    out = np.zeros((SIZE, SIZE, 4))
    under_a = np.maximum(shadow_a, edge)
    under_rgb = np.where((edge > shadow_a)[..., None], np.array([0.07, 0.06, 0.05]), 0.0)
    out[..., :3] = rgb_s * body_a[..., None] + under_rgb * (1 - body_a[..., None])
    out[..., 3] = body_a + under_a * (1 - body_a)
    out[..., :3] = np.where(out[..., 3:4] > 0, out[..., :3] / np.maximum(out[..., 3:4], 1e-6), 0)
    return out


def render_all_directions(key):
    body = build(*key)
    return np.concatenate([render(body, d / DIRS * math.tau) for d in range(DIRS)], 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview")
    a = ap.parse_args()
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
    if a.preview:
        rows = []
        for uni in UNIFORMS:
            for pose in ["stand", "walk2", "crouch", "prone", "dead"]:
                row = [render(build(uni, "rifle" if uni != "german" else "mg", pose), i / 4 * math.tau + 0.4) for i in range(4)]
                rows.append(np.concatenate(row, 1))
        sheet = np.concatenate(rows, 0)
        bg = np.array([0.45, 0.45, 0.30])
        rgb = sheet[..., :3] * sheet[..., 3:4] + bg * (1 - sheet[..., 3:4])
        Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).resize((sheet.shape[1] * 3, sheet.shape[0] * 3), Image.NEAREST).save(a.preview)
        return
    keys = [(u, w, p) for u in UNIFORMS for w in WEAPONS for p in POSES]
    cols = DIRS * 3     # three poses of 16 directions per row
    rows = math.ceil(len(keys) / 3)
    atlas = np.zeros((rows * SIZE, cols * SIZE, 4))
    index = {}
    with Pool() as pool:
        for i, strip in enumerate(pool.imap(render_all_directions, keys)):
            u, w, p = keys[i]
            r, c0 = i // 3, (i % 3) * DIRS
            index[f"{u}/{w}/{p}"] = [r, c0]
            atlas[r * SIZE:(r + 1) * SIZE, c0 * SIZE:(c0 + DIRS) * SIZE] = strip
            print(f"\r{i + 1}/{len(keys)}", end="", flush=True)
    print()
    out = os.path.join(root, "data", "sprites")
    os.makedirs(out, exist_ok=True)
    Image.fromarray((np.clip(atlas, 0, 1) * 255).astype(np.uint8), "RGBA").save(os.path.join(out, "soldiers.png"), optimize=True)
    json.dump({"size": SIZE, "metres": METRES, "centreW": CENTRE_W, "tilt": math.degrees(TILT), "directions": DIRS, "sprites": index},
              open(os.path.join(out, "soldiers.json"), "w"), indent=1)
    print("wrote", out)


if __name__ == "__main__":
    main()
