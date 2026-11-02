# Generates Thornwick Woods (Phase 5, C2), game/data/levels/thornwick_woods.jsonc: a woodsball site on old farmland,
# about seven times the size of Oxbarrow Works. Its woods, hedges, tracks and cover are laid out here, the cover
# scattered from a fixed seed clear of the tracks, the buildings and each other. Edit this and run it, not the file:
#   python3 tools/levels/thornwick_woods.py game/data/levels
import json, math, random, sys

OUT = sys.argv[1]

# The site: a timber fence round x -172...172, z -122...122 (north is -Z), with a gate in each side.
FX, FZ = 172.0, 122.0
GATE = 8.0

def fmt(v):
    if isinstance(v, float):
        s = f"{v:.2f}".rstrip('0').rstrip('.')
        return s if s not in ('-0', '') else '0'
    return json.dumps(v)

def obj(d):
    parts = []
    for k, v in d.items():
        if isinstance(v, dict):
            parts.append(f'"{k}": {obj(v)}')
        elif isinstance(v, list):
            parts.append(f'"{k}": {arr(v)}')
        else:
            parts.append(f'"{k}": {fmt(v)}')
    return "{ " + ", ".join(parts) + " }"

def arr(a):
    return "[" + ", ".join(arr(x) if isinstance(x, list) else (obj(x) if isinstance(x, dict) else fmt(x)) for x in a) + "]"

# ---------------------------------------------------------------------------------------------------------------------
# Tracks. Junctions are points both lines share, so the bots' road graph joins them.
CROSS = [0.0, -10.0]
ROADS = [
    {"name": "the lane", "points_m": [[0, 134], [0, FZ - 10], [0, 60], CROSS], "width_m": 4.5, "material": "gravel"},
    {"name": "the north lane", "points_m": [CROSS, [0, -60], [0, -FZ + 10], [0, -134]], "width_m": 4.5, "material": "gravel"},
    {"name": "the west track", "points_m": [CROSS, [-60, -10], [-112, -8], [-160, -8], [-180, -8]], "width_m": 4.0, "material": "gravel"},
    {"name": "the east track", "points_m": [CROSS, [60, -12], [118, -14], [160, -14], [180, -14]], "width_m": 4.0, "material": "gravel"},
    {"name": "the ring", "points_m": [[-160, -112], [0, -112], [160, -112], [160, -14], [160, 112], [0, 112], [-160, 112],
                                      [-160, -8], [-160, -112]], "width_m": 4.0, "material": "dirt"},
    {"name": "the quarry track", "points_m": [[118, -14], [118, 24]], "width_m": 4.0, "material": "gravel"},
    {"name": "the farm track", "points_m": [[0, 68], [-16, 68]], "width_m": 4.0, "material": "gravel"},
]

# Buildings from the kit (each placed by its footprint's north-west corner) and their footprints' size.
BUILDINGS = [
    ("goods_shed", -48, 62, 30, 12),      # the barn
    ("depot_office", 12, 70, 16, 10),     # the farmhouse
    ("pump_house", 34, 88, 8, 6),
    ("gatehouse", 7, 114, 6, 4),          # the site office by the south gate
    ("guardhouse", -130, 20, 6, 5),       # the camp
    ("yard_office", -118, 36, 10, 6),
    ("signal_box", 26, -92, 8, 4),        # the fort's lookout
    ("boiler_house", 110, 40, 14, 10),    # the scrapyard
    ("weighbridge_hut", 100, 28, 3, 2.5),
    ("lamp_hut", -100, -68, 3, 2.5),      # a hut in Blackthorn Wood's glade
]

# The fort: a timber stockade round the lookout, a doorway in each side.
FORT = (16.0, -102.0, 44.0, -74.0)

# Hedges (paint and people stop at them; gaps to go through).
HEDGES = [
    [[-62, 30], [-6, 30]], [[6, 30], [28, 30]], [[32, 30], [66, 30]],          # the farm's north side
    [[-62, -58], [-6, -58]], [[6, -58], [36, -58]], [[40, -58], [62, -58]],    # the north field's south side
    [[-62, -58], [-62, -30]], [[-62, -26], [-62, -16]], [[-62, -4], [-62, 12]], [[-62, 16], [-62, 30]],  # west of the middle
    [[62, -58], [62, -38]], [[62, -34], [62, -18]], [[62, -6], [62, 10]], [[62, 14], [62, 30]],          # east of the middle
]

