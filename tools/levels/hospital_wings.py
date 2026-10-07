# Generates the hospital's two three-storey wings, game/data/kit/buildings/north_wing.jsonc and west_wing.jsonc, whose
# walls, doors, stair holes and furniture repeat floor by floor with variations. Edit this and run it, not the files:
#   python3 tools/levels/hospital_wings.py game/data/kit/buildings
import json, sys

ST = 3.4          # storey height
OUT = sys.argv[1]

def fmt(v):
    if isinstance(v, float):
        s = f"{v:.3f}".rstrip('0').rstrip('.')
        return s if s not in ('-0', '') else '0'
    return json.dumps(v)

def obj(d, indent=0):
    # one-line JSON object with compact numbers
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

def door(seg, at, w, h=2.1, leaf=None):
    d = {"segment": seg, "at_m": at, "width_m": w, "height_m": h, "kind": "door"}
    if leaf: d["leaf"] = leaf
    return d

def win(seg, at, w=1.6, sill=0.9, h=1.4):
    return {"segment": seg, "at_m": at, "width_m": w, "sill_m": sill, "height_m": h, "kind": "window"}

SWING = {"door": "swing", "hinge": "start", "swing": "both", "double": True}
def fire(hinge="start", swing="left"): return {"door": "steel", "hinge": hinge, "swing": swing, "start": "random"}
def panel(hinge="start", swing="left"): return {"door": "panel", "hinge": hinge, "swing": swing, "start": "random"}

def wall(storey, pts, mat, thick, openings=None, closed=False, base=None, height=None, style=None):
    d = {"storey": storey}
    if base is not None: d["baseElevation_m"] = base
    if height is not None: d["height_m"] = height
    if style: d["style"] = style
    d["points_m"] = pts
    if closed: d["closed"] = True
    d["thickness_m"] = thick
    d["material"] = mat
    if openings: d["openings"] = openings
    return d

def railing(storey, pts):
    return wall(storey, pts, "steel_painted", 0.05, base=ST * storey, height=1.0, style="railing")

def write(path, header, body):
    lines = [header, "{"]
    keys = list(body.keys())
    for i, k in enumerate(keys):
        v = body[k]
        comma = "," if i < len(keys) - 1 else ""
        if isinstance(v, list) and v and isinstance(v[0], (dict, list)) and k != "footprint_m" and k != "storeys_m":
            lines.append(f'  "{k}": [')
            for j, item in enumerate(v):
                c2 = "," if j < len(v) - 1 else ""
                if isinstance(item, str):
                    lines.append("    " + item + c2)
                else:
                    lines.append("    " + (obj(item) if isinstance(item, dict) else arr(item)) + c2)
            lines.append("  ]" + comma)
        elif isinstance(v, dict):
            lines.append(f'  "{k}": {obj(v)}{comma}')
        elif isinstance(v, list):
            lines.append(f'  "{k}": {arr(v)}{comma}')
        else:
            lines.append(f'  "{k}": {fmt(v)}{comma}')
    lines.append("}")
    open(path, "w").write("\n".join(lines) + "\n")

