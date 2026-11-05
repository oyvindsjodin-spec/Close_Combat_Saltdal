#!/usr/bin/env python3
"""Paint the command panel along the bottom of the battle screen: the olive
steel it is made of, the dark wells set into it, the signal-pad paper the
radio reports are written on, brass rivets, and the little pictures on it.
Those are a figure for each kind of team (rifle section, machine gun, mortar,
headquarters, ski troops), a picture for each order (move, move fast, sneak,
fire, defend, hide, dig in, stop), the buttons (zoom, pause, speed, ceasefire
flag, sound, help) and a side view of each weapon (rifle, Bren, sub-machine
gun, MG 34, Colt M/29, mortar).

Every picture is cut out as a shape, then shaded as if pressed out of tin
and lit from the north-west, like the ground and the soldiers, with a dark
painted edge. The pictures are pale so the game can tint them; the steel,
wells and paper carry their own colours and tile seamlessly.

    data/sprites/hud.png    the pictures
    data/sprites/hud.json   where each one is: [x, y, width, height]

Usage:
    python3 tools/paint_hud.py
    python3 tools/paint_hud.py --preview FILE   (the sheet on the panel colour, to look at)

Needs numpy, scipy and Pillow.
"""
import argparse, json, math, os

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

OUT = os.path.join(os.path.dirname(__file__), "..", "data", "sprites")
SS = 4                       # shapes are cut at four times the size, then scaled down
rng = np.random.default_rng(1940)


# ----------------------------------------------------------------- textures

def periodic_noise(size, sigma, seed):
    """Smooth noise that wraps round at the edges, so the tile repeats without a seam."""
    r = np.random.default_rng(seed)
    white = r.standard_normal((size, size))
    f = np.fft.fft2(white)
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    if isinstance(sigma, tuple):
        sy, sx = sigma
    else:
        sy = sx = sigma
    g = np.exp(-2 * (math.pi ** 2) * ((fy * sy) ** 2 + (fx * sx) ** 2))
    n = np.real(np.fft.ifft2(f * g))
    n -= n.mean()
    return n / (n.std() + 1e-9)


def steel(size=256):
    """Olive-drab paint on steel: brush strokes, mottling, worn chips and grime."""
    base = np.array([0.335, 0.318, 0.215])
    mottle = periodic_noise(size, 18, 1)
    strokes = periodic_noise(size, (1.2, 22), 2)       # long horizontal brush marks
    fine = periodic_noise(size, 0.8, 3)
    grime = periodic_noise(size, 40, 4)
    chips = periodic_noise(size, 2.5, 5)
    v = 1 + 0.045 * mottle + 0.035 * strokes + 0.03 * fine - 0.05 * np.clip(grime, 0, None)
    img = base[None, None, :] * v[..., None]
    # Worn spots where the paint has rubbed through to darker primer
    worn = np.clip((chips - 2.2) * 2, 0, 1)[..., None]
    img = img * (1 - worn) + np.array([0.24, 0.22, 0.16]) * worn
    # A few pale scratches
    lit = np.clip((chips + 2.6) * -3, 0, 1)[..., None]
    img = img * (1 - lit) + np.array([0.48, 0.46, 0.34]) * lit
    return rgba(img)


def well(size=128):
    """The dark recess the lists sit in: near-black olive with a dull grain."""
    base = np.array([0.135, 0.128, 0.092])
    v = 1 + 0.06 * periodic_noise(size, 10, 11) + 0.05 * periodic_noise(size, 0.9, 12)
    return rgba(base[None, None, :] * v[..., None])


def paper(size=256):
    """Army signal-pad paper, a little yellowed, with fibres and the odd stain."""
    base = np.array([0.84, 0.80, 0.66])
    fibres = periodic_noise(size, (0.6, 6), 21) + periodic_noise(size, (6, 0.6), 22)
    blotch = periodic_noise(size, 30, 23)
    grain = periodic_noise(size, 0.7, 24)
    v = 1 + 0.018 * fibres + 0.035 * blotch + 0.022 * grain
    img = base[None, None, :] * v[..., None]
    stain = np.clip((periodic_noise(size, 14, 25) - 1.9) * 0.6, 0, 0.25)[..., None]
    img = img * (1 - stain) + np.array([0.66, 0.55, 0.36]) * stain
    return rgba(img)