WOODS = [
    {"name": "Blackthorn Wood", "rect_m": [-168, -118, -66, -22], "spacing_m": 5.5, "gaps": 0.18, "seed": 11,
     "glades": [[-122, -82, -94, -58]]},
    {"name": "Rookery Wood", "rect_m": [66, -118, 168, -22], "spacing_m": 6.0, "gaps": 0.15, "seed": 12,
     "glades": [[100, -74, 126, -50]]},
    {"name": "Hazel Copse", "rect_m": [-168, 72, -66, 118], "spacing_m": 5.0, "gaps": 0.2, "seed": 13, "glades": []},
    {"name": "Long Spinney", "rect_m": [66, 90, 168, 118], "spacing_m": 5.5, "gaps": 0.15, "seed": 14, "glades": []},
    {"name": "the middle copse", "rect_m": [-46, -52, -12, -20], "spacing_m": 5.0, "gaps": 0.2, "seed": 15, "glades": []},
]

PLAYER_SPAWNS = [
    {"position_m": [0, 0, 128], "yaw_deg": 0},
    {"position_m": [-178, 0, -8], "yaw_deg": -90},
    {"position_m": [0, 0, -128], "yaw_deg": 180},
    {"position_m": [178, 0, -14], "yaw_deg": 90},
]

VEHICLES = [
    {"vehicle": "buggy", "position_m": [-10, 0, 100], "yaw_deg": 0},
    {"vehicle": "buggy", "position_m": [10, 0, 100], "yaw_deg": 0},
    {"vehicle": "buggy", "position_m": [-140, 0, 4], "yaw_deg": -90},
    {"vehicle": "buggy", "position_m": [126, 0, 14], "yaw_deg": 0},
    {"vehicle": "buggy", "position_m": [-14, 0, -96], "yaw_deg": 180},
]

OPPONENT_SPAWNS = [
    {"id": "fort_tower", "position_m": [30, 3.2, -90.8], "yaw_deg": 180, "roles": ["marksman", "sentry"]},
    {"id": "fort_gate", "position_m": [30, 0, -70], "yaw_deg": 0, "roles": ["sentry"]},
    {"id": "north_field", "position_m": [-30, 0, -92], "yaw_deg": 0, "roles": ["patroller", "sentry"], "patrol": "north_field"},
    {"id": "blackthorn_glade", "position_m": [-108, 0, -76], "yaw_deg": -45, "roles": ["sentry", "flanker"]},
    {"id": "blackthorn_edge", "position_m": [-72, 0, -40], "yaw_deg": -90, "roles": ["hunter", "flanker"]},
    {"id": "rookery_glade", "position_m": [113, 0, -62], "yaw_deg": 45, "roles": ["sentry", "flanker"]},
    {"id": "rookery_edge", "position_m": [72, 0, -30], "yaw_deg": 90, "roles": ["flanker", "hunter"]},
    {"id": "crossroads", "position_m": [8, 0, -3], "yaw_deg": 0, "roles": ["sentry", "rusher"]},
    {"id": "middle_copse", "position_m": [-28, 0, -36], "yaw_deg": 0, "roles": ["sentry", "flanker"]},
    {"id": "east_field", "position_m": [36, 0, -30], "yaw_deg": 90, "roles": ["patroller", "rusher"], "patrol": "middle_east"},
    {"id": "camp_office", "position_m": [-113, 0, 39], "yaw_deg": -90, "roles": ["sentry"]},
    {"id": "camp_containers", "position_m": [-136, 0, 48], "yaw_deg": -90, "roles": ["rusher", "patroller"], "patrol": "camp"},
    {"id": "guardhouse", "position_m": [-127, 0, 22.5], "yaw_deg": 0, "roles": ["sentry"]},
    {"id": "scrapyard", "position_m": [130, 0, 64], "yaw_deg": 90, "roles": ["sentry", "rusher"]},
    {"id": "boiler_house", "position_m": [117, 0, 45], "yaw_deg": 90, "roles": ["sentry"]},
    {"id": "quarry_track", "position_m": [114, 0, 6], "yaw_deg": 0, "roles": ["patroller"], "patrol": "quarry_track"},
    {"id": "barn", "position_m": [-33, 0, 66], "yaw_deg": -90, "roles": ["sentry"]},
    {"id": "farmhouse", "position_m": [16, 0, 75], "yaw_deg": 0, "roles": ["sentry", "marksman"]},
    {"id": "farmyard", "position_m": [-6, 0, 86], "yaw_deg": 0, "roles": ["patroller", "rusher"], "patrol": "farm"},
    {"id": "hazel_copse", "position_m": [-120, 0, 96], "yaw_deg": -90, "roles": ["flanker", "sentry"]},
    {"id": "long_spinney", "position_m": [120, 0, 104], "yaw_deg": 90, "roles": ["flanker", "sentry"]},
    {"id": "pump_house", "position_m": [38, 0, 91], "yaw_deg": 0, "roles": ["sentry"]},
    {"id": "west_field", "position_m": [-46, 0, 8], "yaw_deg": -90, "roles": ["rusher", "hunter"]},
    {"id": "ring_north", "position_m": [-80, 0, -112], "yaw_deg": -90, "roles": ["patroller"], "patrol": "ring_north"},
]

