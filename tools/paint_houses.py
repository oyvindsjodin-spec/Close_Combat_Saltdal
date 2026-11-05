"""Roofs for the painted ground, seen from straight above in the light of a
low sun from the north-west, as the 1990s wargames painted their villages.

Every roof is worked out as a height field (gable or hipped), so each slope
gets its own light and shade, and then covered with its own material, laid
course by course: red or brown pantiles, grey slate, tarred felt between
battens, corrugated iron or turf. On top of that come moss, lichen and soot
streaks, ridge tiles, white barge boards on the gable ends, gutters, brick
chimneys with their shadows, roof lights and dormers. Big barns get a barn
bridge (laavebru) up to the loft door, and many houses a small entrance
porch.

Used by paint_ground.py; paint_house() paints one house into the picture.
"""
import math

import numpy as np
from scipy import ndimage

PX = 4                      # pixels per metre, must match PX_PER_M in paint_ground.py
SUN = np.array([-1.0, -1.0]) / math.sqrt(2)    # towards the sun, (row, col)

# colour, material, weight (how common)
HOUSE_ROOFS = [
    ((158, 72, 50), "tile", 7), ((138, 62, 46), "tile", 5), ((116, 66, 50), "tile", 2),
    ((84, 70, 64), "tile", 2),
    ((104, 106, 110), "slate", 3), ((86, 90, 96), "slate", 2),
    ((60, 58, 56), "tar", 3),
    ((132, 132, 126), "iron", 1),
    ((98, 100, 60), "turf", 1),
]
BARN_ROOFS = [
    ((62, 60, 58), "tar", 4), ((136, 136, 130), "iron", 3), ((120, 84, 66), "iron", 1),
    ((142, 64, 48), "tile", 2), ((96, 98, 102), "slate", 2),
]


def _pick(table, rng):
    w = np.array([t[2] for t in table], float)
    return table[rng.choice(len(table), p=w / w.sum())][:2]


def _value_noise(shape, cell_px, rng):
    """Smooth noise 0..1 with features about cell_px across."""
    h, w = shape
    gh, gw = max(2, int(h / cell_px) + 2), max(2, int(w / cell_px) + 2)
    g = rng.random((gh, gw)).astype(np.float32)
    out = ndimage.zoom(g, ((h + cell_px) / gh, (w + cell_px) / gw), order=3)[:h, :w]
    return np.clip(out, 0, 1)