def rgba(rgb, alpha=None):
    rgb = np.clip(rgb, 0, 1)
    if alpha is None:
        alpha = np.ones(rgb.shape[:2])
    return np.dstack([rgb, alpha])


# ----------------------------------------------------------------- shapes

class Shape:
    """A picture cut out at four times its size, drawn with simple strokes."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        self.img = Image.new("L", (w * SS, h * SS), 0)
        self.d = ImageDraw.Draw(self.img)

    def P(self, x, y):
        return (x * SS, y * SS)

    def line(self, pts, width, fill=255):
        pts = [self.P(*p) for p in pts]
        self.d.line(pts, fill=fill, width=max(1, int(width * SS)), joint="curve")
        r = width * SS / 2
        for (x, y) in (pts[0], pts[-1]):
            self.d.ellipse((x - r, y - r, x + r, y + r), fill=fill)

    def poly(self, pts, fill=255):
        self.d.polygon([self.P(*p) for p in pts], fill=fill)

    def circle(self, x, y, r, fill=255):
        self.d.ellipse((SS * (x - r), SS * (y - r), SS * (x + r), SS * (y + r)), fill=fill)

    def ellipse(self, x0, y0, x1, y1, fill=255):
        self.d.ellipse((x0 * SS, y0 * SS, x1 * SS, y1 * SS), fill=fill)

    def rect(self, x0, y0, x1, y1, fill=255):
        self.d.rectangle((x0 * SS, y0 * SS, x1 * SS, y1 * SS), fill=fill)

    def arc(self, box, a0, a1, width, fill=255):
        self.d.arc([v * SS for v in box], a0, a1, fill=fill, width=int(width * SS))

    def mask(self):
        return np.asarray(self.img, dtype=np.float32) / 255


def press(mask, color=(0.93, 0.91, 0.84), relief=1.0, outline=1.2):
    """Shade a cut-out as pressed tin lit from the north-west, with a dark painted rim."""
    h = ndimage.gaussian_filter(mask, SS * 0.9 * relief)
    gy, gx = np.gradient(h)
    k = 2.6 * SS
    light = np.clip(0.80 + k * (-gx - gy) * 0.7, 0.45, 1.25)
    grain = 1 + 0.05 * ndimage.gaussian_filter(rng.standard_normal(mask.shape), SS * 0.5)
    rgb = np.array(color)[None, None, :] * (light * grain)[..., None]
    rim = ndimage.grey_dilation(mask, size=(int(SS * outline * 2) + 1,) * 2)
    rim = ndimage.gaussian_filter(rim, SS * 0.35)
    alpha = np.maximum(mask, rim * 0.9)
    dark = np.array([0.07, 0.065, 0.05])
    rgb = rgb * mask[..., None] + dark * (1 - mask[..., None])
    return shrink(rgba(rgb, alpha))


def shrink(arr):
    """Scale a picture painted at four times the size down to its true size."""
    h, w = arr.shape[0] // SS, arr.shape[1] // SS
    a = arr[..., 3:4]
    premul = np.dstack([arr[..., :3] * a, a])
    out = np.zeros((h, w, 4), dtype=np.float32)
    for c in range(4):
        ch = Image.fromarray((np.clip(premul[..., c], 0, 1) * 255).astype(np.float32), mode="F")
        out[..., c] = np.asarray(ch.resize((w, h), Image.LANCZOS)) / 255
    a = np.clip(out[..., 3:4], 0, 1)
    rgb = np.where(a > 1e-4, out[..., :3] / np.maximum(a, 1e-4), 0)
    return np.dstack([np.clip(rgb, 0, 1), a[..., 0]])


# --- figures (48 x 32): a man seen from the side, facing right

def man(s, x, y, pose="stand", rifle=True, scale=1.0):
    """A soldier in a steel helmet. x, y is where his feet are."""
    k = scale
    if pose == "stand":
        hip = (x, y - 11 * k); neck = (x + 0.5 * k, y - 20 * k); head = (x + 1 * k, y - 23.5 * k)
        s.line([(x - 2 * k, y), hip], 3.3 * k)
        s.line([(x + 2.5 * k, y), (x + 1.2 * k, y - 5 * k), hip], 3.3 * k)
        s.line([hip, neck], 5.4 * k)
        if rifle:
            s.line([(x - 1 * k, y - 4 * k), (x + 1.5 * k, y - 26 * k)], 1.5 * k)
            s.line([neck, (x + 1.2 * k, y - 14 * k)], 2.5 * k)
        else:
            s.line([neck, (x + 2 * k, y - 12 * k)], 2.5 * k)
    elif pose == "walk":
        hip = (x, y - 11 * k); neck = (x + 2 * k, y - 20 * k); head = (x + 3 * k, y - 23.5 * k)
        s.line([(x - 4 * k, y), hip], 3.3 * k)
        s.line([(x + 4.5 * k, y), (x + 2.5 * k, y - 5.5 * k), hip], 3.3 * k)
        s.line([hip, neck], 5.4 * k)
        if rifle:
            s.line([(x + 0.5 * k, y - 21 * k), (x + 3 * k, y - 8 * k)], 1.5 * k)
        s.line([neck, (x + 3.5 * k, y - 14 * k)], 2.5 * k)
    elif pose == "run":
        hip = (x, y - 10 * k); neck = (x + 5 * k, y - 18 * k); head = (x + 7 * k, y - 21 * k)
        s.line([(x - 7 * k, y - 3 * k), (x - 3 * k, y - 5 * k), hip], 3.3 * k)
        s.line([(x + 6 * k, y), (x + 3.5 * k, y - 5 * k), hip], 3.3 * k)
        s.line([hip, neck], 5.4 * k)
        if rifle:
            s.line([(x + 1 * k, y - 18 * k), (x + 11 * k, y - 13 * k)], 1.5 * k)
        s.line([neck, (x + 7 * k, y - 13 * k)], 2.5 * k)
    elif pose == "crouch":
        hip = (x - 1 * k, y - 6 * k); neck = (x + 4 * k, y - 13 * k); head = (x + 6 * k, y - 16 * k)
        s.line([(x - 5 * k, y), (x - 3 * k, y - 4 * k), hip], 3.3 * k)
        s.line([(x + 4 * k, y), (x + 3 * k, y - 4 * k), hip], 3.3 * k)
        s.line([hip, neck], 5.4 * k)
        if rifle:
            s.line([(x + 2 * k, y - 11 * k), (x + 13 * k, y - 10 * k)], 1.5 * k)
        s.line([neck, (x + 7 * k, y - 10 * k)], 2.5 * k)
    elif pose == "prone":
        head = (x + 14 * k, y - 3.5 * k)
        s.line([(x - 6 * k, y - 1 * k), (x + 11 * k, y - 2 * k)], 5.2 * k)
        s.line([(x - 9 * k, y), (x - 4 * k, y - 1 * k)], 3.0 * k)
        if rifle:
            s.line([(x + 9 * k, y - 3 * k), (x + 22 * k, y - 3.5 * k)], 1.5 * k)
    # the head and the helmet with its brim
    hx, hy = head
    s.circle(hx, hy, 3.0 * k)
    s.ellipse(hx - 3.2 * k, hy - 3.6 * k, hx + 3.0 * k, hy + 0.6 * k)
    s.line([(hx - 3.6 * k, hy - 0.2 * k), (hx + 3.6 * k, hy - 0.2 * k)], 0.9 * k)


def team_rifle():
    s = Shape(48, 32)
    man(s, 10, 30, "stand")
    man(s, 22, 30, "stand")
    man(s, 34, 30, "stand")
    return s


def team_mg():
    s = Shape(48, 32)
    man(s, 8, 29, "prone", rifle=False)
    # MG on its bipod in front of him
    s.line([(18, 24), (40, 23)], 2.0)
    s.line([(20, 25.5), (26, 25)], 2.6)
    s.line([(33, 23.5), (31, 29)], 0.9); s.line([(33, 23.5), (36, 29)], 0.9)
    man(s, 10, 22, "crouch", rifle=False, scale=0.7)
    return s


def team_mortar():
    s = Shape(48, 32)
    # baseplate, tube and bipod
    s.poly([(20, 30), (32, 30), (30, 27.5), (21, 27.5)])
    s.line([(25, 28), (38, 8)], 3.0)
    s.line([(35, 15), (40, 29)], 1.0); s.line([(35, 15), (31, 29)], 1.0)
    man(s, 12, 30, "crouch", rifle=False)
    man(s, 44, 30, "crouch", rifle=False, scale=0.75)
    return s


def team_hq():
    s = Shape(48, 32)
    man(s, 16, 30, "stand", rifle=False)
    # binoculars at the eyes, and a pennant on a staff
    s.rect(18, 6, 22, 8.5)
    s.line([(33, 30), (33, 3)], 1.2)
    s.poly([(33.6, 3), (45, 6.5), (33.6, 10)])
    man(s, 26, 30, "stand", scale=0.8)
    return s


def team_ski():
    s = Shape(48, 32)
    for x, sc in ((12, 1.0), (30, 0.9)):
        man(s, x, 28, "walk", scale=sc)
        s.line([(x - 9, 29.5), (x + 11, 29.5), (x + 13, 28)], 1.0)
        s.line([(x - 2, 30), (x + 6, 14)], 0.8)
    return s


# --- order pictures and buttons (32 x 32)

def ico_move():
    s = Shape(32, 32)
    man(s, 13, 30, "walk")
    s.poly([(22, 13), (29, 17), (22, 21)])
    return s


def ico_fast():
    s = Shape(32, 32)
    man(s, 12, 30, "run")
    for y in (12, 17, 22):
        s.line([(1, y), (5, y)], 1.2)
    return s


def ico_sneak():
    s = Shape(32, 32)
    man(s, 6, 27, "prone")
    return s


def ico_fire():
    s = Shape(32, 32)
    s.arc((5, 5, 27, 27), 0, 360, 2.0)
    for (a, b) in (((16, 1), (16, 10)), ((16, 22), (16, 31)), ((1, 16), (10, 16)), ((22, 16), (31, 16))):
        s.line([a, b], 1.8)
    s.circle(16, 16, 1.8)
    return s


def ico_defend():
    s = Shape(32, 32)
    # a sandbag wall with a rifle over it
    for i, (x, y) in enumerate(((3, 24), (12, 24), (21, 24), (7.5, 18.5), (16.5, 18.5))):
        s.ellipse(x, y, x + 9, y + 6)
    s.line([(6, 15), (29, 12)], 1.3)
    s.circle(12, 13, 3)
    s.ellipse(8.4, 8.6, 15.4, 13.4)
    return s


def ico_hide():
    s = Shape(32, 32)
    # a bush with a helmet peeping out
    for (x, y, r) in ((9, 22, 7), (17, 18, 8), (24, 23, 6.5), (15, 26, 6)):
        s.circle(x, y, r)
    s.ellipse(11, 6, 21, 13, fill=255)
    s.line([(10, 12.4), (22, 12.4)], 1.0)
    return s


def ico_dig():
    s = Shape(32, 32)
    # an entrenching tool over a heap of earth
    s.line([(8, 4), (19, 19)], 2.0)
    s.poly([(17, 17), (24, 15), (27, 22), (22, 27), (17, 25)])
    s.ellipse(2, 24, 16, 32)
    return s


def ico_stop():
    s = Shape(32, 32)
    # a raised open hand
    s.ellipse(9, 13, 23, 30)
    for i, x in enumerate((10.5, 14, 17.5, 21)):
        top = (6, 3, 3.5, 7)[i]
        s.line([(x, 18), (x, top)], 3.0)
    s.line([(9, 22), (4, 15)], 3.0)
    return s


def ico_zoom(plus):
    s = Shape(32, 32)
    s.arc((3, 3, 21, 21), 0, 360, 2.6)
    s.line([(19, 19), (28, 28)], 4.0)
    s.line([(8, 12), (16, 12)], 2.0)
    if plus:
        s.line([(12, 8), (12, 16)], 2.0)
    return s


def ico_pause():
    s = Shape(32, 32)
    s.rect(9, 6, 13.5, 26)
    s.rect(18.5, 6, 23, 26)
    return s


def ico_play():
    s = Shape(32, 32)
    s.poly([(10, 5), (26, 16), (10, 27)])
    return s


def ico_speed():
    s = Shape(32, 32)
    s.poly([(4, 7), (15, 16), (4, 25)])
    s.poly([(16, 7), (27, 16), (16, 25)])
    return s


def ico_flag():
    s = Shape(32, 32)
    s.line([(7, 3), (7, 30)], 1.6)
    # a white rag, a little ragged at the fly
    s.poly([(8, 4), (16, 6), (27, 4), (25, 9), (28, 14), (16, 15), (8, 13)])
    return s


def ico_sound(on):
    s = Shape(32, 32)
    s.poly([(4, 12), (10, 12), (17, 5), (17, 27), (10, 20), (4, 20)])
    if on:
        s.arc((12, 9, 24, 23), -50, 50, 1.6)
        s.arc((12, 4, 30, 28), -50, 50, 1.6)
    else:
        s.line([(21, 11), (29, 21)], 1.8)
        s.line([(29, 11), (21, 21)], 1.8)
    return s


def ico_help():
    s = Shape(32, 32)
    s.arc((9, 4, 23, 17), 180, 400, 3.0)
    s.line([(19.6, 15.5), (16, 18), (16, 22)], 3.0)
    s.circle(16, 27.5, 2.2)
    return s


def ico_cross():
    """The medic's cross on the team list: how badly the team is hurt."""
    s = Shape(16, 16)
    s.rect(6, 2, 10, 14)
    s.rect(2, 6, 14, 10)
    return s