PATROLS = [
    {"id": "north_field", "points_m": [[-42, 0, -102], [-10, 0, -102], [-10, 0, -68], [-42, 0, -68]], "pause_s": 3},
    {"id": "middle_east", "points_m": [[20, 0, -44], [52, 0, -44], [52, 0, 20], [20, 0, 20]], "pause_s": 3},
    {"id": "camp", "points_m": [[-146, 0, 14], [-100, 0, 14], [-100, 0, 60], [-146, 0, 60]], "pause_s": 3},
    {"id": "quarry_track", "points_m": [[114, 0, -8], [114, 0, 28]], "loop": False, "pause_s": 4},
    {"id": "farm", "points_m": [[-56, 0, 48], [44, 0, 48], [44, 0, 104], [-12, 0, 104]], "pause_s": 3},
    {"id": "ring_north", "points_m": [[-150, 0, -108], [-24, 0, -108]], "loop": False, "pause_s": 4},
]

PICKUPS = [
    {"id": "pod_tower", "kind": "pod", "position_m": [32.5, 3.2, -90.6]},
    {"id": "pod_camp", "kind": "pod", "position_m": [-110, 0, 38.5]},
    {"id": "pod_barn", "kind": "pod", "position_m": [-24, 0, 65]},
    {"id": "pod_boiler", "kind": "pod", "position_m": [120, 0, 43]},
    {"id": "pod_crossroads", "kind": "pod", "position_m": [5, 0, -15]},
    {"id": "pod_pump_house", "kind": "pod", "position_m": [36, 0, 90]},
    {"id": "air_gate", "kind": "air", "position_m": [10, 0, 119.5]},
    {"id": "air_glade", "kind": "air", "position_m": [-98.5, 0, -64.5]},
]

# ---------------------------------------------------------------------------------------------------------------------
# Keeping things apart.

def seg_dist(p, a, b):
    ax, az, bx, bz = a[0], a[1], b[0], b[1]
    dx, dz = bx - ax, bz - az
    l2 = dx * dx + dz * dz
    t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - az) * dz) / l2))
    return math.hypot(p[0] - (ax + t * dx), p[1] - (az + t * dz))

def near_road(p, margin):
    for r in ROADS:
        pts = r["points_m"]
        for i in range(len(pts) - 1):
            if seg_dist(p, pts[i], pts[i + 1]) < r["width_m"] / 2 + margin:
                return True
    return False

def near_hedge(p, margin):
    return any(seg_dist(p, h[0], h[1]) < 0.45 + margin for h in HEDGES)

def in_rect(p, r, margin=0.0):
    return min(r[0], r[2]) - margin <= p[0] <= max(r[0], r[2]) + margin and min(r[1], r[3]) - margin <= p[1] <= max(r[1], r[3]) + margin

def near_building(p, margin):
    for (_, x, z, w, d) in BUILDINGS:
        if in_rect(p, [x, z, x + w, z + d], margin):
            return True
    return in_rect(p, FORT, margin)

def near_fort_walls(p, margin):
    x0, z0, x1, z1 = FORT
    corners = [[x0, z0], [x1, z0], [x1, z1], [x0, z1]]
    return any(seg_dist(p, corners[i], corners[(i + 1) % 4]) < margin for i in range(4))

# ---------------------------------------------------------------------------------------------------------------------
# Cover: hand-placed where it makes a place (the camp, the scrapyard, the farm), scattered over the fields and woods.

