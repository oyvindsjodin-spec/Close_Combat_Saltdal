#!/usr/bin/env python3
"""Build a battle map from real elevation data.

Downloads open elevation tiles (AWS Terrain Tiles, "terrarium" format) for a
square area, then derives terrain types from height and slope. Writes:

    data/maps/<name>/map.json     size, cell size, location
    data/maps/<name>/height.bin   float32 metres, row-major, little-endian
    data/maps/<name>/terrain.bin  one byte per cell (see TerrainType.cs)
    data/maps/<name>/preview.png  a quick look at the result

The terrain types are a first guess from the height model only. Forest,
fields and farms will be hand-painted later from historical maps.

Usage:
    python3 tools/build_map.py pothus --lat 66.950 --lon 15.302 --size 1536 --cell 4
"""
import argparse, heapq, io, json, math, os, urllib.request

import numpy as np
from PIL import Image

TILE_URL = "https://elevation-tiles-prod.s3.amazonaws.com/terrarium/{z}/{x}/{y}.png"
ZOOM = 14

# Must match the TerrainType enum in src/Sim/TerrainType.cs
GRASS, FOREST, ROCK, SNOW, WATER, ROAD, BRIDGE = range(7)


def tile_xy(lat, lon, z=ZOOM):
    n = 2 ** z
    x = (lon + 180) / 360 * n
    r = math.radians(lat)
    y = (1 - math.log(math.tan(r) + 1 / math.cos(r)) / math.pi) / 2 * n
    return x, y


def fetch_tile(x, y, cache):
    path = os.path.join(cache, f"{ZOOM}_{x}_{y}.png")
    if not os.path.exists(path):
        with urllib.request.urlopen(TILE_URL.format(z=ZOOM, x=x, y=y)) as r:
            data = r.read()
        with open(path, "wb") as f:
            f.write(data)
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.float64)
    return a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768