# ------------------------------------------------------------------ north wing
# Local frame: x 0..56 west→east, z 0..14 north→south. Corridor z 5.8..8.2. The east end (x 43..56) is fallen in: the
# ground floor there is one ruined room open to the sky, the first floor ends at x 46, the top floor at x 42.5 and the
# roof at x 40. The west end sits over the west wing (x 0..14 of the south wall is shared: the passage at x 7 leads in).
END = {0: 43.0, 1: 43.0, 2: 42.5}     # where the interior walls stop on each storey
SLAB = {1: 46.0, 2: 42.5}
walls = []
for s in range(3):
    base = ST * s
    # Exterior.
    if s == 0:
        north = [win(0, 3.5, 1.0, 1.9, 0.8), win(0, 10, ), win(0, 16.5), win(0, 26), win(0, 30), win(0, 33.5), win(0, 39.5, 1.0, 1.9, 0.8), win(0, 47), win(0, 52),
                 door(0, 18.5, 1.2, 2.2, fire("end", "left"))]
        walls.append(wall(0, [[0, 0], [56, 0], [56, 14], [0, 14]], "render_cream", 0.3, north + [
            door(1, 7, 4.0, 3.0),                                    # the breach into the fallen end
            win(2, 56 - 16), win(2, 56 - 20), win(2, 56 - 24),       # outpatients, over the courtyard
            door(2, 56 - 30, 1.8, 2.3, SWING),                       # the garden door
            win(2, 56 - 35), win(2, 56 - 39), win(2, 56 - 48), win(2, 56 - 52),
            door(2, 56 - 7, 2.4, 2.4),                               # the passage into the west wing
            win(3, 14 - 3, 1.0, 1.9, 0.8), door(3, 14 - 7, 1.2, 2.2, fire("start", "left")), win(3, 14 - 11)
        ], closed=True))
    else:
        e = SLAB[s]
        opens_n = [win(0, 3.5, 1.0, 1.9, 0.8), win(0, 10), win(0, 16.5), win(0, 26), win(0, 30), win(0, 33.5), win(0, 39.5, 1.0, 1.9, 0.8)]
        opens_n = [o for o in opens_n if o["at_m"] + o["width_m"] / 2 < e - 0.3]
        walls.append(wall(s, [[e, 0], [0, 0]], "render_cream", 0.3,
                          [dict(o, at_m=e - o["at_m"]) for o in opens_n]))           # north, running west
        walls.append(wall(s, [[0, 0], [0, 14]], "render_cream", 0.3,
                          [win(0, 3, 1.0, 1.9, 0.8), win(0, 7, 1.2), win(0, 11)]))  # west, running south
        opens_s = [door(0, 7, 2.4, 2.4), win(0, 16), win(0, 20), win(0, 24), win(0, 30), win(0, 35), win(0, 39)]
        opens_s = [o for o in opens_s if o["at_m"] + o["width_m"] / 2 < e - 0.3]
        walls.append(wall(s, [[0, 14], [e, 14]], "render_cream", 0.3, opens_s))      # south, running east
    end = END[s]
    # Corridor walls.
    ncorr = [door(0, 3.5 - 0.15, 1.2, 2.2, fire("start", "right")), door(0, 13.5 - 0.15, 1.6, 2.2, SWING),
             door(0, 21.25 - 0.15, 1.2, 2.2), door(0, 29 - 0.15, 1.6, 2.2, SWING), door(0, 39.5 - 0.15, 1.2, 2.2, fire("end", "right"))]
    walls.append(wall(s, [[0.15, 5.8], [end, 5.8]], "paint_green", 0.15, ncorr))
    scorr = [door(0, 3 - 0.15, 0.9, 2.1, panel("start", "right")), door(0, 7 - 0.15, 2.4, 2.4), door(0, 18 - 0.15, 1.6, 2.2, SWING),
             (door(0, 30 - 0.15, 2.0, 2.4) if s == 0 else door(0, 30 - 0.15, 0.9, 2.1, panel("end", "right"))),
             door(0, 37 - 0.15, 1.6, 2.2, SWING)]
    walls.append(wall(s, [[0.15, 8.2], [end, 8.2]], "paint_green", 0.15, scorr))
    # North side partitions: stairs A | N1 | lift | N2 | stairs B (| the fallen end on the ground floor).
    walls.append(wall(s, [[7, 0.15], [7, 5.725]], "paint_cream", 0.12))
    walls.append(wall(s, [[20, 5.725], [20, 3.3], [22.5, 3.3], [22.5, 5.725]], "concrete_wall", 0.15))   # the lift shaft
    walls.append(wall(s, [[22.5, 0.15], [22.5, 3.3]], "paint_cream", 0.12))
    walls.append(wall(s, [[36, 0.15], [36, 5.725]], "paint_cream" if s != 1 else "tiles_white", 0.12))
    walls.append(wall(s, [[end, 0.15], [end, 5.725]], "paint_cream", 0.15))
    # South side partitions: corner room | passage | S1 | lobby | S2.
    walls.append(wall(s, [[5.8, 8.275], [5.8, 13.85]], "paint_cream", 0.12))
    walls.append(wall(s, [[8.2, 8.275], [8.2, 13.85]], "paint_cream", 0.12))
    walls.append(wall(s, [[28, 8.275], [28, 13.85]], "paint_cream", 0.12))
    walls.append(wall(s, [[32, 8.275], [32, 13.85]], "paint_cream", 0.12))
    if s == 0:
        walls.append(wall(0, [[end, 8.275], [end, 13.85]], "paint_cream", 0.15))
