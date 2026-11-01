#!/usr/bin/env python3
"""Paint the ground picture for a battle map, in the style of the 1990s
top-down wargames: a detailed, slightly muted overhead painting where every
tuft, stone, furrow, fence and roof tile catches the light from the north-west
and throws a shadow to the south-east.

Reads a map written by build_map.py (height and terrain per 4 m cell) and
paints it at 2 pixels per metre. The season is late May in Nordland: the
valley grass is still half straw after the winter, the birches are only just
coming into leaf, and snow lies on the heights.

What it paints:
  - grass and meadow: straw and new green, with tufts, stones and juniper bushes
  - fields: long strips running down the slope, ploughed, sown or left as hay,
    with round-pole fences between them
  - gravel roads with wheel ruts and a grassy crown
  - fjord and river with a gravel shore, darker deep water and ripples
  - rock and scree with lichen, and patchy spring snow
  - spruce, pine and birch as single crowns with soft shadows
  - houses and barns with tiled, slate, tarred or turf roofs, chimneys and shadows

Writes data/maps/<name>/ground.jpg, which the game draws under the soldiers.

Usage:
    python3 tools/paint_ground.py pothus viskiskoia rognan
    python3 tools/paint_ground.py rognan --crop ROW0 COL0 ROW1 COL1 --out FILE
        (paints only that window of cells, for trying out the look)

Needs numpy, scipy and Pillow.
"""
import argparse, json, math, os, zlib

import numpy as np
from PIL import Image
from scipy import ndimage

PX_PER_M = 4
# Sizes below were tuned at 2 pixels per metre; K scales them to the real resolution
K = PX_PER_M / 2
# Must match the TerrainType enum in src/Sim/TerrainType.cs
GRASS, FOREST, ROCK, SNOW, WATER, ROAD, BRIDGE, FIELD, BUILDING, FORD, WRECK = range(11)

# Sun from the north-west, fairly low (late evening light reads best)
SUN = np.array([-1.0, -1.0]) / math.sqrt(2)   # direction towards the sun, (row, col)
SHADOW_PX = int(7 * K)                                   # shadow of a two-storey house


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


def emboss(bump):
    """Light and shade from a height texture, lit from the north-west."""
    e = np.zeros_like(bump)
    e[1:-1, 1:-1] = bump[:-2, :-2] - bump[2:, 2:]
    return e


def speckle(shape, density, sigma, rng):
    """Scattered round bumps (stones, tufts): a height map with blobs about sigma px wide."""
    pts = (rng.random(shape) < density).astype(np.float32)
    pts *= rng.uniform(0.5, 1.0, shape).astype(np.float32)
    b = ndimage.gaussian_filter(pts, sigma)
    return b / max(b.max(), 1e-6)