PROPS = []
def P(prop, x, z, yaw=None):
    d = {"prop": prop, "position_m": [x, 0, z]}
    if yaw is not None:
        d["yaw_deg"] = yaw
    PROPS.append(d)

# The camp: containers in a rough square, sandbags between them, drums and a generator.
P("container_green", -142, 30, 0); P("container_red", -142, 46, 0); P("container_blue", -122, 54, 90)
P("container_green", -104, 54, 90); P("sandbag_wall", -132, 30, 90); P("sandbag_wall", -112, 26, 0)
P("sandbag_wall", -100, 44, 90); P("oil_drum", -126, 42); P("oil_drum_rust", -126.7, 42.5); P("generator", -116, 46)
P("cable_reel", -138, 18, 30); P("pallet_stack", -106, 30); P("crate_large", -104, 32, 15); P("tyre_stack", -148, 58)
# The scrapyard: heaps of scrap and rubble, wrecks, tyres and skips round the boiler house.
P("scrap_pile", 100, 46, 20); P("scrap_pile", 140, 30, -30); P("rubble_pile", 134, 74, 10); P("rubble_pile", 104, 76, -20)
P("car_wreck", 128, 34, 70); P("car_wreck", 144, 52, 160); P("car_wreck", 98, 62, 15); P("car_wreck", 120, 70, 100)
P("tyre_stack", 108, 30); P("tyre_stack", 108.8, 30.4); P("tyre_stack", 146, 70); P("skip_bin", 136, 44, 90)
P("skip_bin", 112, 60, 0); P("container_red", 146, 26, 90); P("chimney", 126.5, 45.5); P("oil_drum_tipped", 104, 54, 40)
P("lorry_cab", 132, 58, 200)
# The farm: a trailer and stacks in the yard, crates and tyres by the barn and the farmhouse.
P("lorry_trailer", -30, 56, 90); P("pallet_stack", -44, 80); P("pallet_stack_tall", -42.6, 80.4, 8); P("crate_large", -20, 82, 20)
P("crate_small", -18.8, 82.6); P("tyre_stack", 30, 66); P("tyre_stack", 30.7, 66.4); P("cable_reel", 44, 76, 15)
P("oil_drum", 8, 88); P("oil_drum_rust", 8.6, 88.5); P("forklift", -50, 92, 70); P("generator", 26, 94)
P("sandbag_wall", -30, 96, 0); P("concrete_barrier", 18, 100, 0); P("concrete_barrier", 22, 100, 0)
# The fort's inside: crates and sandbags to fight from.
P("sandbag_wall", 22, -80, 0); P("crate_large", 40, -96, 10); P("crate_small", 41.2, -95.2); P("pallet_stack", 20, -98)
P("oil_drum", 38, -80); P("sandbag_wall", 40, -86, 90)

FIXED = list(PROPS)

# Nothing placed by hand may stand on a track (a buggy would stop at it).
for d in FIXED:
    x, z = d["position_m"][0], d["position_m"][2]
    reach = {"lorry_trailer": 6.5, "lorry_cab": 3.0, "container_red": 3.2, "container_green": 3.2, "container_blue": 3.2}.get(d["prop"], 1.5)
    if near_road([x, z], reach):
        raise SystemExit(f"{d['prop']} at ({x}, {z}) stands on a track")

def free(p, r, placed, spacing):
    if not in_rect(p, [-FX + 4, -FZ + 4, FX - 4, FZ - 4]):
        return False
    if near_road(p, r + 2.0) or near_hedge(p, r + 1.5) or near_building(p, r + 3.0) or near_fort_walls(p, r + 2.0):
        return False
    for s in PLAYER_SPAWNS:
        if math.hypot(p[0] - s["position_m"][0], p[1] - s["position_m"][2]) < 12:
            return False
    for v in VEHICLES:
        if math.hypot(p[0] - v["position_m"][0], p[1] - v["position_m"][2]) < 6:
            return False
    for o in OPPONENT_SPAWNS:
        if math.hypot(p[0] - o["position_m"][0], p[1] - o["position_m"][2]) < 2.5 + r:
            return False
    for q in placed:
        if math.hypot(p[0] - q[0], p[1] - q[1]) < spacing + r + q[2]:
            return False
    return True