# --- weapons (64 x 16), muzzle to the right

def wpn_rifle():
    s = Shape(64, 16)
    s.poly([(3, 9), (16, 7.5), (22, 7.5), (22, 10), (16, 10.5), (6, 13), (3, 12.5)])   # butt and wrist
    s.line([(21, 8.2), (50, 7.6)], 2.6)          # fore stock
    s.line([(48, 7.2), (61, 7.0)], 1.0)          # barrel
    s.line([(27, 6.6), (30, 5.6)], 1.0)          # bolt
    s.arc((22, 8, 27, 13), 0, 180, 0.8)          # trigger guard
    return s


def wpn_lmg():
    s = Shape(64, 16)
    s.poly([(3, 8), (14, 7), (17, 7), (17, 10), (13, 10.5), (4, 12)])
    s.rect(16, 6.5, 40, 10)
    s.line([(39, 8.2), (57, 8.0)], 1.6)
    s.line([(55, 7.5), (61, 7.5)], 2.2)           # flash hider
    s.poly([(25, 6.8), (28, 0.8), (33, 0.5), (31, 6.8)])   # the curved top magazine
    s.line([(47, 9), (44, 15)], 0.8); s.line([(47, 9), (51, 15)], 0.8)
    s.line([(22, 10), (21, 13.5)], 1.6)
    return s


