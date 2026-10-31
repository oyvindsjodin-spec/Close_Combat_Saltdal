#!/usr/bin/env python3
"""Paint the ground picture for a battle map.

Reads a map written by build_map.py (height and terrain per 4 m cell) and
paints a detailed overhead picture of it at 2 pixels per metre: grass and
meadow with grain, ploughed fields with furrows, dirt roads, water that darkens
away from the bank, rock and scree, snow, individual trees with their shadows,
and houses with pitched roofs. Light comes from the north-west.

Writes data/maps/<name>/ground.jpg, which the game draws under the soldiers.

Usage:
    python3 tools/paint_ground.py pothus viskiskoia rognan

Needs numpy, scipy and Pillow.
"""
import json, math, os, sys

import numpy as np
from PIL import Image
from scipy import ndimage

PX_PER_M = 2
# Must match the TerrainType enum in src/Sim/TerrainType.cs
GRASS, FOREST, ROCK, SNOW, WATER, ROAD, BRIDGE, FIELD, BUILDING, FORD, WRECK = range(11)


def noise(shape, scale_px, rng, octaves=3):
    """Smooth fractal noise in roughly [-1, 1], features about scale_px across."""
    h, w = shape
    total = np.zeros(shape, np.float32)
    amp, norm = 1.0, 0.0
    s = scale_px
    for _ in range(octaves):
        gh, gw = max(2, int(h / s) + 2), max(2, int(w / s) + 2)
        coarse = rng.standard_normal((gh, gw)).astype(np.float32)
        total += amp * ndimage.zoom(coarse, (h / gh, w / gw), order=3)[:h, :w]
        norm += amp
        amp *= 0.5
        s = max(2, s / 2)
    return total / norm


def tint(base, *layers):
    """base: (3,) colour; layers: (array, (3,) colour delta) pairs."""
    return base