# How much room each kind of cover takes (m, a rough radius).
SIZE = {"tyre_stack": 0.7, "pallet_stack": 0.8, "pallet_stack_tall": 0.8, "cable_reel": 1.1, "sandbag_wall": 1.7,
        "car_wreck": 2.5, "oil_drum": 0.4, "crate_large": 0.9, "concrete_barrier": 1.1, "skip_bin": 2.1,
        "container_blue": 6.2, "container_green": 6.2, "container_red": 6.2, "scrap_pile": 3.0, "rubble_pile": 3.0,
        "lorry_trailer": 7.0, "generator": 1.2, "forklift": 1.8, "chimney": 1.5, "lorry_cab": 3.0, "crate_small": 0.5,
        "oil_drum_rust": 0.4, "oil_drum_tipped": 0.6, "pallet_racking": 2.0}

placed = [[d["position_m"][0], d["position_m"][2], SIZE.get(d["prop"], 1.0)] for d in FIXED]
rng = random.Random(5150)

def scatter(rect, count, kinds, spacing, avoid=()):
    made, tries = 0, 0
    while made < count and tries < count * 200:
        tries += 1
        kind = rng.choice(kinds)
        r = SIZE.get(kind, 1.0)
        p = [round(rng.uniform(rect[0], rect[2]), 1), round(rng.uniform(rect[1], rect[3]), 1)]
        if any(in_rect(p, a, r + 1.0) for a in avoid) or not free(p, r, placed, spacing):
            continue
        yaw = rng.choice([0, 15, 30, 45, 60, 75, 90, 105, 120, 135, 150, 165])
        P(kind, p[0], p[1], yaw)
        placed.append([p[0], p[1], r])
        made += 1
    return made

FIELD_COVER = ["tyre_stack", "tyre_stack", "pallet_stack", "pallet_stack_tall", "cable_reel", "cable_reel", "sandbag_wall",
               "sandbag_wall", "car_wreck", "crate_large", "concrete_barrier", "oil_drum", "skip_bin"]
WOOD_COVER = ["sandbag_wall", "pallet_stack", "cable_reel", "tyre_stack", "crate_large", "pallet_stack_tall"]
WOOD_RECTS = [w["rect_m"] for w in WOODS]

# The middle field round the crossroads, the north field round the fort, the fields east and west of the farm,
# the strips between the woods and the ring.
scatter([-58, -54, 58, 26], 34, FIELD_COVER, 7.0, avoid=WOOD_RECTS)
scatter([-58, -118, 58, -62], 22, FIELD_COVER, 7.0)
scatter([-160, 0, -66, 66], 12, FIELD_COVER, 8.0, avoid=[[-150, 8, -92, 64]])
scatter([66, -18, 160, 86], 10, FIELD_COVER, 8.0, avoid=[[92, 22, 152, 84]])
scatter([-58, 34, 62, 118], 10, FIELD_COVER, 7.0, avoid=[[-54, 50, 44, 98]])
# A few bunkers in the woods.
for w in WOODS:
    scatter(w["rect_m"], 8 if w["rect_m"][2] - w["rect_m"][0] > 60 else 3, WOOD_COVER, 9.0)

# ---------------------------------------------------------------------------------------------------------------------
# Woods: kept off the tracks, the hedges, the buildings, the cover and the starts.

def clearings(wood):
    x0, z0, x1, z1 = wood["rect_m"]
    out = [list(g) for g in wood["glades"]]
    for r in ROADS:
        pts = r["points_m"]
        m = r["width_m"] / 2 + 2.0
        for i in range(len(pts) - 1):
            a, b = pts[i], pts[i + 1]
            box = [min(a[0], b[0]) - m, min(a[1], b[1]) - m, max(a[0], b[0]) + m, max(a[1], b[1]) + m]
            if box[2] >= x0 and box[0] <= x1 and box[3] >= z0 and box[1] <= z1:
                out.append(box)
    for h in HEDGES:
        a, b = h
        box = [min(a[0], b[0]) - 2, min(a[1], b[1]) - 2, max(a[0], b[0]) + 2, max(a[1], b[1]) + 2]
        if box[2] >= x0 and box[0] <= x1 and box[3] >= z0 and box[1] <= z1:
            out.append(box)
    for (_, bx, bz, bw, bd) in BUILDINGS:
        box = [bx - 4, bz - 4, bx + bw + 4, bz + bd + 4]
        if box[2] >= x0 and box[0] <= x1 and box[3] >= z0 and box[1] <= z1:
            out.append(box)
    for d in PROPS:
        x, z = d["position_m"][0], d["position_m"][2]
        r = SIZE.get(d["prop"], 1.0) + 1.5
        if in_rect([x, z], wood["rect_m"], r):
            out.append([x - r, z - r, x + r, z + r])
    for s in OPPONENT_SPAWNS:
        x, z = s["position_m"][0], s["position_m"][2]
        if in_rect([x, z], wood["rect_m"], 3):
            out.append([x - 2.5, z - 2.5, x + 2.5, z + 2.5])
    for v in VEHICLES:
        x, z = v["position_m"][0], v["position_m"][2]
        if in_rect([x, z], wood["rect_m"], 5):
            out.append([x - 4, z - 4, x + 4, z + 4])
    return [[round(c, 1) for c in box] for box in out]