# The theatre's tiled walls (first floor, x 22.5..36 north side) are the partitions above; railings round the stair holes.
walls += [railing(1, [[1.9, 1.4], [1.9, 5.725]]), railing(1, [[37.9, 1.4], [37.9, 5.725]]),
          railing(2, [[4.7, 5.0], [4.7, 1.35], [6.25, 1.35]]), railing(2, [[40.7, 5.0], [40.7, 1.35], [42.25, 1.35]])]

floors = [
    {"elevation_m": ST, "rect_m": [0, 0, SLAB[1], 14], "holes_m": [[0.15, 1.4, 1.85, 5.725], [36.15, 1.4, 37.85, 5.725], [20.1, 3.4, 22.4, 5.7]], "material": "lino"},
    {"elevation_m": 2 * ST, "rect_m": [0, 0, SLAB[2], 14], "holes_m": [[4.75, 1.4, 6.25, 5.0], [40.75, 1.4, 42.25, 5.0], [20.1, 3.4, 22.4, 5.7]], "material": "lino"},
]
stairs = [
    {"start_m": [1.1, 5.0], "yaw_deg": 0, "width_m": 1.4, "run_m": 3.6, "rise_m": ST, "steps": 18, "material": "concrete_floor"},
    {"start_m": [5.5, 1.4], "yaw_deg": 180, "width_m": 1.4, "run_m": 3.6, "rise_m": ST, "steps": 18, "baseElevation_m": ST, "material": "concrete_floor"},
    {"start_m": [37.1, 5.0], "yaw_deg": 0, "width_m": 1.4, "run_m": 3.6, "rise_m": ST, "steps": 18, "material": "concrete_floor"},
    {"start_m": [41.5, 1.4], "yaw_deg": 180, "width_m": 1.4, "run_m": 3.6, "rise_m": ST, "steps": 18, "baseElevation_m": ST, "material": "concrete_floor"},
]
def P(prop, x, y, z, yaw=None):
    d = {"prop": prop, "position_m": [x, y, z]}
    if yaw is not None: d["yaw_deg"] = yaw
    return d
def beds_along_south(x0, x1, y, curtains=True):
    # South-side wards: heads against the corridor wall, so the windows over the courtyard stay clear.
    out = []
    n = int((x1 - x0) / 3.0)
    for k in range(n):
        x = x0 + 1.5 + k * 3.0
        out.append(P("hospital_bed", x, y, 9.3, 0))
        if curtains and k < n - 1:
            out.append(P("curtain_screen", x + 1.5, y, 9.5, 90))
    return out
props = []
# Ground floor.
props += [P("desk", 9.5, 0, 2.0), P("filing_cabinet", 7.6, 0, 0.6, 180), P("filing_cabinet", 8.3, 0, 0.6, 180), P("shelving_rack", 11.0, 0, 0.6, 180),
          P("shelving_rack", 13.7, 0, 0.6, 180), P("desk_flipped", 12.5, 0, 4.0, 30)]                         # records office
props += [P("operating_table", 29, 0, 2.6, 90), P("generator", 33.8, 0, 1.2), P("locker_bank", 24.0, 0, 0.45, 180)]  # x-ray room
props += [P("bench", 11.5, 0, 10.6), P("bench", 15.0, 0, 10.6), P("bench", 18.5, 0, 10.6), P("bench", 22.0, 0, 10.6),
          P("bench", 13.0, 0, 12.8), P("bench", 20.0, 0, 12.8), P("wheelchair", 25.8, 0, 9.4, 40), P("medical_trolley", 9.2, 0, 13.2)]  # outpatients