def wpn_smg():
    s = Shape(64, 16)
    s.line([(8, 7.6), (16, 7.6)], 0.8)            # folded stock
    s.line([(8, 7.6), (9, 11)], 0.8)
    s.rect(15, 6, 38, 9.5)
    s.line([(37, 7.6), (50, 7.6)], 1.4)
    s.rect(26, 9, 28.6, 15.5)                     # magazine below
    s.line([(19, 9), (18, 13)], 2.0)
    return s


def wpn_mg():
    s = Shape(64, 16)
    s.poly([(2, 7.5), (12, 6), (15, 6), (15, 10), (11, 10.5), (3, 11.5)])
    s.rect(14, 6, 32, 9.6)
    s.rect(31, 5.6, 48, 9.6)                       # perforated barrel jacket
    for x in range(33, 47, 3):
        s.circle(x, 7.6, 0.6, fill=0)
    s.line([(47, 7.6), (61, 7.6)], 1.2)
    s.circle(22, 12, 3.2)                          # the drum
    s.line([(44, 9), (41, 15)], 0.8); s.line([(44, 9), (48, 15)], 0.8)
    return s


def wpn_hmg():
    s = Shape(64, 16)
    s.rect(14, 4.6, 38, 9.4)
    s.line([(37, 6.8), (56, 6.8)], 2.0)
    s.line([(55, 6.6), (61, 6.6)], 1.0)
    s.line([(10, 6), (14, 6.8)], 1.6)               # spade grips
    s.line([(26, 9), (26, 11)], 1.4)
    s.line([(26, 11), (14, 15.5)], 1.1); s.line([(26, 11), (40, 15.5)], 1.1); s.line([(26, 11), (27, 15.5)], 1.1)
    return s


