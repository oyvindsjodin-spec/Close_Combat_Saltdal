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

Each map has a recipe in tools/maps/<name>.json: where it is, and the hand-made
parts the height model cannot give (the road's route, blown bridges, fords,
farms and their fields, a town). Positions in a recipe are [row, col] as
fractions of the map, row 0 at the north edge and col 0 at the west edge.

Usage:
    python3 tools/build_map.py tools/maps/pothus.json
"""
import argparse, heapq, io, json, math, os, urllib.request

import numpy as np
from PIL import Image

TILE_URL = "https://elevation-tiles-prod.s3.amazonaws.com/terrarium/{z}/{x}/{y}.png"
ZOOM = 14

# Must match the TerrainType enum in src/Sim/TerrainType.cs
GRASS, FOREST, ROCK, SNOW, WATER, ROAD, BRIDGE, FIELD, BUILDING, FORD, WRECK = range(11)


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


def smooth_noise(shape, scale_cells, rng):
    """Smooth random field in [0, 1], with blobs about scale_cells across."""
    rows, cols = shape
    gr, gc = max(2, rows // scale_cells + 2), max(2, cols // scale_cells + 2)
    coarse = (rng.random((gr, gc)) * 255).astype(np.uint8)
    img = Image.fromarray(coarse).resize((cols, rows), Image.BICUBIC)
    a = np.asarray(img).astype(np.float32) / 255
    return (a - a.min()) / max(1e-6, a.max() - a.min())


def cell(rc, rows, cols):
    return (min(rows - 1, max(0, int(rc[0] * rows))), min(cols - 1, max(0, int(rc[1] * cols))))


def paint_path(t, path, kind, width=1, keep=()):
    rows, cols = t.shape
    for r, c in path:
        for dr in range(-(width // 2), width - width // 2):
            for dc in range(-(width // 2), width - width // 2):
                rr, cc = r + dr, c + dc
                if 0 <= rr < rows and 0 <= cc < cols and t[rr, cc] not in keep:
                    t[rr, cc] = kind


def build(recipe_path):
    with open(recipe_path) as f:
        R = json.load(f)
    name = R["name"]
    out_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "maps")
    out = os.path.join(out_root, name)
    cache = os.path.join(out_root, ".tilecache")
    os.makedirs(out, exist_ok=True)
    os.makedirs(cache, exist_ok=True)
    rng = np.random.default_rng(R.get("seed", 1))
    cell_m = R.get("cell", 4)
    treeline = R.get("treeline", 450)
    snowline = R.get("snowline", 550)

    h = sample_heights(R["lat"], R["lon"], R.get("size", 1536), cell_m, cache)
    rows, cols = h.shape
    s = slope_deg(h, cell_m)
    t = np.full(h.shape, GRASS, dtype=np.uint8)

    # Birch forest on the slopes below the tree line, bare rock on cliffs, snow up high
    t[(h < treeline) & (s >= 6) & (s < 35)] = FOREST
    t[s >= 35] = ROCK
    t[h >= snowline] = SNOW
    # The valley floor is mostly wooded too, in patches
    flat = (h < treeline) & (s < 6)
    share = R.get("valleyForest", 0.5)
    if share > 0 and flat.any():
        noise = smooth_noise(h.shape, 14, rng) + 0.5 * smooth_noise(h.shape, 5, rng) + 0.25 * smooth_noise(h.shape, 2, rng)
        cut = np.quantile(noise[flat], 1 - share)
        t[flat & (noise >= cut)] = FOREST

    # The sea (a fjord), if the map reaches it
    sea = np.zeros(h.shape, bool)
    if R.get("seaLevel") is not None:
        sea = h < R["seaLevel"]
        t[sea] = WATER

    # The river: the lowest route from the south edge to the north edge or the sea.
    river_mask = sea.copy()
    if R.get("river", True):
        rel = h - h.min()
        goals = [(0, c) for c in range(cols)] + list(zip(*np.nonzero(sea)))
        start = lowest_cell(h, rows - 1)
        river = cheapest_path(rel ** 2 + 1, start, goals)
        width = R.get("riverWidth", 5)
        m = np.zeros(h.shape, bool)
        for r, c in river:
            for dc in range(-width // 2 + 1, width // 2 + 1):
                if 0 <= c + dc < cols:
                    m[r, c + dc] = True
        river_mask |= m
    # Side streams run down to the river
    for st in R.get("streams", []):
        rel = h - h.min()
        path = cheapest_path(rel ** 2 + 1, cell(st, rows, cols), list(zip(*np.nonzero(river_mask))))
        m = np.zeros(h.shape, bool)
        for r, c in path:
            m[max(0, r - 1):r + 2, max(0, c - 1):c + 1] = True
        river_mask |= m
    t[river_mask] = WATER

    # Farms: fields cleared around the farmhouse
    farms = []
    for fm in R.get("farms", []):
        r0, c0 = cell(fm, rows, cols)
        rad = rng.uniform(16, 28)
        yy, xx = np.ogrid[:rows, :cols]
        d = np.hypot((yy - r0) / rng.uniform(0.7, 1.3), xx - c0)
        wobble = smooth_noise(h.shape, 6, rng) * 8
        fields = (d < rad + wobble - 4) & (s < 10) & ~river_mask & (t != ROCK)
        t[fields] = FIELD
        farms.append((r0, c0, fm[2] if len(fm) > 2 else 3))

    # The road through the map, following waypoints; it crosses water on a bridge,
    # or on the wreck of one where the waypoint says "blown"
    road_cost = 1 + (s / 8) ** 2 + np.where(river_mask, 400, 0)
    road_mask = np.zeros(h.shape, bool)
    crossings = []

    def lay(points, width):
        for a, b in zip(points, points[1:]):
            path = cheapest_path(road_cost, cell(a, rows, cols), [cell(b, rows, cols)])
            blown = len(b) > 2 and b[2] == "blown"
            wet = [(r, c) for r, c in path if river_mask[r, c]]
            if wet:
                crossings.append({"row": wet[len(wet) // 2][0], "col": wet[len(wet) // 2][1], "blown": blown})
            for r, c in path:
                for dr in range(width):
                    for dc in range(width):
                        rr, cc = r + dr, c + dc
                        if rr >= rows or cc >= cols:
                            continue
                        road_mask[rr, cc] = True
                        if river_mask[rr, cc]:
                            t[rr, cc] = WRECK if blown else BRIDGE
                        else:
                            t[rr, cc] = ROAD

    if "road" in R:
        lay(R["road"], 2)
    else:
        path = cheapest_path(road_cost, (rows - 1, int(np.argmin(road_cost[rows - 1]))), [(0, c) for c in range(cols)])
        lay([[r / rows, c / cols] for r, c in path[::20]] + [[path[-1][0] / rows, path[-1][1] / cols]], 2)
    for track in R.get("tracks", []):
        lay(track, 1)

    # Farmhouses and barns, and a track out to the road
    for r0, c0, n in farms:
        placed = 0
        for _ in range(60):
            if placed >= n:
                break
            hr, hc = int(rng.integers(3, 5)), int(rng.integers(3, 5))
            r = r0 + int(rng.integers(-7, 8)); c = c0 + int(rng.integers(-7, 8))
            if r < 1 or c < 1 or r + hr >= rows - 1 or c + hc >= cols - 1:
                continue
            area = t[r - 1:r + hr + 1, c - 1:c + hc + 1]
            if np.isin(area, [WATER, ROAD, BRIDGE, BUILDING, WRECK]).any() or s[r:r + hr, c:c + hc].max() > 12:
                continue
            t[r:r + hr, c:c + hc] = BUILDING
            placed += 1
        if road_mask.any():
            rr, cc = np.nonzero(road_mask)
            k = int(np.argmin((rr - r0) ** 2 + (cc - c0) ** 2))
            if math.hypot(rr[k] - r0, cc[k] - c0) > 3:
                path = cheapest_path(road_cost, (r0, c0), [(int(rr[k]), int(cc[k]))])
                for r, c in path:
                    if t[r, c] not in (BUILDING, BRIDGE, WRECK, WATER):
                        t[r, c] = ROAD

    # A town: a grid of streets with houses in the blocks
    town = R.get("town")
    if town:
        (ra, ca), (rb, cb) = cell(town["rect"][:2], rows, cols), cell(town["rect"][2:], rows, cols)
        step = town.get("street", 12)
        density = town.get("density", 0.7)
        for r in range(ra, rb):
            for c in range(ca, cb):
                if river_mask[r, c] or t[r, c] in (BRIDGE, WRECK):
                    continue
                if (r - ra) % step in (0, 1) or (c - ca) % step in (0, 1):
                    t[r, c] = ROAD
                elif t[r, c] not in (ROAD,):
                    t[r, c] = GRASS
        for br in range(ra + 2, rb - 3, step):
            for bc in range(ca + 2, cb - 3, step):
                for hr0 in range(br + 1, br + step - 4, 5):
                    for hc0 in range(bc + 1, bc + step - 4, 5):
                        if rng.random() > density:
                            continue
                        hr, hc = int(rng.integers(3, 5)), int(rng.integers(3, 5))
                        area = t[hr0:hr0 + hr, hc0:hc0 + hc]
                        if area.size and not np.isin(area, [WATER, ROAD, BRIDGE, WRECK]).any():
                            t[hr0:hr0 + hr, hc0:hc0 + hc] = BUILDING

    # Fords over gravel bars
    for fd in R.get("fords", []):
        r0, c0 = cell(fd, rows, cols)
        for r in range(max(0, r0 - 1), min(rows, r0 + 2)):
            for c in range(max(0, c0 - 25), min(cols, c0 + 26)):
                if t[r, c] == WATER and not sea[r, c]:
                    t[r, c] = FORD

    meta = {
        "name": name,
        "width": cols,
        "height": rows,
        "cellSize": cell_m,
        "centerLat": R["lat"],
        "centerLon": R["lon"],
        "source": "AWS Terrain Tiles (terrarium), zoom %d; recipe %s" % (ZOOM, os.path.basename(recipe_path)),
        "bridges": crossings,
        "note": R.get("note", "Terrain derived from height and slope, plus the hand-made parts of the recipe."),
    }
    with open(os.path.join(out, "map.json"), "w") as f:
        json.dump(meta, f, indent=2)
    h.astype("<f4").tofile(os.path.join(out, "height.bin"))
    t.tofile(os.path.join(out, "terrain.bin"))

    palette = {GRASS: (150, 160, 100), FOREST: (70, 100, 60), ROCK: (130, 125, 115),
               SNOW: (235, 238, 240), WATER: (70, 110, 150), ROAD: (180, 160, 120),
               BRIDGE: (90, 70, 50), FIELD: (180, 172, 110), BUILDING: (115, 60, 50),
               FORD: (110, 145, 170), WRECK: (40, 35, 30)}
    img = np.zeros((rows, cols, 3), np.uint8)
    for k, col in palette.items():
        img[t == k] = col
    gy, gx = np.gradient(h)
    shade = np.clip(1 + (gx - gy) * 0.08, 0.75, 1.2)
    img = np.clip(img * shade[..., None], 0, 255).astype(np.uint8)
    Image.fromarray(img).resize((cols * 2, rows * 2), Image.NEAREST).save(
        os.path.join(out, "preview.png"))
    print(f"{name}: {cols}x{rows} cells of {cell_m} m, height {h.min():.0f}-{h.max():.0f} m, "
          f"crossings {crossings}")


if __name__ == "__main__":
    import sys
    for recipe in sys.argv[1:]:
        build(recipe)