props += [P("wheelchair", 30.2, 0, 11.6, -20)]                                                               # garden door
props += [P("shelving_rack", 34.5, 0, 13.4), P("shelving_rack", 37.2, 0, 13.4), P("desk", 40.2, 0, 10.2, 90), P("crate_small", 33.2, 0, 9.0)]  # pharmacy
props += [P("locker_bank", 2.9, 0, 13.55), P("desk", 2.0, 0, 10.0, 90)]                                         # porters' room
props += [P("rubble_pile", 47, 0, 4, 20), P("rubble_pile", 51.5, 0, 9, -30), P("scrap_pile", 49, 0, 11.5, 60), P("rubble_pile", 54, 0, 3, 0),
          P("desk_flipped", 45.5, 0, 9.5, 80), P("hospital_bed", 52.5, 0, 6.5, 70)]                           # the fallen end
# First floor.
y1 = ST
props += [P("medical_trolley", 9.0, y1, 1.0), P("medical_trolley", 11.0, y1, 1.2, 20), P("locker_bank", 16.0, y1, 0.45, 180), P("wheelchair", 18.6, y1, 4.4, 70)]  # anaesthetic room
props += [P("operating_table", 29, y1, 2.6, 90), P("medical_trolley", 26.2, y1, 1.0), P("medical_trolley", 32.6, y1, 4.6, 15), P("locker_bank", 34.9, y1, 1.2, -90)]  # theatre
props += beds_along_south(8.4, 27.8, y1)                                                                       # recovery ward
props += [P("medical_trolley", 30.0, y1, 12.8)]                                                                 # sluice
props += beds_along_south(32.2, 41.5, y1) + [P("rubble_pile", 44.6, y1, 7.0, 15)]                              # men's ward, and the broken floor
props += [P("crate_large", 2.0, y1, 12.6), P("crate_small", 3.4, y1, 12.8, 20)]                                 # linen room
# Top floor.
y2 = 2 * ST
props += [P("desk", 10.0, y2, 3.6), P("desk", 13.2, y2, 3.6, 180), P("filing_cabinet", 7.6, y2, 0.6, 180), P("locker_bank", 17.5, y2, 0.45, 180)]  # nurses' station
props += [P("hospital_bed", 24.8, y2, 1.2, 0), P("hospital_bed", 27.8, y2, 1.2, 0), P("hospital_bed", 30.8, y2, 1.2, 0), P("hospital_bed", 33.8, y2, 1.2, 0),
          P("curtain_screen", 26.3, y2, 1.4, 90), P("curtain_screen", 32.3, y2, 1.4, 90)]                      # children's ward
props += beds_along_south(8.4, 27.8, y2)                                                                       # women's ward
props += [P("bench", 30.0, y2, 13.2, 180), P("desk_flipped", 29.6, y2, 10.4, 15)]                              # day room
props += beds_along_south(32.2, 38.5, y2, curtains=False) + [P("rubble_pile", 40.4, y2, 9.0, 40)]              # the broken ward
props += [P("crate_small", 2.0, y2, 12.8), P("crate_small", 2.6, y2, 13.2, 30)]                                 # top floor store

def A(name, rect, s, light, indoor=True):
    lo = -1.0 if s == 0 else ST * s - 0.2
    hi = ST * (s + 1) - 0.2 if s < 2 else 10.6
    d = {"name": name, "rect_m": rect, "minY_m": lo, "maxY_m": hi}
    if indoor: d["indoor"] = True
    d["light"] = light
    return d