def wpn_mortar():
    s = Shape(64, 16)
    s.poly([(4, 15), (20, 15), (18, 12.5), (6, 12.5)])
    s.line([(12, 13), (52, 3)], 3.0)
    s.line([(42, 6), (46, 15)], 0.9); s.line([(42, 6), (38, 15)], 0.9)
    s.line([(46, 4.2), (48, 7)], 1.4)
    return s


def rivet():
    """A brass rivet head, 12 px across."""
    n = 12 * SS
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    u = (xx + 0.5) / n * 2 - 1
    v = (yy + 0.5) / n * 2 - 1
    r = np.hypot(u, v)
    dome = np.sqrt(np.clip(1 - (r / 0.78) ** 2, 0, 1))
    nx, ny = u / 0.78, v / 0.78
    light = np.clip(0.45 + 0.75 * (-0.55 * nx - 0.55 * ny + 0.6 * dome), 0.15, 1.3)
    brass = np.array([0.62, 0.52, 0.30])
    rgb = brass[None, None, :] * light[..., None]
    shadow = np.clip(1 - (np.hypot(u - 0.12, v - 0.12) - 0.74) / 0.22, 0, 1)
    inside = (r < 0.78).astype(np.float32)
    alpha = np.maximum(inside, shadow * 0.6)
    rgb = rgb * inside[..., None]
    return shrink(rgba(rgb, alpha))