def woods():
    out = []
    for w in WOODS:
        out.append({"name": w["name"], "rect_m": w["rect_m"], "spacing_m": w["spacing_m"], "gaps": w["gaps"],
                    "trunkRadius_m": [0.12, 0.36], "height_m": [9.0, 19.0], "seed": w["seed"], "clear_m": clearings(w)})
    return out

# ---------------------------------------------------------------------------------------------------------------------

def fort_walls():
    x0, z0, x1, z1 = FORT
    w, d = x1 - x0, z1 - z0
    # A doorway in the middle of each side; a gun port either side of it.
    openings = []
    for seg, length in ((0, w), (1, d), (2, w), (3, d)):
        openings.append({"segment": seg, "at_m": length / 2, "width_m": 2.0, "kind": "gap"})
        for side in (-1, 1):
            openings.append({"segment": seg, "at_m": length / 2 + side * length / 4, "width_m": 1.2, "sill_m": 1.1,
                             "height_m": 0.4, "kind": "window"})
    return {"baseElevation_m": 0, "height_m": 2.2, "points_m": [[x0, z0], [x1, z0], [x1, z1], [x0, z1]], "closed": True,
            "thickness_m": 0.2, "material": "wood_planks", "openings": openings}

def fence():
    return {"baseElevation_m": 0, "height_m": 2.0, "points_m": [[-FX, -FZ], [FX, -FZ], [FX, FZ], [-FX, FZ]], "closed": True,
            "thickness_m": 0.12, "material": "wood_planks",
            "openings": [
                {"segment": 0, "at_m": FX, "width_m": GATE, "kind": "gap"},           # the north gate, x 0
                {"segment": 1, "at_m": FZ - 14, "width_m": GATE, "kind": "gap"},      # the east gate, z -14
                {"segment": 2, "at_m": FX, "width_m": GATE, "kind": "gap"},           # the south gate, x 0
                {"segment": 3, "at_m": FZ + 8, "width_m": GATE, "kind": "gap"},       # the west gate, z -8
            ]}

def hedges():
    return [{"baseElevation_m": 0, "height_m": 1.8, "points_m": h, "thickness_m": 0.9, "material": "hedge"} for h in HEDGES]