def sample_heights(lat, lon, size_m, cell_m, cache):
    cells = int(size_m // cell_m)
    m_per_deg_lat = 111_320.0
    m_per_deg_lon = 111_320.0 * math.cos(math.radians(lat))
    # Cell centres, row 0 = north edge
    offs = (np.arange(cells) + 0.5) * cell_m - size_m / 2
    lats = lat - offs / m_per_deg_lat
    lons = lon + offs / m_per_deg_lon
    fx = np.array([tile_xy(lat, lo)[0] for lo in lons])
    fy = np.array([tile_xy(la, lon)[1] for la in lats])
    tx0, tx1 = int(fx.min()), int(fx.max())
    ty0, ty1 = int(fy.min()), int(fy.max())
    mosaic = np.vstack([
        np.hstack([fetch_tile(tx, ty, cache) for tx in range(tx0, tx1 + 1)])
        for ty in range(ty0, ty1 + 1)
    ])
    px = (fx - tx0) * 256 - 0.5
    py = (fy - ty0) * 256 - 0.5
    gx, gy = np.meshgrid(px, py)
    x0 = np.floor(gx).astype(int); y0 = np.floor(gy).astype(int)
    dx = gx - x0; dy = gy - y0
    h00 = mosaic[y0, x0]; h10 = mosaic[y0, x0 + 1]
    h01 = mosaic[y0 + 1, x0]; h11 = mosaic[y0 + 1, x0 + 1]
    h = (h00 * (1 - dx) * (1 - dy) + h10 * dx * (1 - dy)
         + h01 * (1 - dx) * dy + h11 * dx * dy)
    return h.astype(np.float32)


def slope_deg(h, cell_m):
    gy, gx = np.gradient(h, cell_m)
    return np.degrees(np.arctan(np.hypot(gx, gy)))


def cheapest_path(cost, start, goals):
    """Dijkstra over an 8-connected grid; returns the cell list."""
    rows, cols = cost.shape
    dist = np.full(cost.shape, np.inf)
    prev = {}
    dist[start] = 0
    heap = [(0.0, start)]
    goals = set(goals)
    while heap:
        d, (r, c) = heapq.heappop(heap)
        if (r, c) in goals:
            path = [(r, c)]
            while path[-1] in prev:
                path.append(prev[path[-1]])
            return path[::-1]
        if d > dist[r, c]:
            continue
        for dr in (-1, 0, 1):
            for dc in (-1, 0, 1):
                if dr == dc == 0:
                    continue
                nr, nc = r + dr, c + dc
                if 0 <= nr < rows and 0 <= nc < cols:
                    step = (1.414 if dr and dc else 1.0) * (cost[r, c] + cost[nr, nc]) / 2
                    nd = d + step
                    if nd < dist[nr, nc]:
                        dist[nr, nc] = nd
                        prev[(nr, nc)] = (r, c)
                        heapq.heappush(heap, (nd, (nr, nc)))
    raise RuntimeError("no path")


def lowest_cell(h, row):
    return (row, int(np.argmin(h[row])))


def build(args):
    out = os.path.join(args.out, args.name)
    cache = os.path.join(args.out, ".tilecache")
    os.makedirs(out, exist_ok=True)
    os.makedirs(cache, exist_ok=True)

    h = sample_heights(args.lat, args.lon, args.size, args.cell, cache)
    rows, cols = h.shape
    s = slope_deg(h, args.cell)
    t = np.full(h.shape, GRASS, dtype=np.uint8)

    # Birch forest on gentle and moderate slopes below the tree line
    t[(h < args.treeline) & (s >= 6) & (s < 35)] = FOREST
    t[s >= 35] = ROCK
    t[h >= args.snowline] = SNOW

    # River: the lowest route from the south edge to the north edge.
    # Saltdalselva flows north towards Rognan.
    rel = h - h.min()
    river = cheapest_path(rel ** 2 + 1, lowest_cell(h, rows - 1),
                          [(0, c) for c in range(cols)])
    river_mask = np.zeros(h.shape, bool)
    for r, c in river:
        for dc in range(-args.river_width // 2 + 1, args.river_width // 2 + 1):
            if 0 <= c + dc < cols:
                river_mask[r, c + dc] = True
    t[river_mask] = WATER

    # Road: the easiest route along the valley floor, crossing the river
    # only if it has to (and then on a bridge).
    road_cost = 1 + (s / 8) ** 2 + np.where(river_mask, 400, 0)
    road = cheapest_path(road_cost, (rows - 1, int(np.argmin(road_cost[rows - 1]))),
                         [(0, c) for c in range(cols)])
    for r, c in road:
        t[r, c] = BRIDGE if river_mask[r, c] else ROAD

    meta = {
        "name": args.name,
        "width": cols,
        "height": rows,
        "cellSize": args.cell,
        "centerLat": args.lat,
        "centerLon": args.lon,
        "source": "AWS Terrain Tiles (terrarium), zoom %d" % ZOOM,
        "note": "Terrain types are derived from height and slope only; hand edits come later.",
    }
    with open(os.path.join(out, "map.json"), "w") as f:
        json.dump(meta, f, indent=2)
    h.astype("<f4").tofile(os.path.join(out, "height.bin"))
    t.tofile(os.path.join(out, "terrain.bin"))

    palette = {GRASS: (150, 160, 100), FOREST: (70, 100, 60), ROCK: (130, 125, 115),
               SNOW: (235, 238, 240), WATER: (70, 110, 150), ROAD: (180, 160, 120),
               BRIDGE: (90, 70, 50)}
    img = np.zeros((rows, cols, 3), np.uint8)
    for k, col in palette.items():
        img[t == k] = col
    Image.fromarray(img).resize((cols * 2, rows * 2), Image.NEAREST).save(
        os.path.join(out, "preview.png"))
    print(f"{args.name}: {cols}x{rows} cells of {args.cell} m, "
          f"height {h.min():.0f}-{h.max():.0f} m")


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    p.add_argument("name")
    p.add_argument("--lat", type=float, required=True)
    p.add_argument("--lon", type=float, required=True)
    p.add_argument("--size", type=float, default=1536, help="map side in metres")
    p.add_argument("--cell", type=float, default=4, help="cell size in metres")
    p.add_argument("--treeline", type=float, default=450)
    p.add_argument("--snowline", type=float, default=550)
    p.add_argument("--river-width", type=int, default=5, help="in cells")
    p.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "data", "maps"))
    build(p.parse_args())