areas = [
    {"name": "the corner stairs", "rect_m": [0, 0, 7, 5.8], "minY_m": -1, "maxY_m": 10.6, "indoor": True, "light": 0.35},
    {"name": "the east stairs", "rect_m": [36, 0, 43, 5.8], "minY_m": -1, "maxY_m": 10.6, "indoor": True, "light": 0.35},
    A("the north corridor", [0, 5.8, 43, 8.2], 0, 0.3), A("the records office", [7, 0, 22.5, 5.8], 0, 0.45), A("the x-ray room", [22.5, 0, 36, 5.8], 0, 0.4),
    A("the porters' room", [0, 8.2, 5.8, 14], 0, 0.35), A("the outpatients", [8.2, 8.2, 28, 14], 0, 0.55), A("the garden door", [28, 8.2, 32, 14], 0, 0.5),
    A("the pharmacy", [32, 8.2, 43, 14], 0, 0.5), A("the fallen end", [43, 0, 56, 14], 0, 1.0, indoor=False),
    A("the north corridor, first floor", [0, 5.8, 43, 8.2], 1, 0.3), A("the anaesthetic room", [7, 0, 22.5, 5.8], 1, 0.45),
    A("the operating theatre", [22.5, 0, 36, 5.8], 1, 0.4), A("the linen room", [0, 8.2, 5.8, 14], 1, 0.35), A("the recovery ward", [8.2, 8.2, 28, 14], 1, 0.6),
    A("the sluice", [28, 8.2, 32, 14], 1, 0.45), A("the men's ward", [32, 8.2, 43, 14], 1, 0.6), A("the broken floor", [43, 0, 46, 14], 1, 1.0, indoor=False),
    A("the north corridor, top floor", [0, 5.8, 42.5, 8.2], 2, 0.3), A("the nurses' station", [7, 0, 22.5, 5.8], 2, 0.5),
    A("the children's ward", [22.5, 0, 36, 5.8], 2, 0.55), A("the top floor store", [0, 8.2, 5.8, 14], 2, 0.35), A("the women's ward", [8.2, 8.2, 28, 14], 2, 0.65),
    A("the day room", [28, 8.2, 32, 14], 2, 0.6), A("the broken ward", [32, 8.2, 42.5, 14], 2, 0.75),
]

def toned(areas):
    """What each room sounds like (presentation.jsonc audio.ambience.tones): wind through the wards' broken windows and
    up the stairwells, water dripping in the sluice and the pharmacy; the rest a quiet room."""
    for a in areas:
        name = a["name"]
        tone = "draught" if "ward" in name or "day room" in name or "stairs" in name else "drip" if "sluice" in name or "pharmacy" in name else ""
        if tone and a.get("indoor"):
            a["tone"] = tone
    return areas

areas = toned(areas)

north = {
    "id": "north_wing", "displayName": "North wing", "storeys_m": [ST, ST, ST], "slabThickness_m": 0.25, "footprint_m": [0, 0, 56, 14],
    "floorMaterial": "lino", "frames": "steel_painted", "gutters": "steel_painted", "fittings": "steel_painted", "ceilingLights": "enamel_white",
    "skirting": "paint_green",
    "markings": {"color": "#3d6b52", "lineWidth_m": 0.1, "stencils": [
        {"text": "THEATRE", "at_m": [29, 5.4, 5.71], "size_m": 0.3, "yaw_deg": 180, "wall": True},
        {"text": "OUTPATIENTS", "at_m": [18, 2.3, 8.29], "size_m": 0.3, "wall": True},
        {"text": "WARD 7", "at_m": [18, 5.7, 8.29], "size_m": 0.3, "wall": True},
        {"text": "WARD 9", "at_m": [18, 9.1, 8.29], "size_m": 0.3, "wall": True}]},
    "walls": walls, "floors": floors,
    "roof": {"rect_m": [-0.2, -0.2, 40.2, 14.2], "material": "roof_felt", "ceilingMaterial": "paint_cream", "parapet_m": 0.6, "parapetMaterial": "render_cream"},
    "stairs": stairs, "props": props, "areas": areas,
}
header = """// Hospital north wing: three storeys, 56 × 14 m, a corridor down the middle (z 5.8–8.2) with wards and rooms either side,
// a stairwell at the west end (the corner stairs, over the west wing) and one at x 36–43 (the east stairs), each two
// flights side by side; a lift shaft open down through every floor (x 20–22.5) beside the corridor; the operating
// theatre on the first floor. The east end has fallen in: the ground floor there is a ruin open to the sky (x 43–56,
// in through a breach in the end wall), the first floor ends at x 46, the top floor at x 42.5 and the roof at x 40.
// The south wall's west 14 m backs onto the west wing; the passage at x 7 leads into its corridor on every floor.
// Generated by tools/levels/hospital_wings.py: edit that, not this.
// Wall segments on the ground floor: 0 north, 1 east, 2 south (at_m = 56 − x), 3 west (at_m = 14 − z); upstairs the
// outer walls are open runs (north running west from the broken end, west, south running east)."""
write(f"{OUT}/north_wing.jsonc", header, north)