body = {
    "id": "thornwick_woods",
    "displayName": "Thornwick Woods",
    "description": "A woodsball site on old farmland, seven times the size of the works: four woods, hedged fields, a timber fort with a lookout, a farm, a camp of containers and huts, a scrapyard, and gravel tracks to drive the site's buggies along.",
    "boundsMin_m": [-185, -2, -135],
    "boundsMax_m": [185, 40, 135],
    "groundMaterial": "grass_field",
    # The bots' grid, a little coarser than the other levels' (the kit's doors still fit): it loads in half the time.
    "navCellSize_m": 0.35,
    "groundPatches": [
    ] + [{"rect_m": w["rect_m"], "material": "leaf_litter"} for w in WOODS] + [
        {"rect_m": [-54, 50, 44, 98], "material": "dirt"},        # the farmyard
        {"rect_m": [-150, 8, -92, 64], "material": "dirt"},       # the camp
        {"rect_m": [92, 22, 152, 84], "material": "gravel"},      # the scrapyard
        {"rect_m": [16.2, -101.8, 43.8, -74.2], "material": "dirt"},  # inside the fort
    ],
    "playerSpawns": PLAYER_SPAWNS,
    "spawnArea": "the site",
    "deadZone_m": [-14, 0, 130],
    "scenery": {
        "powerLines": [{"points_m": [[-600, -260], [-150, -240], [600, -250]], "spacing_m": 240, "height_m": 28,
                        "material": "steel_galvanised", "wire": "steel_painted"}],
        "poleLines": [{"points_m": [[-400, 142], [400, 142]], "spacing_m": 45, "height_m": 8.5, "material": "wood_planks",
                       "wire": "steel_painted"}],
    },
    "buildings": [{"building": b[0], "position_m": [b[1], 0, b[2]]} for b in BUILDINGS],
    "walls": [fence(), fort_walls()] + hedges(),
    "roads": ROADS,
    "woods": woods(),
    "vehicles": VEHICLES,
    "props": PROPS,
    "areas": [
        {"name": "outside the fence", "rect_m": [-185, -135, 185, 135]},
        {"name": "the site", "rect_m": [-FX, -FZ, FX, FZ]},
        {"name": "the north field", "rect_m": [-62, -122, 62, -58]},
        {"name": "the fort", "rect_m": list(FORT)},
        {"name": "the middle field", "rect_m": [-62, -58, 62, 30]},
        {"name": "the crossroads", "rect_m": [-10, -20, 10, 0]},
        {"name": "the farm", "rect_m": [-62, 30, 66, 122]},
        {"name": "Blackthorn Wood", "rect_m": WOODS[0]["rect_m"]},
        {"name": "Rookery Wood", "rect_m": WOODS[1]["rect_m"]},
        {"name": "Hazel Copse", "rect_m": WOODS[2]["rect_m"]},
        {"name": "Long Spinney", "rect_m": WOODS[3]["rect_m"]},
        {"name": "the middle copse", "rect_m": WOODS[4]["rect_m"]},
        {"name": "the camp", "rect_m": [-150, 8, -92, 64]},
        {"name": "the scrapyard", "rect_m": [92, 22, 152, 84]},
    ],
    "opponentSpawns": OPPONENT_SPAWNS,
    "patrols": PATROLS,
    "places": [
        {"id": "whole", "displayName": "The whole site", "still": "over the site",
         "description": "All of it, inside the fence: the four woods, the fields and hedges, the fort, the farm, the camp and the scrapyard, with buggies at the gate, the camp, the scrapyard and the north field. You come in at any of the four gates."},
        {"id": "farm", "displayName": "The farm", "still": "the farmyard",
         "description": "The south of the site: the barn, the farmhouse and the pump house round the farmyard, Hazel Copse and Long Spinney either side, and the hedge along the north. You come in at the south gate, where two buggies stand.",
         "rect_m": [-70, 30, 72, 134], "deadZone_m": [-14, 0, 130], "spawnScale": 0.6},
        {"id": "north_field", "displayName": "The north field and the fort", "still": "the fort",
         "description": "The timber fort with its lookout, the open field round it and the hedge to the south. You come in at the north gate.",
         "rect_m": [-66, -134, 66, -56], "deadZone_m": [-14, 0, -130], "spawnScale": 0.6},
        {"id": "blackthorn", "displayName": "Blackthorn Wood", "still": "in Blackthorn Wood",
         "description": "The big wood in the north-west: trees close enough to hide behind, a glade with a hut, bunkers of sandbags and pallets, and the ring track along its edge. You come in at the west gate.",
         "rect_m": [-184, -122, -60, -2], "deadZone_m": [-178, 0, -18], "spawnScale": 0.6},
        {"id": "camp", "displayName": "The camp", "still": "the camp",
         "description": "The west of the site: containers, the guardhouse and the camp office in a square of sandbags, Hazel Copse to the south and the west track to the crossroads. You come in at the west gate, where a buggy stands.",
         "rect_m": [-184, -20, -60, 122], "deadZone_m": [-178, 0, 2], "spawnScale": 0.6},
        {"id": "scrapyard", "displayName": "The scrapyard", "still": "the scrapyard",
         "description": "The east of the site: heaps of scrap and wrecks round the old boiler house, the quarry track and the edge of Rookery Wood. You come in at the east gate.",
         "rect_m": [60, -26, 184, 122], "deadZone_m": [178, 0, -2], "spawnScale": 0.6},
    ],
    "pickups": PICKUPS,
    "viewpoints": [
        {"name": "over the site", "position_m": [-40, 62, 150], "yaw_deg": -8, "pitch_deg": -30},
        {"name": "the south gate", "position_m": [0, 1.7, 126], "yaw_deg": 0, "pitch_deg": -2},
        {"name": "the crossroads", "position_m": [6, 1.7, 6], "yaw_deg": 25, "pitch_deg": 0},
        {"name": "in Blackthorn Wood", "position_m": [-96, 1.7, -46], "yaw_deg": 40, "pitch_deg": 3},
        {"name": "the fort", "position_m": [24, 1.7, -60], "yaw_deg": -10, "pitch_deg": 3},
        {"name": "from the lookout", "position_m": [30, 4.9, -89], "yaw_deg": 180, "pitch_deg": -8},
        {"name": "the farmyard", "position_m": [34, 1.7, 54], "yaw_deg": 104, "pitch_deg": 0},
        {"name": "the camp", "position_m": [-92, 1.7, 26], "yaw_deg": 106, "pitch_deg": 0},
        {"name": "the scrapyard", "position_m": [98, 1.7, 16], "yaw_deg": -146, "pitch_deg": 0},
    ],
    "objectives": {
        "caseSpots_m": [[-33, 0, 66], [30, 3.2, -90.6], [-113, 0, 39.5], [117, 0, 45], [-98.5, 0, -66.7], [38, 0, 90.5]],
        "exits": [
            {"name": "the south gate", "at_m": [0, 0, 126]},
            {"name": "the north gate", "at_m": [0, 0, -126]},
            {"name": "the west gate", "at_m": [-176, 0, -8]},
            {"name": "the east gate", "at_m": [176, 0, -14]},
        ],
        "holdRooms": ["signal box, upstairs", "yard office", "the boiler house", "goods shed", "pump house"],
    },
}