def paint(name, root, crop=None, out=None):
    folder = os.path.join(root, name)
    meta = json.load(open(os.path.join(folder, "map.json")))
    W, H, cell = meta["width"], meta["height"], meta["cellSize"]
    hts = np.fromfile(os.path.join(folder, "height.bin"), "<f4").reshape(H, W)
    ter = np.fromfile(os.path.join(folder, "terrain.bin"), np.uint8).reshape(H, W)
    if crop:
        r0, c0, r1, c1 = crop
        hts, ter = hts[r0:r1, c0:c1].copy(), ter[r0:r1, c0:c1].copy()
        H, W = ter.shape
    rng = np.random.default_rng(zlib.crc32(name.encode()))
    up = int(cell * PX_PER_M)            # pixels per cell
    ph, pw = H * up, W * up
    shape = (ph, pw)
    print(f"{name}: painting {pw}x{ph} px")

    # ---- Which kind of ground each pixel is -------------------------------
    # Edges between natural ground are wobbled so they do not follow the 4 m
    # grid; roads, fields, houses and water get smooth, rounded outlines.
    yy, xx = np.mgrid[0:ph, 0:pw].astype(np.float32)
    wob = 1.1 * up
    wx = noise(shape, 16 * K, rng, 2) * wob
    wy = noise(shape, 16 * K, rng, 2) * wob
    ci = np.clip(((yy + wy) / up).astype(int), 0, H - 1)
    cj = np.clip(((xx + wx) / up).astype(int), 0, W - 1)
    t = ter[ci, cj]
    t_exact = np.repeat(np.repeat(ter, up, 0), up, 1)
    t[np.isin(t, [ROAD, BRIDGE, BUILDING, WRECK, WATER, FORD, FIELD])] = GRASS
    del wx, wy, ci, cj

    def smooth(cells, sigma, level, ragged=0.0):
        """A cell mask with rounded edges at pixel level, without the 4 m stairs.
        ragged > 0 lets the edge wander a little, as a river bank does."""
        m = ndimage.zoom(cells.astype(np.float32), up, order=1)[:ph, :pw]
        if ragged:
            level = np.clip(level + ragged * noise(shape, 10 * K, rng, 2), 0.2, 0.8)
        return ndimage.gaussian_filter(m, sigma) > level

    field_px = smooth(ter == FIELD, up * 0.9, 0.5)
    t[field_px] = FIELD
    wet_cells = np.isin(ter, [WATER, FORD, BRIDGE, WRECK])
    water_px = smooth(wet_cells, up * 0.6, 0.5, ragged=0.35)
    t[water_px] = WATER
    t[water_px & (t_exact == FORD)] = FORD
    road_cells = np.isin(ter, [ROAD, BRIDGE, WRECK])
    road_px = smooth(road_cells, up * 0.4, 0.38)
    crossing = ndimage.binary_dilation(np.isin(t_exact, [BRIDGE, WRECK]), iterations=up // 2)
    t[road_px & ~water_px] = ROAD
    on_water = road_px & water_px & crossing
    t[on_water & ndimage.binary_dilation(t_exact == WRECK, iterations=up // 2)] = WRECK
    t[on_water & (t != WRECK)] = BRIDGE
    t[t_exact == BUILDING] = BUILDING

    # ---- Shared textures --------------------------------------------------
    grain = rng.standard_normal(shape).astype(np.float32)
    mid = noise(shape, 40 * K, rng)
    big = noise(shape, 180 * K, rng, 2)
    img = np.zeros((ph, pw, 3), np.float32)
    bump = np.zeros(shape, np.float32)      # micro-relief, turned into light and shade at the end
    fine = ndimage.gaussian_filter(grain, 0.7)

    def mix(a, b, w):
        a = np.array(a, np.float32); b = np.array(b, np.float32)
        return a[None, :] * (1 - w[:, None]) + b[None, :] * w[:, None]

    # Grass: straw left from the winter, greening in patches and in the hollows
    g = t == GRASS
    mottle = noise(shape, 7 * K, rng, 2)
    greenness = np.clip(0.55 + 0.6 * big + 0.6 * mid, 0, 1)
    img[g] = mix((128, 120, 76), (86, 100, 52), greenness[g])
    img[g] *= (1 + 0.10 * mottle[g])[:, None]
    img[g] += grain[g][:, None] * np.array((6, 6, 4), np.float32)
    tufts = speckle(shape, 0.10 / K ** 2, 0.8 * K, rng) + 0.5 * speckle(shape, 0.10, 0.7, rng)
    bump += 1.0 * fine + 2.4 * tufts * g
    img[g] *= (1 - 0.30 * tufts[g])[:, None]

    # Forest floor: moss, needles and old leaves, mostly hidden under the crowns
    f = t == FOREST
    img[f] = mix((50, 52, 34), (42, 50, 32), np.clip(0.5 + mid[f], 0, 1))
    img[f] += grain[f][:, None] * 6

    # Rock and scree: grey stone with lichen, stones of all sizes
    r = t == ROCK
    lichen = np.clip(0.5 + 1.2 * mid, 0, 1)
    img[r] = mix((100, 98, 92), (84, 88, 72), lichen[r]) * (1 + 0.12 * mottle[r])[:, None]
    stones = speckle(shape, 0.02 / K ** 2, 1.6 * K, rng) + 0.6 * speckle(shape, 0.08 / K ** 2, 0.8 * K, rng) + 0.3 * speckle(shape, 0.08, 0.7, rng)
    bump += 3.0 * stones * r + 1.5 * fine * r

    # Spring snow: patchy, grey where it is thin, with rock showing through
    s = t == SNOW
    thin = noise(shape, 30 * K, rng, 3)
    snow_cover = s & (thin > -0.35)
    img[s] = mix((112, 108, 100), (100, 98, 88), lichen[s])
    img[snow_cover] = mix((236, 238, 240), (196, 198, 200), np.clip(-thin[snow_cover] + 0.2, 0, 1))
    bump += 1.5 * stones * (s & ~snow_cover)

    # Gravel roads: ruts where the carts run, a grassy crown, ragged verges
    rd = t == ROAD
    if rd.any():
        d = ndimage.distance_transform_edt(rd)
        half = ndimage.maximum_filter(d, size=up * 3)
        rel = np.where(rd, d / np.maximum(half, 1), 0)
        base = mix((154, 144, 116), (140, 130, 104), np.clip(0.5 + mid[rd], 0, 1))
        img[rd] = base + grain[rd][:, None] * 7
        rut = np.exp(-((rel - 0.55) / 0.13) ** 2) * (half > 3)
        crown = np.clip((rel - 0.8) / 0.2, 0, 1) * (half > 3)
        verge = np.clip(1 - rel / 0.3, 0, 1)
        img[rd] *= (1 - 0.18 * rut[rd])[:, None]
        img[rd] = img[rd] * (1 - 0.5 * crown[rd, None]) + np.array((122, 120, 78)) * 0.5 * crown[rd, None]
        img[rd] = img[rd] * (1 - 0.5 * verge[rd, None]) + np.array((130, 124, 82)) * 0.5 * verge[rd, None]
        bump += 0.8 * speckle(shape, 0.15 / K ** 2, 0.5 * K, rng) * rd + 0.4 * speckle(shape, 0.15, 0.5, rng) * rd
        del d, half, rel, rut, crown, verge

    # Under the houses (only the eaves show): trodden earth
    bl = t == BUILDING
    img[bl] = (118, 106, 82)

    # Fields: long strips down the slope, ploughed, sown or hay, with fences
    fences = np.zeros(shape, bool)
    if field_px.any():
        paint_fields(img, bump, fences, field_px, hts, up, rng, grain, mid)

    # Garden fences along the roads near houses
    if (t_exact == BUILDING).any() and rd.any():
        near_house = ndimage.distance_transform_edt(t_exact != BUILDING) < 26
        by_road = ndimage.binary_dilation(rd, iterations=int(3 * K)) & ~ndimage.binary_dilation(rd, iterations=int(3 * K) - 1)
        fences |= by_road & near_house & np.isin(t, [GRASS, FIELD]) & (noise(shape, 30 * K, rng, 1) > -0.5)

    # Water: gravel shore, shallows over stones, dark deep water, ripples
    water = np.isin(t, [WATER, BRIDGE, WRECK, FORD])
    if water.any():
        depth = ndimage.distance_transform_edt(water)
        dn = np.clip(depth[water] / (14 * PX_PER_M), 0, 1) ** 0.7
        img[water] = mix((92, 108, 94), (36, 64, 74), dn)
        Y, X = np.nonzero(water)
        rip = np.sin(Y * 0.55 + X * 0.18 + mid[water] * 9) * np.sin(X * 0.21 - Y * 0.07 + big[water] * 5)
        img[water] += (rip * 5 + grain[water] * 2)[:, None]
        # Shore: a band of pale gravel and wet stones
        shore = ndimage.binary_dilation(water, iterations=int(4 * K)) & ~water & (t != BUILDING)
        img[shore] = mix((150, 142, 120), (110, 104, 88), np.clip(0.5 + grain[shore] * 0.4, 0, 1))
        bump += 1.6 * speckle(shape, 0.12 / K ** 2, 0.7 * K, rng) * shore
        wetline = ndimage.binary_dilation(water, iterations=1) & ~water
        img[wetline] *= 0.75
        del depth, Y, X
    ford = t == FORD
    if ford.any():
        img[ford] = mix((124, 128, 112), (100, 110, 100), np.clip(0.5 + grain[ford] * 0.5, 0, 1))
        foam = ford & (speckle(shape, 0.05 / K ** 2, 0.8 * K, rng) > 0.35)
        img[foam] = (200, 206, 204)
        bump += 2.0 * speckle(shape, 0.06 / K ** 2, 1.0 * K, rng) * ford

    # Bushes on open grass: juniper and willow, as small dark clumps
    bushes = speckle(shape, 0.0006 / K ** 2, 2.2 * K, rng)
    bushes = (bushes > 0.35) & g
    img[bushes] = mix((60, 72, 40), (78, 84, 48), np.clip(0.5 + grain[bushes] * 0.3, 0, 1))
    bump += 4.0 * ndimage.gaussian_filter(bushes.astype(np.float32), 1.2 * K)

    # ---- Light and shade --------------------------------------------------
    img *= (1 + 0.14 * emboss(bump))[..., None]
    hpx = ndimage.zoom(hts, up, order=1)[:ph, :pw]
    gy_, gx_ = np.gradient(hpx, 1.0 / PX_PER_M)
    slope_light = (gy_ * SUN[0] + gx_ * SUN[1])      # facing the sun: positive
    shade = 1.0 + np.where(slope_light > 0, 0.18, 0.32) * np.tanh(slope_light * 1.3)
    img *= shade[..., None]
    del gy_, gx_, hpx, bump

    # Fences draw over the light, with a thin shadow
    if fences.any():
        sh = np.zeros_like(fences); sh[1:, 1:] = fences[:-1, :-1]
        img[sh & ~fences] *= 0.7
        posts = fences & (((xx + yy).astype(int) % int(7 * K)) == 0)
        img[fences] = (122, 112, 92)
        img[posts] = (84, 74, 60)
    del xx, yy

    # ---- Trees and houses, with their shadows ------------------------------
    shadow = np.zeros(shape, np.float32)
    crowns = []
    tree_specs = make_tree_stamps(rng)
    forest_cells = np.argwhere(ter == FOREST)
    for (rr, cc) in forest_cells:
        for _ in range(rng.integers(1, 3)):
            crowns.append((rr * up + rng.uniform(0, up), cc * up + rng.uniform(0, up), rng.integers(0, len(tree_specs))))
    open_cells = np.argwhere(np.isin(ter, [GRASS]))
    if len(open_cells):
        for (rr, cc) in open_cells[rng.random(len(open_cells)) < 0.03]:
            crowns.append((rr * up + rng.uniform(0, up), cc * up + rng.uniform(0, up), rng.integers(0, len(tree_specs))))
    crowns.sort(key=lambda c: c[0])
    for (y, x, k) in crowns:
        _, alpha, length = tree_specs[k]
        stamp_max(shadow, alpha, int(y + length * 0.8), int(x + length * 0.8))

    houses = []
    lab, n = ndimage.label(t_exact == BUILDING)
    for k, sl in enumerate(ndimage.find_objects(lab)):
        r0, r1 = sl[0].start + int(K), sl[0].stop - int(K)
        c0, c1 = sl[1].start + int(K), sl[1].stop - int(K)
        if r1 - r0 < 3 * K or c1 - c0 < 3 * K:
            continue
        houses.append((r0, c0, r1, c1))
        tall = SHADOW_PX if max(r1 - r0, c1 - c0) < 40 * K else SHADOW_PX + int(3 * K)
        for i in range(1, tall + 1):
            shadow[r0 + i:r1 + i, c0 + i:c1 + i] = np.maximum(shadow[r0 + i:r1 + i, c0 + i:c1 + i], 1.0)
    # Trodden yards round the houses
    yard = np.zeros(shape, bool)
    for (r0, c0, r1, c1) in houses:
        m5 = int(5 * K)
        yard[max(r0 - m5, 0):r1 + m5, max(c0 - m5, 0):c1 + m5] = True
    yard &= np.isin(t, [GRASS, FIELD]) & (noise(shape, 5 * K, rng, 1) > -0.4)
    img[yard] = img[yard] * 0.45 + np.array((128, 114, 86), np.float32) * 0.55
    shadow = ndimage.gaussian_filter(shadow, 1.3 * K)
    img *= (1 - 0.42 * np.clip(shadow, 0, 1))[..., None]
    del shadow

    for (y, x, k) in crowns:
        crown, _, _ = tree_specs[k]
        stamp_over(img, crown, int(y), int(x))

    for (r0, c0, r1, c1) in houses:
        paint_house(img, r0, c0, r1, c1, rng)

    # Bridges: planking with rails; wrecks: broken dark spans over the water
    br = t == BRIDGE
    if br.any():
        Y, X = np.nonzero(br)
        img[Y, X] = np.array((116, 98, 74), np.float32) * (0.88 + 0.12 * np.sin(X * 1.6 + Y * 1.6))[:, None]
        edge = ndimage.binary_dilation(br, iterations=int(K)) & ~br & water
        img[edge] = (64, 58, 52)
    wr = t == WRECK
    if wr.any():
        Y, X = np.nonzero(wr)
        broken = (noise(shape, 6 * K, rng, 1)[Y, X] > -0.1)
        img[Y, X] = np.where(broken[:, None], np.array((58, 54, 50), np.float32), img[Y, X])

    # ---- Colour grade: a touch muted and warm, like a printed photograph ---
    lum = img.mean(axis=2, keepdims=True)
    img = lum + (img - lum) * 0.82
    img = (img - 128) * 1.06 + 128
    img *= np.array((1.03, 1.0, 0.95), np.float32)
    out_img = np.clip(img, 0, 255).astype(np.uint8)

    path = out or os.path.join(folder, "ground.jpg")
    Image.fromarray(out_img).save(path, quality=90, optimize=True)
    print(f"  wrote {path} ({os.path.getsize(path) // 1024} kB, {len(crowns)} trees, {len(houses)} houses)")


def paint_fields(img, bump, fences, field_px, hts, up, rng, grain, mid):
    """Strips of ploughed land, young crops and hay, run down the slope as on
    the Saltdal farms, with round-pole fences between them."""
    lab, n = ndimage.label(field_px)
    hpx_grad = np.gradient(ndimage.zoom(hts, up, order=1)[:field_px.shape[0], :field_px.shape[1]])
    kinds = [  # base colour, furrow strength, furrow period (px)
        ((96, 76, 56), 14, 3.0),    # freshly ploughed
        ((112, 92, 66), 10, 3.0),   # harrowed
        ((104, 112, 62), 9, 4.0),   # young crop in rows
        ((132, 126, 82), 0, 0),     # last year's hay meadow
        ((118, 120, 72), 0, 0),     # grazed meadow
    ]
    for i, sl in enumerate(ndimage.find_objects(lab), start=1):
        m = lab[sl] == i
        Y, X = np.nonzero(m)
        gy = hpx_grad[0][sl][m].mean(); gx = hpx_grad[1][sl][m].mean()
        a = math.atan2(gy, gx) if abs(gx) + abs(gy) > 1e-4 else rng.uniform(0, math.pi)
        a += rng.normal(0, 0.15)
        Yg, Xg = Y + sl[0].start, X + sl[1].start
        u = Xg * math.cos(a) + Yg * math.sin(a)      # along the strip
        v = -Xg * math.sin(a) + Yg * math.cos(a)     # across the strips
        v0 = v.min()
        edges = np.cumsum(rng.uniform(22 * K, 64 * K, 200))
        idx = np.searchsorted(edges, v - v0)
        kind = rng.integers(0, len(kinds), idx.max() + 1)
        base = np.array([kinds[k][0] for k in kind], np.float32)[idx]
        amp = np.array([kinds[k][1] for k in kind], np.float32)[idx]
        per = np.array([kinds[k][2] or 1 for k in kind], np.float32)[idx]
        furrow = np.sin(v * 2 * math.pi / per)
        col = base + furrow[:, None] * amp[:, None] * np.array((0.6, 0.55, 0.45), np.float32)
        col += grain[Yg, Xg][:, None] * 6 + mid[Yg, Xg][:, None] * 8
        img[Yg, Xg] = col
        bump[Yg, Xg] += furrow * amp / 10
        # Fences along strip edges and round the outside of the farm
        strip = np.full(m.shape, -1, int); strip[Y, X] = idx
        e = np.zeros(m.shape, bool)
        e[:, 1:] |= (strip[:, 1:] != strip[:, :-1]) & (strip[:, 1:] >= 0) & (strip[:, :-1] >= 0)
        e[1:, :] |= (strip[1:, :] != strip[:-1, :]) & (strip[1:, :] >= 0) & (strip[:-1, :] >= 0)
        e |= m & ~ndimage.binary_erosion(m, iterations=1)
        fences[sl] |= e
        # A strip of rough grass along each fence that the plough cannot reach
        rough = ndimage.binary_dilation(e, iterations=int(2 * K)) & m & ~e
        ry, rx = np.nonzero(rough)
        img[ry + sl[0].start, rx + sl[1].start] = img[ry + sl[0].start, rx + sl[1].start] * 0.4 + np.array((126, 118, 76)) * 0.6


ROOFS = [  # colour, kind
    ((150, 66, 48), "tile"), ((132, 58, 44), "tile"), ((78, 64, 58), "tile"),
    ((100, 102, 104), "slate"), ((88, 90, 94), "slate"),
    ((56, 54, 52), "tar"), ((128, 128, 122), "iron"),
    ((96, 98, 58), "turf"),
]


def paint_house(img, r0, c0, r1, c1, rng):
    """A roof seen from above: two slopes, one lit and one shaded, with tile
    courses, a ridge, eaves and a chimney."""
    col, kind = ROOFS[rng.integers(0, len(ROOFS))]
    col = np.array(col, np.float32) * rng.uniform(0.92, 1.08)
    h, w = r1 - r0, c1 - c0
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32) / K   # in 2-px-per-metre units
    ew = w >= h                                    # ridge runs east-west
    across = ys if ew else xs                      # distance across the ridge
    along = xs if ew else ys
    span = (h if ew else w) / K
    ridge = span / 2
    lit = across < ridge                            # north or west slope faces the sun
    light = np.where(lit, 1.18, 0.72)
    # Slopes darken a little towards the eaves
    light *= 1 - 0.10 * np.abs(across - ridge) / max(ridge, 1)
    pat = np.zeros_like(ys)
    if kind == "tile":
        pat = -0.10 * (np.mod(across, 2.5) < 0.9) - 0.05 * (np.mod(along + 1.2 * (np.floor(across / 2.5) % 2), 2.4) < 0.8)
    elif kind == "slate":
        pat = -0.08 * (np.mod(across, 2.0) < 0.7) + rng.normal(0, 0.03, ys.shape)
    elif kind == "iron":
        pat = 0.07 * np.sin(along * math.pi)
    elif kind == "tar":
        pat = rng.normal(0, 0.02, ys.shape) - 0.05 * (np.mod(along, 6) < 1)
    elif kind == "turf":
        pat = ndimage.gaussian_filter(rng.normal(0, 0.25, ys.shape), 0.8)
    roof = col[None, None, :] * (light * (1 + pat))[..., None]
    roof += rng.normal(0, 3, roof.shape)
    # Ridge cap and eaves
    cap = np.abs(across - ridge) < 0.8
    roof[cap] = col * 1.25
    roof[(across < 0.9) | (across > span - 1.1) | (along < 0.9) | (along > (w if ew else h) / K - 1.1)] *= 0.86
    img[r0:r1, c0:c1] = roof
    # Chimney on the ridge, with its own little shadow
    if kind != "turf" or rng.random() < 0.5:
        L = w if ew else h
        p = int(L * rng.uniform(0.25, 0.75))
        q = int(ridge * K)
        c3 = int(3 * K)
        cy, cx = (r0 + q - c3 // 3, c0 + p) if ew else (r0 + p, c0 + q - c3 // 3)
        img[cy + c3 * 2 // 3:cy + c3 * 5 // 3, cx + c3 * 2 // 3:cx + c3 * 5 // 3] *= 0.6
        img[cy:cy + c3, cx:cx + c3] = (104, 62, 50)
        img[cy:cy + int(K), cx:cx + c3] = (150, 100, 80)
        img[cy:cy + c3, cx:cx + int(K)] = (150, 100, 80)
        img[cy + c3 // 3:cy + c3 * 2 // 3 + 1, cx + c3 // 3:cx + c3 * 2 // 3 + 1] = (30, 26, 24)


def make_tree_stamps(rng):
    """Crowns as RGBA arrays (with light from the north-west built in), the
    matching shadow masks, and how far each shadow reaches."""
    out = []
    specs = [  # kind, radius px, colour, how many variants
        ("spruce", 5.5, (38, 56, 40), 4), ("spruce", 4.2, (44, 62, 44), 3),
        ("pine", 6.0, (60, 76, 46), 2),
        ("birch", 6.2, (88, 100, 54), 3),    # just coming into leaf
        ("birch", 5.0, (98, 94, 66), 1),     # still nearly bare
    ]
    for kind, r, col, count in specs:
        for _ in range(count):
            rad_v = r * K * rng.uniform(0.85, 1.15)
            size = int(rad_v * 2 + 6)
            yy, xx = np.mgrid[0:size, 0:size].astype(np.float32) - size / 2
            ang = np.arctan2(yy, xx); rad = np.hypot(yy, xx)
            # A ragged outline: a few waves of random size, so no two crowns
            # share a regular star or flower shape
            wob = sum(rng.uniform(0.3, 1.0) / k * np.cos(ang * k + rng.uniform(0, 6.3)) for k in range(3, 9))
            if kind == "spruce":
                edge = rad_v * (0.84 + 0.11 * wob)
            else:
                edge = rad_v * (0.82 + 0.15 * wob)
            alpha = np.clip((edge - rad) * 1.3 / K, 0, 1)
            # Height of the crown: a dome, lumpy for leafy trees, a cone for spruce
            hgt = np.clip(1 - rad / np.maximum(edge, 1e-3), 0, 1)
            if kind == "spruce":
                # Branch tufts: fine lumps on a cone, not regular ridges
                tufts = ndimage.gaussian_filter(rng.random((size, size)).astype(np.float32), 0.6 * K)
                tufts = (tufts - tufts.mean()) / (tufts.std() + 1e-6)
                hgt = hgt * (1 + 0.18 * tufts)
            else:
                lumps = ndimage.gaussian_filter(rng.random((size, size)).astype(np.float32), 1.0 * K)
                hgt = np.sqrt(hgt) * (0.7 + 0.6 * lumps)
            gy, gx = np.gradient(hgt)
            light = 1.0 - 1.1 * (gy * SUN[0] + gx * SUN[1]) + 0.2 * hgt - 0.1
            tex = 1 + rng.standard_normal((size, size)) * 0.07
            rgb = np.array(col, np.float32)[None, None, :] * np.clip(light * tex, 0.45, 1.5)[..., None]
            out.append((np.dstack([rgb, alpha]), alpha, rad_v))
    return out


def _clip(shape_img, h, w, y, x):
    y0, x0 = y - h // 2, x - w // 2
    ys, xs = max(0, -y0), max(0, -x0)
    ye, xe = min(h, shape_img[0] - y0), min(w, shape_img[1] - x0)
    return y0, x0, ys, xs, ye, xe


def stamp_over(img, stamp, y, x):
    h, w = stamp.shape[:2]
    y0, x0, ys, xs, ye, xe = _clip(img.shape, h, w, y, x)
    if ye <= ys or xe <= xs:
        return
    region = img[y0 + ys:y0 + ye, x0 + xs:x0 + xe]
    s = stamp[ys:ye, xs:xe]
    a = s[..., 3:4]
    region[:] = region * (1 - a) + s[..., :3] * a


def stamp_max(arr, mask, y, x):
    h, w = mask.shape
    y0, x0, ys, xs, ye, xe = _clip(arr.shape, h, w, y, x)
    if ye <= ys or xe <= xs:
        return
    region = arr[y0 + ys:y0 + ye, x0 + xs:x0 + xe]
    np.maximum(region, mask[ys:ye, xs:xe], out=region)


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("maps", nargs="+")
    ap.add_argument("--crop", nargs=4, type=int, metavar=("ROW0", "COL0", "ROW1", "COL1"))
    ap.add_argument("--out")
    a = ap.parse_args()
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "maps")
    for name in a.maps:
        paint(name, root, a.crop, a.out)