# ------------------------------------------------------------------ west wing
# Local frame: x 0..14 west→east, z 0..40 north→south; its north edge backs onto the north wing (no north wall of its
# own). Corridor x 5.8..8.2. Stairs C at the south-west (x 0..5.8, z 32..40). Ground floor: the main entrance hall at the
# south end (x 5.8..14, z 32..40) with its doors in the south wall.
walls = []
for s in range(3):
    ext = [win(0, 4), win(0, 9), door(0, 20, 1.2, 2.2, fire("end", "left")) if s == 0 else win(0, 20), win(0, 15) if s else None, win(0, 26), win(0, 30),
           win(0, 36, 1.0, 1.9, 0.8),
           win(1, 3, 1.0, 1.9, 0.8), (door(1, 10, 2.0, 2.4, SWING) if s == 0 else win(1, 10)),
           win(2, 40 - 37), win(2, 40 - 33), win(2, 40 - 30), win(2, 40 - 26), win(2, 40 - 22), win(2, 40 - 18),
           (door(2, 40 - 14, 1.8, 2.3, SWING) if s == 0 else win(2, 40 - 14)), win(2, 40 - 9), win(2, 40 - 5)]
    ext = [o for o in ext if o]
    walls.append(wall(s, [[0, 0], [0, 40], [14, 40], [14, 0]], "render_cream", 0.3, ext))
    cend = 32.0 if s == 0 else 39.85
    wcorr = [door(0, 6, 1.6, 2.2, SWING), door(0, 18, 1.6, 2.2, SWING), door(0, 28, 1.2, 2.1, panel("start", "left")),
             door(0, 33.5, 1.2, 2.2, fire("end", "left"))]
    walls.append(wall(s, [[5.8, 0.15], [5.8, 39.85]], "paint_green", 0.15, wcorr))
    ecorr = [door(0, 6, 1.6, 2.2, SWING), (door(0, 14, 2.0, 2.4) if s == 0 else door(0, 14, 0.9, 2.1, panel("end", "right"))), door(0, 22, 1.6, 2.2, SWING)]
    if s == 0:
        walls.append(wall(0, [[8.2, 0.15], [8.2, 32]], "paint_green", 0.15, ecorr))
    else:
        ecorr.append(door(0, 34, 1.6, 2.2, SWING))
        walls.append(wall(s, [[8.2, 0.15], [8.2, 39.85]], "paint_green", 0.15, ecorr))
    # West side: W1 | W2 | W3 | stairs C. East side: E1 | lobby | E2 | E3 (on the ground floor E3 is the entrance hall's).
    for z in (12, 24, 32):
        walls.append(wall(s, [[0.15, z], [5.725, z]], "paint_cream", 0.12))
    for z in (12, 16, 28):
        walls.append(wall(s, [[8.275, z], [13.85, z]], "paint_cream", 0.12, [door(0, 2.8, 0.9, 2.1, panel("start", "left"))] if (s == 0 and z == 28) else None))