# ----------------------------------------------------------------- the sheet

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview")
    args = ap.parse_args()

    tiles = {"steel": steel(), "well": well(), "paper": paper()}
    team = {"team_rifle": team_rifle, "team_mg": team_mg, "team_mortar": team_mortar,
            "team_hq": team_hq, "team_ski": team_ski}
    icons = {"move": ico_move, "fast": ico_fast, "sneak": ico_sneak, "fire": ico_fire,
             "defend": ico_defend, "hide": ico_hide, "dig": ico_dig, "stop": ico_stop,
             "zoom_in": lambda: ico_zoom(True), "zoom_out": lambda: ico_zoom(False),
             "pause": ico_pause, "play": ico_play, "speed": ico_speed, "flag": ico_flag,
             "sound_on": lambda: ico_sound(True), "sound_off": lambda: ico_sound(False),
             "help": ico_help}
    weapons = {"w_rifle": wpn_rifle, "w_lmg": wpn_lmg, "w_smg": wpn_smg, "w_mg": wpn_mg,
               "w_hmg": wpn_hmg, "w_mortar": wpn_mortar}

    W = 512
    sheet = np.zeros((512, W, 4), dtype=np.float32)
    index = {}

    def put(name, arr, x, y):
        h, w = arr.shape[:2]
        sheet[y:y + h, x:x + w] = arr
        index[name] = [x, y, w, h]

    put("steel", tiles["steel"], 0, 0)            # 256 x 256
    put("paper", tiles["paper"], 256, 0)          # 256 x 256
    put("well", tiles["well"], 0, 256)            # 128 x 128
    put("rivet", rivet(), 128, 256)
    put("cross", press(ico_cross().mask(), relief=0.6, outline=0.8), 144, 256)
    x, y = 128, 288
    for name, fn in team.items():                 # 48 x 32
        if x + 48 > W:
            x, y = 128, y + 32
        put(name, press(fn().mask()), x, y)
        x += 48
    x, y = 0, 384
    for name, fn in icons.items():                # 32 x 32
        if x + 32 > W:
            x, y = 0, y + 32
        put(name, press(fn().mask()), x, y)
        x += 32
    x, y = 0, 448
    for name, fn in weapons.items():              # 64 x 16
        if x + 64 > W:
            x, y = 0, y + 16
        put(name, press(fn().mask(), relief=0.6, outline=0.9), x, y)
        x += 64

    os.makedirs(OUT, exist_ok=True)
    out = Image.fromarray((np.clip(sheet, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    out.save(os.path.join(OUT, "hud.png"), optimize=True)
    with open(os.path.join(OUT, "hud.json"), "w") as f:
        json.dump({"_help": "Made by tools/paint_hud.py. Each picture: [x, y, width, height] in hud.png.",
                   "sprites": index}, f, indent=1)
    print(f"{len(index)} pictures in data/sprites/hud.png")

    if args.preview:
        bg = Image.new("RGBA", out.size, (86, 81, 55, 255))
        bg.alpha_composite(out)
        bg = bg.resize((out.width * 2, out.height * 2), Image.NEAREST)
        bg.save(args.preview)


if __name__ == "__main__":
    main()