def paint(name, root):
    folder = os.path.join(root, name)
    meta = json.load(open(os.path.join(folder, "map.json")))
    W, H, cell = meta["width"], meta["height"], meta["cellSize"]
    hts = np.fromfile(os.path.join(folder, "height.bin"), "<f4").reshape(H, W)
    ter = np.fromfile(os.path.join(folder, "terrain.bin"), np.uint8).reshape(H, W)
    rng = np.random.default_rng(abs(hash(name)) % (2 ** 32))
    up = int(cell * PX_PER_M)            # pixels per cell
    ph, pw = H * up, W * up
    print(f"{name}: painting {pw}x{ph} px")

    # Terrain per pixel. Edges between natural ground types are wobbled so they
    # do not follow the 4 m grid; roads, houses and water keep their shape.
    yy, xx = np.mgrid[0:ph, 0:pw].astype(np.float32)
    wob = 0.9 * up
    wx = noise((ph, pw), 18, rng, 2) * wob
    wy = noise((ph, pw), 18, rng, 2) * wob
    ci = np.clip(((yy + wy) / up).astype(int), 0, H - 1)
    cj = np.clip(((xx + wx) / up).astype(int), 0, W - 1)
    t_wobbly = ter[ci, cj]
    t_exact = np.repeat(np.repeat(ter, up, 0), up, 1)
    t = t_wobbly.copy()
    t[np.isin(t, [ROAD, BRIDGE, BUILDING, WRECK, WATER, FORD])] = GRASS

    def smooth(cells, sigma, level):
        """A cell mask with rounded, anti-stairstep edges at pixel level."""
        m = ndimage.zoom(cells.astype(np.float32), up, order=1)[:ph, :pw]
        return ndimage.gaussian_filter(m, sigma) > level

    wet_cells = np.isin(ter, [WATER, FORD, BRIDGE, WRECK])
    water_px = smooth(wet_cells, up * 0.45, 0.5)
    t[water_px] = WATER
    t[water_px & (t_exact == FORD)] = FORD
    road_cells = np.isin(ter, [ROAD, BRIDGE, WRECK])
    road_px = smooth(road_cells, up * 0.35, 0.38)
    crossing = np.isin(t_exact, [BRIDGE, WRECK])
    crossing = ndimage.binary_dilation(crossing, iterations=up // 2)
    t[road_px & ~water_px] = ROAD
    on_water = road_px & water_px & crossing
    t[on_water & ndimage.binary_dilation(t_exact == WRECK, iterations=up // 2)] = WRECK
    t[on_water & (t != WRECK)] = BRIDGE
    t[t_exact == BUILDING] = BUILDING
    del yy, xx, wx, wy, ci, cj, t_wobbly

    img = np.zeros((ph, pw, 3), np.float32)
    grain = rng.standard_normal((ph, pw)).astype(np.float32)
    mid = noise((ph, pw), 60, rng)
    big = noise((ph, pw), 220, rng, 2)

    def fill(mask, base, var_mid, var_grain, var_big=(0, 0, 0)):
        base = np.array(base, np.float32)
        col = (base[None, :]
               + mid[mask][:, None] * np.array(var_mid, np.float32)[None, :]
               + grain[mask][:, None] * np.array(var_grain, np.float32)[None, :]
               + big[mask][:, None] * np.array(var_big, np.float32)[None, :])
        img[mask] = col

    # Grass and meadow: green with yellow, dry patches
    fill(t == GRASS, (104, 118, 62), (14, 12, 4), (7, 8, 5), (16, 10, -4))
    # Forest floor: dark moss, mostly hidden under the trees
    fill(t == FOREST, (62, 74, 40), (8, 9, 4), (6, 6, 4))
    # Rock and scree: grey with cracks and stones
    fill(t == ROCK, (122, 118, 108), (16, 15, 13), (16, 16, 15), (10, 10, 8))
    # Snow: bright, faintly blue in the hollows
    fill(t == SNOW, (226, 230, 234), (8, 8, 6), (3, 3, 3))
    # Dirt roads: worn yellow-brown with ruts
    fill(np.isin(t, [ROAD]), (150, 128, 92), (10, 9, 7), (9, 8, 7))

    # Fields: a patchwork of strips, each with its own crop colour and furrow direction
    fields = t == FIELD
    if fields.any():
        lab, n = ndimage.label(ter == FIELD)
        # Split each farm's fields into strips by a coarse Voronoi of seeds
        seeds = rng.random((max(8, n * 5), 2)) * [H, W]
        gy, gx = np.mgrid[0:H, 0:W]
        d = np.full((H, W), np.inf); owner = np.zeros((H, W), int)
        for i, (sy, sx) in enumerate(seeds):
            dd = ((gy - sy) * 1.6) ** 2 + (gx - sx) ** 2
            better = dd < d
            d[better] = dd[better]; owner[better] = i
        owner_px = np.repeat(np.repeat(owner, up, 0), up, 1)
        angles = rng.uniform(0, math.pi, len(seeds))
        tones = np.array([(150, 136, 84), (128, 120, 70), (112, 124, 60), (138, 112, 76),
                          (120, 104, 70), (100, 116, 58)], np.float32)
        tone_idx = rng.integers(0, len(tones), len(seeds))
        Y, X = np.nonzero(fields)
        o = owner_px[Y, X]
        a = angles[o]
        stripe = np.sin((X * np.cos(a) + Y * np.sin(a)) * (2 * math.pi / 5.0))
        col = tones[tone_idx[o]] + stripe[:, None] * np.array([9, 8, 5], np.float32)
        col += grain[Y, X][:, None] * 5
        img[Y, X] = col
        # A darker line where two strips meet
        edge = np.zeros_like(fields)
        edge[:, 1:] |= owner_px[:, 1:] != owner_px[:, :-1]
        edge[1:, :] |= owner_px[1:, :] != owner_px[:-1, :]
        img[edge & fields] *= 0.82
        del owner_px, Y, X

    # Water: dark in the middle, lighter and browner in the shallows, with ripples
    water = np.isin(t, [WATER, BRIDGE, WRECK, FORD])
    if water.any():
        depth = ndimage.distance_transform_edt(water)
        depth = np.clip(depth / (6 * PX_PER_M), 0, 1)
        shallow = np.array((92, 112, 98), np.float32); deep = np.array((38, 62, 78), np.float32)
        w = depth[water][:, None]
        rip = np.sin(np.nonzero(water)[0] * 0.9 + mid[water] * 6)[:, None]
        img[water] = shallow * (1 - w) + deep * w + rip * 4 + grain[water][:, None] * 3
        # Wet, darker ground along the bank
        bank = ndimage.binary_dilation(water, iterations=3) & ~water
        img[bank] *= 0.8
    ford = t == FORD
    if ford.any():
        Y, X = np.nonzero(ford)
        img[Y, X] = np.array((120, 128, 112), np.float32) + grain[Y, X][:, None] * 14

    # Hill shading from the heights, lit from the north-west
    hpx = ndimage.zoom(hts, up, order=1)[:ph, :pw]
    gy_, gx_ = np.gradient(hpx, 1.0 / PX_PER_M)
    shade = 1.0 + (-gx_ - gy_) * 0.22
    shade = np.clip(shade, 0.55, 1.35)
    img *= shade[..., None]
    del gy_, gx_, hpx

    # Trees: individual crowns with shadows. Spruce dark and pointed, birch lighter.
    forest_cells = np.argwhere(ter == FOREST)
    stamps = make_tree_stamps(rng)
    trees = []
    for (r, c) in forest_cells:
        for _ in range(rng.integers(1, 3)):
            trees.append((r * up + rng.uniform(0, up), c * up + rng.uniform(0, up)))
    # Odd trees on open grass too
    grass_cells = np.argwhere(ter == GRASS)
    if len(grass_cells):
        pick = grass_cells[rng.random(len(grass_cells)) < 0.04]
        for (r, c) in pick:
            trees.append((r * up + rng.uniform(0, up), c * up + rng.uniform(0, up)))
    trees = np.array(trees) if trees else np.zeros((0, 2))
    kinds = rng.integers(0, len(stamps), len(trees))
    order = np.argsort(trees[:, 0]) if len(trees) else []
    # Shadows first, so crowns overlap them
    for i in order:
        y, x = trees[i]
        _, shadow = stamps[kinds[i]]
        stamp_mul(img, shadow, int(y) + 3, int(x) + 3)
    for i in order:
        y, x = trees[i]
        crown, _ = stamps[kinds[i]]
        stamp_over(img, crown, int(y), int(x))

    # Houses: pitched roofs with a lit side and a shaded side, and a shadow
    lab, n = ndimage.label(ter == BUILDING)
    roofs = [(150, 70, 52), (120, 58, 46), (86, 84, 82), (110, 94, 70), (140, 120, 96)]
    for k, sl in enumerate(ndimage.find_objects(lab)):
        r0, r1 = sl[0].start * up, sl[0].stop * up
        c0, c1 = sl[1].start * up, sl[1].stop * up
        r0 += 1; c0 += 1; r1 -= 1; c1 -= 1
        sh = 5
        img[r0 + sh:r1 + sh, c0 + sh:c1 + sh] *= 0.55
        roof = np.array(roofs[rng.integers(0, len(roofs))], np.float32)
        hgt, wid = r1 - r0, c1 - c0
        if wid >= hgt:   # ridge runs east-west: north half lit
            mid_r = (r0 + r1) // 2
            img[r0:mid_r, c0:c1] = roof * 1.15
            img[mid_r:r1, c0:c1] = roof * 0.78
            img[mid_r - 1:mid_r + 1, c0:c1] = roof * 0.6
        else:            # ridge runs north-south: west half lit
            mid_c = (c0 + c1) // 2
            img[r0:r1, c0:mid_c] = roof * 1.15
            img[r0:r1, mid_c:c1] = roof * 0.78
            img[r0:r1, mid_c - 1:mid_c + 1] = roof * 0.6
        img[r0:r1, c0:c1] += grain[r0:r1, c0:c1, None] * 3
        # Roof edge
        img[r0, c0:c1] *= 0.7; img[r1 - 1, c0:c1] *= 0.7
        img[r0:r1, c0] *= 0.7; img[r0:r1, c1 - 1] *= 0.7

    # Bridges: planking with rails; wrecks: broken dark spans over the water
    br = t == BRIDGE
    if br.any():
        Y, X = np.nonzero(br)
        img[Y, X] = np.array((112, 92, 66), np.float32) * (0.9 + 0.1 * np.sin(X * 1.6))[:, None]
        edge = ndimage.binary_dilation(br, iterations=1) & ~br & water
        img[edge] = (60, 56, 52)
    wr = t == WRECK
    if wr.any():
        Y, X = np.nonzero(wr)
        broken = (noise((ph, pw), 6, rng, 1)[Y, X] > -0.1)
        col = np.where(broken[:, None], np.array((58, 54, 50), np.float32), img[Y, X])
        img[Y, X] = col

    out = np.clip(img, 0, 255).astype(np.uint8)
    path = os.path.join(folder, "ground.jpg")
    Image.fromarray(out).save(path, quality=88, optimize=True)
    print(f"  wrote {path} ({os.path.getsize(path) // 1024} kB, {len(trees)} trees, {n} houses)")


def make_tree_stamps(rng):
    """A few crowns (RGBA float arrays) and matching shadows (multiplier arrays)."""
    stamps = []
    specs = [("spruce", 5.5, (40, 62, 36)), ("spruce", 4.5, (46, 70, 40)),
             ("birch", 6.5, (86, 112, 52)), ("birch", 5.0, (98, 120, 58)),
             ("pine", 6.0, (56, 82, 44))]
    for kind, r, col in specs:
        size = int(r * 2 + 4)
        yy, xx = np.mgrid[0:size, 0:size].astype(np.float32) - size / 2
        ang = np.arctan2(yy, xx)
        rad = np.hypot(yy, xx)
        if kind == "spruce":
            edge = r * (0.8 + 0.2 * np.cos(ang * 7))
        else:
            edge = r * (0.85 + 0.15 * np.cos(ang * 5 + rng.uniform(0, 6)))
        inside = rad < edge
        alpha = np.clip((edge - rad) * 1.2, 0, 1)
        # Lit from the north-west, darker to the south-east
        light = 1.0 + (-xx - yy) / (r * 3.0) + 0.15 * (1 - rad / max(r, 1))
        tex = 1 + rng.standard_normal((size, size)) * 0.08
        rgb = np.array(col, np.float32)[None, None, :] * (light * tex)[..., None]
        crown = np.dstack([rgb, alpha * inside])
        shadow = 1 - 0.45 * alpha * inside
        stamps.append((crown, shadow))
    return stamps


def stamp_over(img, stamp, y, x):
    h, w = stamp.shape[:2]
    y0, x0 = y - h // 2, x - w // 2
    ys, xs = max(0, -y0), max(0, -x0)
    ye, xe = min(h, img.shape[0] - y0), min(w, img.shape[1] - x0)
    if ye <= ys or xe <= xs:
        return
    region = img[y0 + ys:y0 + ye, x0 + xs:x0 + xe]
    s = stamp[ys:ye, xs:xe]
    a = s[..., 3:4]
    region[:] = region * (1 - a) + s[..., :3] * a


def stamp_mul(img, mult, y, x):
    h, w = mult.shape
    y0, x0 = y - h // 2, x - w // 2
    ys, xs = max(0, -y0), max(0, -x0)
    ye, xe = min(h, img.shape[0] - y0), min(w, img.shape[1] - x0)
    if ye <= ys or xe <= xs:
        return
    img[y0 + ys:y0 + ye, x0 + xs:x0 + xe] *= mult[ys:ye, xs:xe, None]


if __name__ == "__main__":
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "maps")
    for name in sys.argv[1:]:
        paint(name, root)