walls += [railing(1, [[2.0, 35.4], [2.0, 39.85]]), railing(2, [[3.8, 39.0], [3.8, 35.35], [5.35, 35.35]])]
floors = [
    {"elevation_m": ST, "rect_m": [0, 0, 14, 40], "holes_m": [[0.15, 35.4, 1.95, 39.85]], "material": "lino"},
    {"elevation_m": 2 * ST, "rect_m": [0, 0, 14, 40], "holes_m": [[3.85, 35.4, 5.35, 39.0]], "material": "lino"},
]
stairs = [
    {"start_m": [1.2, 39.0], "yaw_deg": 0, "width_m": 1.4, "run_m": 3.6, "rise_m": ST, "steps": 18, "material": "concrete_floor"},
    {"start_m": [4.6, 35.4], "yaw_deg": 180, "width_m": 1.4, "run_m": 3.6, "rise_m": ST, "steps": 18, "baseElevation_m": ST, "material": "concrete_floor"},
]
def beds_along_west(z0, z1, y, curtains=True):
    # West-side wards: heads against the corridor wall.
    out = []
    n = int((z1 - z0) / 3.0)
    for k in range(n):
        z = z0 + 1.5 + k * 3.0
        out.append(P("hospital_bed", 4.7, y, z, -90))
        if curtains and k < n - 1:
            out.append(P("curtain_screen", 4.55, y, z + 1.5))
    return out
def beds_along_east(z0, z1, y, curtains=True):
    # East-side wards: heads against the corridor wall, so the windows over the courtyard stay clear.
    out = []
    n = int((z1 - z0) / 3.0)
    for k in range(n):
        z = z0 + 1.5 + k * 3.0
        out.append(P("hospital_bed", 9.3, y, z, 90))
        if curtains and k < n - 1:
            out.append(P("curtain_screen", 9.45, y, z + 1.5))
    return out
props = []
# Ground floor: casualty, treatment rooms, plaster room, triage, the courtyard door, the waiting hall, the entrance hall.
props += beds_along_west(0.2, 11.8, 0) + beds_along_west(12.2, 23.8, 0, curtains=False)
props += [P("medical_trolley", 2.0, 0, 27.0), P("operating_table", 3.0, 0, 29.0, 90)]
props += [P("desk", 11.0, 0, 3.0, -90), P("wheelchair", 10.4, 0, 9.0, 30), P("locker_bank", 13.55, 0, 6.0, -90)]
props += [P("bench", 10.0, 0, 18.0, 90), P("bench", 10.0, 0, 21.0, 90), P("bench", 10.0, 0, 24.0, 90), P("bench", 12.8, 0, 19.5, -90), P("bench", 12.8, 0, 23.5, -90)]
props += [P("desk", 11.0, 0, 34.0), P("bench", 7.4, 0, 36.5, 90), P("wheelchair", 12.4, 0, 38.0, 200), P("medical_trolley", 9.0, 0, 30.0)]
# First floor: side wards, the long wards, the staff room.
y1 = ST
props += beds_along_west(0.2, 11.8, y1, curtains=False) + beds_along_west(12.2, 23.8, y1) + [P("locker_bank", 2.9, y1, 31.55, 180)]
props += beds_along_east(0.2, 11.8, y1) + [P("desk_flipped", 11.0, y1, 14.0, 20)] + beds_along_east(16.2, 27.8, y1) + beds_along_east(28.2, 39.8, y1)
# Top floor.
y2 = 2 * ST
props += beds_along_west(0.2, 11.8, y2) + beds_along_west(12.2, 23.8, y2, curtains=False) + [P("crate_large", 2.0, y2, 30.0)]
props += [P("desk", 11.0, y2, 3.0, -90), P("desk", 11.0, y2, 7.0, -90), P("bench", 11.0, y2, 14.0, 90)] + beds_along_east(16.2, 27.8, y2) + beds_along_east(28.2, 39.8, y2, curtains=False)

def A2(name, rect, s, light):
    lo = -1.0 if s == 0 else ST * s - 0.2
    hi = ST * (s + 1) - 0.2 if s < 2 else 10.6
    return {"name": name, "rect_m": rect, "minY_m": lo, "maxY_m": hi, "indoor": True, "light": light}