def _hash(a, b, salt):
    """A steady pseudo-random number 0..1 for integer arrays a, b."""
    h = (a.astype(np.int64) * 374761393 + b.astype(np.int64) * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def roof_rgb(h, w, col, kind, rng, ridge_ew, hipped, salt=0):
    """Paint a roof h x w pixels. Returns (rgb float array, alpha mask, height field in metres)."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    ym, xm = (ys + 0.5) / PX, (xs + 0.5) / PX
    Hm, Wm = h / PX, w / PX
    dn, ds, dw, de = ym, Hm - ym, xm, Wm - xm
    if ridge_ew:
        dists = [dn, ds] + ([dw, de] if hipped else [])
        dirs = [(-1, 0), (1, 0), (0, -1), (0, 1)]
    else:
        dists = [dw, de] + ([dn, ds] if hipped else [])
        dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)]
    stack = np.stack(dists)
    plane = np.argmin(stack, axis=0)
    srt = np.sort(stack, axis=0)
    u = srt[0]                                      # metres up the slope from the eave
    seam = (srt[1] - srt[0]) if len(dists) > 1 else np.full_like(u, 9)  # near 0 on ridges and hips
    span = (Hm if ridge_ew else Wm) / 2
    # Along each slope: x for slopes facing north or south, y for slopes facing east or west
    faces_ns = np.zeros_like(u, bool)
    for k, (dr, dc) in enumerate(dirs[:len(dists)]):
        faces_ns |= (plane == k) & (dr != 0)
    v = np.where(faces_ns, xm, ym)

    # Light on each slope: a pitch of about 35 degrees, the sun 40 degrees up in the north-west
    pitch = math.radians(28 if kind in ("iron", "tar") else 35)
    el = math.radians(40)
    light = np.zeros_like(u)
    for k, (dr, dc) in enumerate(dirs[:len(dists)]):
        d = math.sin(pitch) * math.cos(el) * (dr * SUN[0] + dc * SUN[1]) + math.cos(pitch) * math.sin(el)
        light[plane == k] = 0.50 + 0.80 * d
    # A little darker towards the eaves, where the roof curves out over the wall
    light *= 1 - 0.08 * np.clip(1 - u / max(span, 0.5), 0, 1)

    tex = np.zeros_like(u)
    tint = np.zeros((h, w, 3), np.float32)       # added colour (moss, rust)
    if kind == "tile":
        course = 0.7
        row = np.floor(u / course).astype(int)
        fr = (u / course) - row                  # 0 at the lower edge of each course
        tw = 0.6
        colk = np.floor(v / tw).astype(int)
        # Pantile profile: a rounded roll and a hollow, lit from one side
        prof = np.sin((v / tw) * 2 * math.pi)
        tex += 0.10 * prof
        # Each course overlaps the one below: a dark line at its lower edge
        tex -= 0.22 * (fr < 0.18)
        tex += 0.06 * (fr > 0.7)
        # Tiles weathered unevenly; the odd new or broken one
        tile = _hash(row + salt, colk, 1)
        tex += (tile - 0.5) * 0.16
        tex -= 0.25 * (tile > 0.985)
    elif kind == "slate":
        course = 0.5
        row = np.floor(u / course).astype(int)
        fr = u / course - row
        sw = 0.55
        colk = np.floor(v / sw + 0.5 * (row % 2)).astype(int)
        fv = v / sw + 0.5 * (row % 2) - colk
        tile = _hash(row + salt, colk, 2)
        tex += (tile - 0.5) * 0.22
        tex -= 0.18 * (fr < 0.2) + 0.10 * ((fv < 0.08) & (fr > 0.2))
        # Yellow lichen spots
        lich = (_hash(row + salt, colk, 3) > 0.93) & (fv > 0.3) & (fv < 0.7)
        tint[lich] += (22, 18, -6)
    elif kind == "tar":
        # Felt laid in strips down the slope, nailed over wooden battens
        bat = np.mod(v, 1.0)
        tex -= 0.16 * (bat < 0.12)
        tex += 0.06 * ((bat > 0.12) & (bat < 0.24))
        tex += (_hash(np.floor(v).astype(int) + salt, np.floor(u / 3).astype(int), 4) - 0.5) * 0.10
        tex += rng.normal(0, 0.03, u.shape)
    elif kind == "iron":
        tex += 0.08 * np.sin(v / 0.42 * 2 * math.pi)
        lap = np.mod(u, 2.4)
        tex -= 0.12 * (lap < 0.15)
        sheet = np.floor(v / 0.9).astype(int)
        tex += (_hash(sheet + salt, np.floor(u / 2.4).astype(int), 5) - 0.5) * 0.12
        # Rust running down from the laps and nail lines
        streak = _hash(np.floor(v / 0.25).astype(int) + salt, np.zeros_like(sheet), 6)
        rust = np.clip((streak - 0.72) * 3, 0, 1) * np.clip(1 - lap / 2.4, 0, 1)
        tint += rust[..., None] * np.array((34, 2, -22), np.float32)
    elif kind == "turf":
        lumps = ndimage.gaussian_filter(rng.normal(0, 1, u.shape).astype(np.float32), 1.2)
        tex += 0.22 * lumps / (lumps.std() + 1e-6) * 0.5
        blades = rng.random(u.shape) < 0.08
        tex += 0.18 * blades
        flowers = rng.random(u.shape) < 0.004
        tint[flowers] += (90, 80, -20)
        # Birch bark showing along the eaves under the turf
        bark = u < 0.35
        tint[bark] += (-20, -26, -18)

    # Weathering: colour drift, moss in damp patches near the eaves, soot and rain streaks
    drift = _value_noise((h, w), 6 * PX, rng) - 0.5
    tex += 0.10 * drift
    if kind in ("tile", "slate", "tar"):
        moss = _value_noise((h, w), 1.6 * PX, rng) * np.clip(1.3 - u / max(span, 0.5), 0, 1)
        moss = np.clip((moss - 0.55) * 2.5, 0, 0.45)
        tint += moss[..., None] * (np.array((86, 98, 50), np.float32) - np.array(col, np.float32)) * 0.8
    streaks = _hash(np.floor(v / 0.35).astype(int) + salt, np.zeros_like(plane), 7)
    tex -= 0.07 * np.clip((streaks - 0.6) * 2.5, 0, 1) * np.clip(1 - u / max(span, 0.5), 0, 1)

    base = np.array(col, np.float32)
    rgb = base[None, None, :] * (light * (1 + tex))[..., None] + tint
    rgb += rng.normal(0, 2.5, rgb.shape)

    # Ridge and hip caps: a row of capping tiles or a board, catching the light
    cap = seam < 0.32
    if kind == "turf":
        cap &= False
    if cap.any():
        capcol = base * (1.18 if kind != "iron" else 1.1)
        if kind == "tile":
            seg = np.mod((xm if ridge_ew else ym) + ym * 0.0, 0.45) < 0.08
            capv = np.where(seg, 0.8, 1.0)
            rgb[cap] = capcol * capv[cap][:, None]
        elif kind == "tar":
            rgb[cap] = np.array((48, 46, 44), np.float32)
        else:
            rgb[cap] = capcol
        # Its shadow just down the south-east side
        shade = (seam > 0.32) & (seam < 0.55) & (light < 0.9)
        rgb[shade] *= 0.82

    # Barge boards on the gable ends: white-painted planks, lit on the north-west
    if not hipped and kind != "turf":
        end = (dw < 0.28) | (de < 0.28) if ridge_ew else (dn < 0.28) | (ds < 0.28)
        board = np.array((206, 200, 184), np.float32) if kind != "iron" or rng.random() < 0.5 else np.array((150, 70, 52), np.float32)
        rgb[end] = board * light[end][:, None] * 0.95
    elif kind == "turf":
        # Turf roofs hang over the gable ends in a ragged fringe
        pass

    # Gutter and the shadow of the eave overhang
    eave = (u < 0.14)
    rgb[eave] *= 0.62
    gutter = (u >= 0.14) & (u < 0.3) & faces_ns & (kind not in ("turf", "tar"))
    rgb[gutter] = rgb[gutter] * 0.5 + np.array((150, 148, 140), np.float32) * light[gutter][:, None] * 0.5

    alpha = np.ones((h, w), np.float32)
    if kind == "turf":
        # Ragged turf edge
        edge = np.minimum(np.minimum(dn, ds), np.minimum(dw, de))
        rag = _value_noise((h, w), 0.8 * PX, rng)
        alpha = np.clip((edge - 0.1 * rag) * 6, 0, 1)
    return rgb, alpha, u


def _shadow(img, mask, r0, c0, depth_px, strength=0.45):
    """Darken the picture to the south-east of a mask placed at (r0, c0)."""
    h, w = mask.shape
    sh = np.zeros((h + depth_px + 2, w + depth_px + 2), np.float32)
    for i in range(1, depth_px + 1):
        sh[i:i + h, i:i + w] = np.maximum(sh[i:i + h, i:i + w], mask)
    sh = ndimage.gaussian_filter(sh, 0.8)
    H, W = img.shape[:2]
    y1, x1 = min(H, r0 + sh.shape[0]), min(W, c0 + sh.shape[1])
    if y1 <= r0 or x1 <= c0 or r0 < 0 or c0 < 0:
        return
    sub = sh[:y1 - r0, :x1 - c0]
    img[r0:y1, c0:x1] *= (1 - strength * np.clip(sub, 0, 1))[..., None]


def _put(img, rgb, alpha, r0, c0):
    H, W = img.shape[:2]
    h, w = alpha.shape
    if r0 < 0 or c0 < 0 or r0 + h > H or c0 + w > W:
        return False
    a = alpha[..., None]
    img[r0:r0 + h, c0:c0 + w] = img[r0:r0 + h, c0:c0 + w] * (1 - a) + rgb * a
    return True


def _chimney(img, cy, cx, rng):
    """A brick chimney with a cap and a sooty flue, and its shadow on the roof."""
    s = int(1.2 * PX)
    H, W = img.shape[:2]
    if cy < 0 or cx < 0 or cy + s + 6 > H or cx + s + 6 > W:
        return
    _shadow(img, np.ones((s, s), np.float32), cy, cx, int(1.4 * PX), 0.5)
    ys, xs = np.mgrid[0:s, 0:s]
    brick = np.array((128, 70, 54) if rng.random() < 0.7 else (124, 120, 112), np.float32)
    v = 1 + 0.10 * ((ys % 2) == 0) - 0.08 * _hash(ys, xs // 2 + ys % 2, 9)
    block = brick * v[..., None]
    block[:1, :] = brick * 1.35          # top edges in the sun
    block[:, :1] = brick * 1.25
    block[-1:, :] = brick * 0.6
    block[:, -1:] = brick * 0.6
    f = s // 2
    block[f - 1:f + 1, f - 1:f + 1] = (24, 20, 18)
    img[cy:cy + s, cx:cx + s] = block


def _roof_light(img, cy, cx, horizontal):
    """A small roof window: a dark pane in a light frame."""
    h, w = (int(0.7 * PX), int(0.9 * PX)) if horizontal else (int(0.9 * PX), int(0.7 * PX))
    H, W = img.shape[:2]
    if cy < 0 or cx < 0 or cy + h + 1 > H or cx + w + 1 > W:
        return
    img[cy:cy + h + 1, cx:cx + w + 1] *= 0.7
    frame = img[cy:cy + h, cx:cx + w].mean(axis=(0, 1)) * 0.6 + np.array((120, 114, 100), np.float32) * 0.4
    img[cy:cy + h, cx:cx + w] = frame
    img[cy + 1:cy + h - 1, cx + 1:cx + w - 1] = (46, 54, 62)
    img[cy + 1, cx + 1] = (96, 112, 122)


def _barn_bridge(img, r0, c0, r1, c1, ridge_ew, rng):
    """The earth and timber ramp up to the loft door on the long side of a barn."""
    H, W = img.shape[:2]
    bw, bl = int(4 * PX), int(5 * PX)
    if ridge_ew:
        mid = (c0 + c1) // 2 + int(rng.uniform(-0.15, 0.15) * (c1 - c0))
        north = rng.random() < 0.5
        y0 = r0 - bl if north else r1
        x0 = mid - bw // 2
        hh, ww = bl, bw
        along_rows = True
    else:
        mid = (r0 + r1) // 2 + int(rng.uniform(-0.15, 0.15) * (r1 - r0))
        west = rng.random() < 0.5
        x0 = c0 - bl if west else c1
        y0 = mid - bw // 2
        hh, ww = bw, bl
        along_rows = False
    if y0 < 0 or x0 < 0 or y0 + hh + 8 > H or x0 + ww + 8 > W:
        return
    _shadow(img, np.ones((hh, ww), np.float32), y0, x0, int(1.0 * PX), 0.4)
    ys, xs = np.mgrid[0:hh, 0:ww]
    across = xs if along_rows else ys
    along = ys if along_rows else xs
    plank = np.array((122, 108, 88), np.float32)
    v = 1 + 0.12 * (_hash(along // 3, np.zeros_like(along), 11) - 0.5) - 0.18 * ((along % 3) == 0)
    ramp = plank * v[..., None]
    n = across.max() + 1
    # Stone walls along both sides
    side = (across < 3) | (across >= n - 3)
    stone = np.array((120, 116, 108), np.float32) * (0.85 + 0.3 * _hash(along // 2, across, 12))[..., None]
    ramp[side] = stone[side]
    ramp[across == 0] *= 1.2
    ramp[across == n - 1] *= 0.6
    img[y0:y0 + hh, x0:x0 + ww] = ramp


def _porch(img, r0, c0, r1, c1, ridge_ew, col, kind, rng):
    """A small entrance porch with its own little gable roof on the long side."""
    pw, pd = int(2.4 * PX), int(1.8 * PX)
    if ridge_ew:
        x0 = c0 + int(rng.uniform(0.2, 0.7) * (c1 - c0 - pw))
        south = rng.random() < 0.6
        y0 = r1 - 1 if south else r0 - pd + 1
        hh, ww = pd, pw
        ridge = False                          # its ridge runs north-south, square to the house
    else:
        y0 = r0 + int(rng.uniform(0.2, 0.7) * (r1 - r0 - pw))
        east = rng.random() < 0.6
        x0 = c1 - 1 if east else c0 - pd + 1
        hh, ww = pw, pd
        ridge = True
    if kind == "turf":
        kind, col = "tar", (60, 58, 56)
    rgb, a, _ = roof_rgb(hh, ww, col, kind, rng, ridge, False, salt=7)
    H, W = img.shape[:2]
    if y0 < 0 or x0 < 0 or y0 + hh + 6 > H or x0 + ww + 6 > W:
        return
    _shadow(img, a, y0, x0, int(1.2 * PX), 0.42)
    _put(img, rgb, a, y0, x0)


def _dormer(img, r0, c0, r1, c1, ridge_ew, col, kind, rng):
    """A dormer window built out of one slope, its small ridge square to the main one."""
    span = (r1 - r0) if ridge_ew else (c1 - c0)
    L = (c1 - c0) if ridge_ew else (r1 - r0)
    dw = int(2.6 * PX)
    depth = int(span * 0.36)
    if depth < 2 * PX or L < 5 * PX:
        return
    at = int(rng.uniform(0.3, 0.7) * (L - dw))
    south = rng.random() < 0.6
    if ridge_ew:
        x0 = c0 + at
        y0 = r1 - depth - int(0.2 * PX) if south else r0 + int(0.2 * PX)
        hh, ww = depth, dw
        ridge = False
    else:
        y0 = r0 + at
        x0 = c1 - depth - int(0.2 * PX) if south else c0 + int(0.2 * PX)
        hh, ww = dw, depth
        ridge = True
    rgb, a, _ = roof_rgb(hh, ww, col, kind, rng, ridge, False, salt=13)
    _shadow(img, a, y0, x0, int(0.8 * PX), 0.35)
    _put(img, rgb, a, y0, x0)


def paint_house(img, r0, c0, r1, c1, seed):
    """Paint one house, barn or shed whose roof covers rows r0..r1, columns c0..c1."""
    rng = np.random.default_rng(seed)
    h, w = r1 - r0, c1 - c0
    long_m = max(h, w) / PX
    short_m = min(h, w) / PX
    barn = long_m >= 14 and short_m >= 9
    shed = long_m < 7
    col, kind = _pick(BARN_ROOFS if barn else HOUSE_ROOFS, rng)
    col = tuple(np.array(col) * rng.uniform(0.92, 1.08))
    ridge_ew = w >= h if abs(w - h) > PX else rng.random() < 0.5
    hipped = (not barn and not shed and kind not in ("turf", "iron") and rng.random() < 0.28)

    if barn and rng.random() < 0.75:
        _barn_bridge(img, r0, c0, r1, c1, ridge_ew, rng)
    if not barn and not shed and rng.random() < 0.55:
        _porch(img, r0, c0, r1, c1, ridge_ew, col, kind, rng)

    rgb, alpha, u = roof_rgb(h, w, col, kind, rng, ridge_ew, hipped, salt=seed % 997)
    _put(img, rgb, alpha, r0, c0)
    # A thin dark line round the roof, so it reads clearly against the yard
    edge = np.zeros((h, w), bool)
    edge[0, :] = edge[-1, :] = edge[:, 0] = edge[:, -1] = True
    if kind != "turf":
        img[r0:r1, c0:c1][edge] *= 0.7

    if not shed and not barn and kind != "turf" and long_m >= 10 and rng.random() < 0.45:
        _dormer(img, r0, c0, r1, c1, ridge_ew, col, kind, rng)

    # Chimneys on the ridge: one or two on a house, maybe one on a shed or turf roof
    L = w if ridge_ew else h
    q = (h if ridge_ew else w) // 2
    n_ch = 0 if barn else (2 if long_m >= 11 and rng.random() < 0.5 else 1)
    if shed or kind == "turf":
        n_ch = 1 if rng.random() < 0.4 else 0
    s = int(1.2 * PX)
    for i in range(n_ch):
        p = int(L * (rng.uniform(0.3, 0.7) if n_ch == 1 else (0.22 + 0.56 * i + rng.uniform(-0.05, 0.05))))
        cy, cx = (r0 + q - s // 2, c0 + p) if ridge_ew else (r0 + p, c0 + q - s // 2)
        _chimney(img, cy, cx, rng)
    # Barns: a ventilator or two on the ridge
    if barn:
        for i in range(rng.integers(1, 3)):
            p = int(L * rng.uniform(0.25, 0.75))
            vs = int(1.0 * PX)
            cy, cx = (r0 + q - vs // 2, c0 + p) if ridge_ew else (r0 + p, c0 + q - vs // 2)
            if cy + vs + 4 < img.shape[0] and cx + vs + 4 < img.shape[1]:
                _shadow(img, np.ones((vs, vs), np.float32), cy, cx, int(0.8 * PX), 0.4)
                img[cy:cy + vs, cx:cx + vs] = (88, 80, 70)
                img[cy:cy + 1, cx:cx + vs] = (140, 128, 110)
                img[cy:cy + vs, cx:cx + 1] = (130, 120, 104)
    # Roof lights on some houses and barns, on the slope away from the dormer side
    if not shed and kind in ("tile", "slate", "iron", "tar") and rng.random() < 0.5:
        for i in range(rng.integers(1, 3)):
            p = int(L * rng.uniform(0.15, 0.85))
            off = int(q * rng.uniform(0.35, 0.6))
            if ridge_ew:
                _roof_light(img, r0 + off, c0 + p, True)
            else:
                _roof_light(img, r0 + p, c0 + off, False)