HEADER = """// Level 5: Thornwick Woods (Phase 5), a woodsball site on old farmland. Generated by tools/levels/thornwick_woods.py:
// edit that and run it, not this file.
// World frame: metres, x east, z south (north is -Z), y up. A timber fence encloses x -172...172, z -122...122 (about
// seven times Oxbarrow Works), with a gate in each side: you come in at any of them. Gravel tracks cross at the
// crossroads (0, -10) and a dirt ring runs inside the fence; buggies (vehicles.jsonc) stand at the south gate, the camp,
// the scrapyard and in the north field, and bots drive them along the tracks. The woods are grown from their seeds
// (each trunk paint, sight and walking meet; the crowns are looks only), clear of the tracks, hedges and cover.
// Perimeter fence segments: 0 north (x -172->172, at_m = x + 172), 1 east (z -122->122, at_m = z + 122),
// 2 south (x 172->-172, at_m = 172 - x), 3 west (z 122->-122, at_m = 122 - z)."""

def write(path):
    lines = [HEADER, "{"]
    keys = list(body.keys())
    for i, k in enumerate(keys):
        v = body[k]
        comma = "," if i + 1 < len(keys) else ""
        if isinstance(v, list) and v and isinstance(v[0], dict):
            lines.append(f'  "{k}": [')
            for j, item in enumerate(v):
                lines.append("    " + obj(item) + ("," if j + 1 < len(v) else ""))
            lines.append("  ]" + comma)
        elif isinstance(v, dict):
            lines.append(f'  "{k}": {{')
            sub = list(v.keys())
            for j, sk in enumerate(sub):
                sv = v[sk]
                c2 = "," if j + 1 < len(sub) else ""
                if isinstance(sv, list) and sv and isinstance(sv[0], dict):
                    lines.append(f'    "{sk}": [')
                    for m, item in enumerate(sv):
                        lines.append("      " + obj(item) + ("," if m + 1 < len(sv) else ""))
                    lines.append("    ]" + c2)
                else:
                    lines.append(f'    "{sk}": ' + (arr(sv) if isinstance(sv, list) else (obj(sv) if isinstance(sv, dict) else fmt(sv))) + c2)
            lines.append("  }" + comma)
        else:
            lines.append(f'  "{k}": ' + (arr(v) if isinstance(v, list) else fmt(v)) + comma)
    lines.append("}")
    open(path, "w").write("\n".join(lines) + "\n")

write(f"{OUT}/thornwick_woods.jsonc")
print(f"{len(PROPS)} props ({len(PROPS) - len(FIXED)} scattered), {len(ROADS)} tracks, {len(WOODS)} woods, {len(HEDGES)} hedges")