areas = [
    {"name": "the south stairs", "rect_m": [0, 32, 5.8, 40], "minY_m": -1, "maxY_m": 10.6, "indoor": True, "light": 0.35},
    A2("the west corridor", [5.8, 0, 8.2, 32], 0, 0.3), A2("the casualty bay", [0, 0, 5.8, 12], 0, 0.45), A2("the treatment rooms", [0, 12, 5.8, 24], 0, 0.45),
    A2("the plaster room", [0, 24, 5.8, 32], 0, 0.4), A2("the triage room", [8.2, 0, 14, 12], 0, 0.55), A2("the courtyard door", [8.2, 12, 14, 16], 0, 0.5),
    A2("the waiting hall", [8.2, 16, 14, 28], 0, 0.6), A2("the main entrance", [5.8, 28, 14, 40], 0, 0.6),
    A2("the west corridor, first floor", [5.8, 0, 8.2, 40], 1, 0.3), A2("the side wards", [0, 0, 5.8, 12], 1, 0.5), A2("the west ward", [0, 12, 5.8, 24], 1, 0.55),
    A2("the store room", [0, 24, 5.8, 32], 1, 0.35), A2("the east ward", [8.2, 0, 14, 12], 1, 0.6), A2("the staff room", [8.2, 12, 14, 16], 1, 0.5),
    A2("the long ward", [8.2, 16, 14, 28], 1, 0.65), A2("the south ward", [8.2, 28, 14, 40], 1, 0.6),
    A2("the west corridor, top floor", [5.8, 0, 8.2, 40], 2, 0.3), A2("the isolation rooms", [0, 0, 5.8, 12], 2, 0.5), A2("the top ward", [0, 12, 5.8, 24], 2, 0.55),
    A2("the top store", [0, 24, 5.8, 32], 2, 0.35), A2("the doctors' rooms", [8.2, 0, 14, 12], 2, 0.55), A2("the top kitchen", [8.2, 12, 14, 16], 2, 0.5),
    A2("the sun ward", [8.2, 16, 14, 28], 2, 0.7), A2("the south ward, top floor", [8.2, 28, 14, 40], 2, 0.65),
]
areas = toned(areas)
west = {
    "id": "west_wing", "displayName": "West wing", "storeys_m": [ST, ST, ST], "slabThickness_m": 0.25, "footprint_m": [0, 0, 14, 40],
    "floorMaterial": "lino", "frames": "steel_painted", "gutters": "steel_painted", "fittings": "steel_painted", "ceilingLights": "enamel_white",
    "skirting": "paint_green",
    "markings": {"color": "#3d6b52", "lineWidth_m": 0.1, "stencils": [
        {"text": "CASUALTY", "at_m": [10, 2.7, 40.16], "size_m": 0.4, "wall": True, "color": "#b33a32"},
        {"text": "MAIN ENTRANCE", "at_m": [10, 3.05, 40.16], "size_m": 0.22, "wall": True}]},
    "walls": walls, "floors": floors,
    "roof": {"rect_m": [-0.2, -0.2, 14.2, 40.2], "material": "roof_felt", "ceilingMaterial": "paint_cream", "parapet_m": 0.6, "parapetMaterial": "render_cream"},
    "stairs": stairs, "props": props, "areas": areas,
}
header = """// Hospital west wing: three storeys, 14 × 40 m, its north end backing onto the north wing (so no north wall of its own;
// its corridor, x 5.8–8.2, runs on through the passage in the north wing's south wall on every floor). Wards and rooms
// either side of the corridor; the south stairs (two flights side by side) at the south-west; on the ground floor the
// main entrance hall at the south end, its doors in the south wall, and a door onto the courtyard halfway along.
// Generated by tools/levels/hospital_wings.py: edit that, not this.
// Outer wall segments: 0 west (z 0→40, at_m = z), 1 south (x 0→14, at_m = x), 2 east (z 40→0, at_m = 40 − z)."""
write(f"{OUT}/west_wing.jsonc", header, west)
print("ok")
