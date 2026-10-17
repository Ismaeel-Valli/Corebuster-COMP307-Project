"""corebuster_env_040: North-West Room density and open-space plan.

Reads the accepted corebuster_env_029 survey, defines the bay map, open spaces, lanes and loops in
room-local metres (u east from the west wall, v north from the south wall), checks every planned
footprint against the measured movement-band reservations, projects the density metrics, and writes:

  Assets/FPS/Art/NorthWestRoom/NorthWestRoomDensityPlan.json  (world coordinates; read by NorthWestRoomDensity)
  Docs/NorthWestRoom/DensityPlan_040.svg                      (plan drawing, north up)

Usage: python Tools/Art/plan_north_west_room_density.py
Local authoring tool (Tools/Art/*.py is git-ignored); the JSON and SVG it writes are the deliverables.
"""
import json
import math
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SURVEY = os.path.join(ROOT, "Docs", "NorthWestRoom", "Measurement_029.json")
PLAN_OUT = os.path.join(ROOT, "Assets", "FPS", "Art", "NorthWestRoom", "NorthWestRoomDensityPlan.json")
SVG_OUT = os.path.join(ROOT, "Docs", "NorthWestRoom", "DensityPlan_040.svg")

S = json.load(open(SURVEY, encoding="utf-8"))
X0, X1 = S["interior"]["min"]["x"], S["interior"]["max"]["x"]
Z0, Z1 = S["interior"]["min"]["z"], S["interior"]["max"]["z"]
W, D = X1 - X0, Z1 - Z0
CU, CV = S["centre"]["x"] - X0, S["centre"]["z"] - Z0
R_OUT = S["ringOuterRadius"]
SPAWN_R = S["spawnRadius"]
HALF = S["connectorHalfWidth"]


def loc(p):
    return (p["x"] - X0, p["z"] - Z0)


def world(u, v):
    return {"x": round(X0 + u, 3), "z": round(Z0 + v, 3)}


SPAWNS = [loc(s["position"]) for s in S["spawns"]]
CONNECTORS = [(r["name"], [loc(c) for c in r["corners"]]) for r in S["routes"] if r["connector"]]
KEEPOUTS = [(g["name"] + " approach", g["approach"]) for g in S["gates"]]
KEEPOUTS += [("Red Terminal keep-out", S["redTerminal"]["keepOut"]), ("Master Terminal keep-out", S["masterTerminal"]["keepOut"])]
KEEPOUTS = [(n, (b["min"]["x"] - X0, b["min"]["z"] - Z0, b["max"]["x"] - X0, b["max"]["z"] - Z0)) for n, b in KEEPOUTS]

# ---------------------------------------------------------------- bay map
# (id, wall, u0, v0, u1, v1, body height band, intended content, owner)
EXISTING = "existing"
BAYS = [
    # South wall (Z min). Only wall with no door or terminal.
    ("S1", "South-West corner", 0.00, 0.00, 2.60, 3.40, "to 6.0 m", "P-3 pressure tank cluster with valve tree and vertical drops to the overhead (corner landmark)", "corebuster_env_033"),
    ("S2", "South", 2.70, 0.48, 11.50, 2.96, "to 2.74 m", "West_Turbine horizontal assembly (built)", "corebuster_env_031"),
    ("S3", "South", 11.65, 0.00, 14.55, 2.80, "to 6.0 m", "Pressure manifold and pump skid between the turbine and the spawn 2 strip; drops rise to the overhead", "corebuster_env_033"),
    ("S4", "South", 14.70, 0.00, 17.70, 1.10, "to 3.4 m (flush)", "Flush switchboard and drive panels (spawn 2 strip: wall-flush only)", "corebuster_env_041"),
    ("S5", "South", 17.85, 0.00, 21.20, 2.60, "to 6.0 m", "Pressure manifold, filter housings and valve drops; mirrors S3 in function, not in layout", "corebuster_env_033"),
    ("S6", "South", 21.32, 0.48, 30.12, 2.96, "to 2.74 m", "East_Generator horizontal assembly (built)", "corebuster_env_031"),
    ("S7", "South-East corner", 30.25, 0.00, 31.92, 3.00, "to 6.0 m", "Vertical drop stack and expansion tank in the corner beside the generator", "corebuster_env_033"),
    # East wall (X max). East door approach is reserved.
    ("E1", "East", 29.20, 3.10, 31.92, 7.05, "1.8 to 3.4 m", "Drive and cabinet row (variable-speed drives, plinth kerb); ends short of the east door approach", "corebuster_env_041"),
    ("E2", "East", 29.20, 11.45, 31.92, 14.25, "to 3.4 m", "Single tall switchgear unit beside the east door: silhouette marker at the entry", "corebuster_env_041"),
    ("E3", "East", 30.80, 14.35, 31.92, 17.60, "to 3.4 m (flush)", "Flush control panels and conduit (spawn 5 strip: wall-flush only)", "corebuster_env_041"),
    ("E4", "East", 29.40, 17.70, 31.92, 22.70, "1.8 to 3.4 m", "Cabinet and drive row A (rear wall of the east service aisle)", "corebuster_env_041"),
    ("E5", "East", 30.70, 22.70, 31.92, 23.90, "to 6.0 m", "Pipe drop bay: vertical drops and valve station between rows A and B", "corebuster_env_033"),
    ("E6", "East", 29.40, 23.90, 31.92, 29.40, "1.8 to 3.4 m", "Cabinet and transformer row B, taller units toward the north-east bank", "corebuster_env_041"),
    ("E7", "North-East corner", 30.95, 29.45, 31.92, 31.83, "to 6.0 m", "Corner riser: vertical drops behind the east distribution bank", "corebuster_env_033"),
    # North wall (Z max). Red Terminal stretch is reserved.
    ("N1", "North", 27.48, 29.59, 30.86, 31.37, "to 2.84 m", "East_Distribution_Bank (built)", "corebuster_env_032"),
    ("N2", "North", 24.20, 29.23, 27.38, 31.83, "1.8 to 3.4 m", "Switchgear and cabinet row", "corebuster_env_041"),
    ("N3", "North", 23.00, 30.63, 24.20, 31.83, "to 6.0 m", "Pipe drop bay between the north-east rows", "corebuster_env_033"),
    ("N4", "North", 19.10, 29.23, 23.00, 31.83, "1.8 to 3.4 m", "Drive row ending short of the Red Terminal reservation (keep the terminal visually distinct)", "corebuster_env_041"),
    ("N5", "North", 9.75, 29.23, 13.35, 31.83, "1.8 to 3.4 m", "Tall cabinet row: rear wall of the north service aisle behind loop anchor A1", "corebuster_env_041"),
    ("N6", "North", 8.55, 30.63, 9.75, 31.83, "to 6.0 m", "Pipe drop bay between the north-west rows", "corebuster_env_033"),
    ("N7", "North", 4.60, 29.23, 8.55, 31.83, "1.8 to 3.4 m", "Cabinet and drive row beside the west transformer bank", "corebuster_env_041"),
    ("N8", "North", 1.06, 29.59, 4.44, 31.37, "to 2.84 m", "West_Transformer_Bank (built)", "corebuster_env_032"),
    ("N9", "North-West corner", 0.00, 29.45, 1.00, 31.83, "to 6.0 m", "Corner riser: vertical drops behind the west transformer bank", "corebuster_env_033"),
    # West wall (X min). Master Terminal, progression lane and Boss Hall Door approach are reserved.
    ("W1", "West", 0.00, 23.80, 2.60, 29.35, "1.8 to 3.4 m", "Cabinet and drive row (north part of the west wall)", "corebuster_env_041"),
    ("W2", "West", 0.00, 22.60, 1.20, 23.80, "to 6.0 m", "Pipe drop bay between the west rows", "corebuster_env_033"),
    ("W3", "West", 0.00, 17.85, 2.60, 22.60, "1.8 to 3.4 m", "Switchgear row north of the Boss Hall Door approach; red warning accents belong to 037", "corebuster_env_041"),
]

# Wall-service-only stretches above reservations (no body-height content).
SERVICE_ONLY = [
    ("R-N", "North", "Red Terminal reservation", "Services only above Y 2.38, outside the terminal column (to Y 2.64); terminal stays visually distinct", "corebuster_env_033"),
    ("R-E", "East", "East door approach", "Services only above the door column (Y 3.49); the wall above the door has no collider", "corebuster_env_033"),
    ("R-W", "West", "Master Terminal, progression lane and Boss Hall Door approach", "Services only above Y 2.38, outside the Master (to Y 3.96) and Boss Hall Door (to Y 4.48) columns", "corebuster_env_033"),
]

# Freestanding loop anchors: low cover owned by corebuster_env_034 (they are cover pieces, not islands).
ANCHORS = [
    ("A1", 10.60, 24.20, 14.30, 26.00, "North-west loop anchor: low power-routing housing, long axis east-west"),
    ("A2", 18.40, 6.20, 21.80, 8.00, "South-east loop anchor: low transformer base, long axis east-west"),
    ("A3", 24.60, 18.90, 26.30, 22.20, "East loop anchor: low reinforced equipment case, long axis north-south"),
]
ANCHOR_MAX_HEIGHT = 1.2

# Open spaces (polygons, local u/v). O1 is the protected radius-8 m disc.
OPEN_SPACES = [
    ("O1", "Central arena", None, "Protected annulus (radius 4-8 m) plus the open inner disc; all spawn connectors meet it"),
    ("O2", "North-west pocket", [(2.6, 17.6), (2.6, 29.23), (10.6, 29.23), (10.6, 24.2), (9.3, 21.0), (8.4, 17.6)],
     "Spawn 6 disc and connector; west leg of Loop-1; boss-door connector edge"),
    ("O3", "South-east entry hall", [(21.8, 2.96), (30.25, 2.96), (31.92, 3.1), (31.92, 11.32), (29.2, 11.45), (23.6, 11.4), (21.8, 8.0)],
     "East door approach and door connector, spawn 3 disc and connector; east leg of Loop-2"),
    ("O4", "North-east pocket", [(17.6, 23.6), (17.6, 29.23), (29.4, 29.23), (29.4, 23.9), (26.3, 22.2), (24.6, 22.2), (21.6, 21.0)],
     "Spawn 8 disc and connector, ring-to-Red-Terminal connector edge; north leg of Loop-3"),
    ("O5", "South-west pocket and Master landing", [(0.0, 3.7), (2.6, 3.4), (11.6, 2.96), (14.6, 2.96), (14.6, 8.0), (10.6, 10.6), (4.0, 13.9), (0.0, 13.9)],
     "Master Terminal keep-out, spawn 1 disc and connector, ring-to-Master connector, start of the progression lane"),
]

# Planned lanes: (id, name, polyline, required clear width). Survey connectors are added as lanes too.
LANES = [
    ("L-N", "North service aisle (behind A1)", [(10.6, 27.6), (14.3, 27.6)], 3.0),
    ("L-S", "South service aisle (behind A2)", [(18.4, 4.6), (21.8, 4.6)], 3.0),
    ("L-E", "East service aisle (behind A3)", [(27.85, 18.9), (27.85, 22.2)], 3.0),
]

LOOPS = [
    ("Loop-1", "North-west loop around A1", [(12.4, 21.9), (15.2, 22.8), (15.4, 27.4), (12.4, 27.6), (9.2, 27.2), (9.4, 22.6)],
     "Arena, ring-to-Red/spawn 7 corridor, north service aisle L-N, north-west pocket O2"),
    ("Loop-2", "South-east loop around A2", [(17.6, 9.6), (17.0, 6.0), (20.1, 4.6), (23.0, 5.0), (23.4, 8.6), (21.0, 10.6)],
     "Arena, spawn 2 corridor, south service aisle L-S, south-east entry hall O3"),
    ("Loop-3", "East loop around A3", [(22.6, 17.6), (27.6, 17.9), (27.85, 20.5), (27.6, 23.3), (23.6, 23.4), (22.0, 19.5)],
     "Arena, spawn 5 corridor, east service aisle L-E, north-east pocket O4"),
]

# ---------------------------------------------------------------- geometry
def seg_dist(p, a, b):
    ax, az = a
    bx, bz = b
    dx, dz = bx - ax, bz - az
    L = dx * dx + dz * dz
    t = 0 if L == 0 else max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - az) * dz) / L))
    return math.hypot(p[0] - (ax + t * dx), p[1] - (az + t * dz))


def rect_point_dist(r, p):
    u0, v0, u1, v1 = r
    du = max(u0 - p[0], 0, p[0] - u1)
    dv = max(v0 - p[1], 0, p[1] - v1)
    return math.hypot(du, dv)


def rect_far(r, p):
    u0, v0, u1, v1 = r
    return math.hypot(max(abs(u0 - p[0]), abs(u1 - p[0])), max(abs(v0 - p[1]), abs(v1 - p[1])))


def rect_seg_dist(r, a, b):
    n = max(1, int(math.ceil(math.hypot(b[0] - a[0], b[1] - a[1]) / 0.05)))
    return min(rect_point_dist(r, (a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n)) for i in range(n + 1))


def violations(r):
    """Same movement-band rule as NorthWestRoomFramework.Layout.KeepOutViolation, in local metres."""
    out = []
    u0, v0, u1, v1 = r
    if u0 < -0.03 or v0 < -0.03 or u1 > W + 0.03 or v1 > D + 0.03:
        out.append("outside room")
    for name, k in KEEPOUTS:
        if u0 < k[2] and u1 > k[0] and v0 < k[3] and v1 > k[1]:
            out.append(name)
    for i, s in enumerate(SPAWNS):
        if rect_point_dist(r, s) < SPAWN_R:
            out.append("spawn %d disc" % (i + 1))
    if rect_point_dist(r, (CU, CV)) < R_OUT and rect_far(r, (CU, CV)) > S["ringInnerRadius"]:
        out.append("annulus")
    for name, cs in CONNECTORS:
        for i in range(1, len(cs)):
            if rect_seg_dist(r, cs[i - 1], cs[i]) < HALF:
                out.append("connector " + name)
                break
    return out


def margin(r):
    """Smallest clearance to any reservation (m); negative values are not produced because violations stop the run."""
    m = [rect_point_dist(r, s) - SPAWN_R for s in SPAWNS]
    m.append(rect_point_dist(r, (CU, CV)) - R_OUT)
    for _, cs in CONNECTORS:
        m += [rect_seg_dist(r, cs[i - 1], cs[i]) - HALF for i in range(1, len(cs))]
    for _, k in KEEPOUTS:
        du = max(k[0] - r[2], r[0] - k[2], 0)
        dv = max(k[1] - r[3], r[1] - k[3], 0)
        m.append(math.hypot(du, dv) if du or dv else 0)
    return min(m)


errors = []
for b in BAYS:
    v = violations(b[2:6])
    if v:
        errors.append("%s: %s" % (b[0], ", ".join(v)))
for a in ANCHORS:
    v = violations(a[1:5])
    if v:
        errors.append("%s: %s" % (a[0], ", ".join(v)))
if errors:
    sys.exit("Planned footprints violate reservations:\n  " + "\n  ".join(errors))

# ---------------------------------------------------------------- projected metrics
TERMINALS = [S["redTerminal"]["body"], S["masterTerminal"]["body"]]
TERMINALS = [(b["min"]["x"] - X0, b["min"]["z"] - Z0, b["max"]["x"] - X0, b["max"]["z"] - Z0) for b in TERMINALS]
SOLIDS = [b[2:6] for b in BAYS] + [a[1:5] for a in ANCHORS] + TERMINALS


def inside(r, p):
    return r[0] <= p[0] <= r[2] and r[1] <= p[1] <= r[3]


STEP = 0.25
cells = occupied = 0
u = STEP / 2
while u < W:
    v = STEP / 2
    while v < D:
        cells += 1
        if any(inside(r, (u, v)) for r in SOLIDS):
            occupied += 1
        v += STEP
    u += STEP
clear_fraction = 1 - occupied / cells

RESERVED = {"North": [(13.497, 19.003)], "East": [(7.197, 11.297)], "West": [(3.697, 17.697)], "South": []}


def wall_line():
    walls = {"South": (W, lambda t: (t, 0.0), "u"), "North": (W, lambda t: (t, D), "u"),
             "West": (D, lambda t: (0.0, t), "v"), "East": (D, lambda t: (W, t), "v")}
    total = occ = 0
    per = {}
    for name, (length, at, axis) in walls.items():
        t = STEP / 2
        wt = wo = 0
        while t < length:
            if not any(a <= t <= b for a, b in RESERVED[name]):
                wt += 1
                p = at(t)
                # A bay occupies a station when its footprint touches the wall-side 3 m column there.
                for b in BAYS:
                    r = b[2:6]
                    if axis == "u" and r[0] <= t <= r[2] and (r[1] <= 3.0 if p[1] == 0 else r[3] >= D - 3.0):
                        wo += 1
                        break
                    if axis == "v" and r[1] <= t <= r[3] and (r[0] <= 3.0 if p[0] == 0 else r[2] >= W - 3.0):
                        wo += 1
                        break
            t += STEP
        per[name] = round(wo / wt, 3) if wt else None
        total += wt
        occ += wo
    return occ / total, per, total * STEP


wall_fraction, wall_per, wall_length = wall_line()


def lane_width(poly, cap=4.0):
    best = 1e9
    for i in range(1, len(poly)):
        a, b = poly[i - 1], poly[i]
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        if L == 0:
            continue
        n = (-(b[1] - a[1]) / L, (b[0] - a[0]) / L)
        steps = max(1, int(L / 0.5))
        for k in range(steps + 1):
            p = (a[0] + (b[0] - a[0]) * k / steps, a[1] + (b[1] - a[1]) * k / steps)
            if any(inside(r, p) for r in SOLIDS):
                return 0.0
            w = 0
            for sgn in (1, -1):
                d = 0.0
                while d < cap:
                    q = (p[0] + sgn * n[0] * d, p[1] + sgn * n[1] * d)
                    if not (0 <= q[0] <= W and 0 <= q[1] <= D) or any(inside(r, q) for r in SOLIDS):
                        break
                    d += 0.02
                w += d
            best = min(best, w)
    return best


lane_rows = []
for lid, name, poly, need in LANES:
    lane_rows.append((lid, name, poly, need, lane_width(poly)))
for name, cs in CONNECTORS:
    lane_rows.append(("C:" + name, name, cs, 2 * HALF, lane_width(cs)))
narrow = [r for r in lane_rows if r[4] + 1e-6 < r[3]]
if narrow:
    sys.exit("Planned lanes narrower than required: " + ", ".join("%s %.2f" % (r[0], r[4]) for r in narrow))


def poly_area(poly):
    return abs(sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly)))) / 2


def point_in_poly(p, poly):
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        if (poly[i][1] > p[1]) != (poly[j][1] > p[1]) and p[0] < (poly[j][0] - poly[i][0]) * (p[1] - poly[i][1]) / (poly[j][1] - poly[i][1]) + poly[i][0]:
            c = not c
        j = i
    return c


def open_area(poly):
    """Clear m2 inside the polygon, excluding the arena disc and planned footprints (0.25 m grid)."""
    n = 0
    u = STEP / 2
    while u < W:
        v = STEP / 2
        while v < D:
            p = (u, v)
            if (poly is None and math.hypot(u - CU, v - CV) <= R_OUT) or (
                    poly is not None and point_in_poly(p, poly) and math.hypot(u - CU, v - CV) > R_OUT):
                if not any(inside(r, p) for r in SOLIDS):
                    n += 1
            v += STEP
        u += STEP
    return n * STEP * STEP


for lid, name, poly, _ in LOOPS:
    for a in ANCHORS:
        if not point_in_poly(((a[1] + a[3]) / 2, (a[2] + a[4]) / 2), poly):
            continue
        break
    else:
        sys.exit(lid + " does not circle any loop anchor")
    for i in range(len(poly)):
        p, q = poly[i], poly[(i + 1) % len(poly)]
        if any(rect_seg_dist(r, p, q) < 0.4 for r in SOLIDS):
            sys.exit("%s leg %d passes within 0.4 m of a planned footprint" % (lid, i))

# ---------------------------------------------------------------- budgets (proposed for owner approval)
BUDGETS = {
    "prefabInstances": {"original": 250, "proposed": 400, "perTask": {
        "corebuster_env_031": 20, "corebuster_env_032": 5, "corebuster_env_041": 130, "corebuster_env_033": 145,
        "corebuster_env_034": 20, "corebuster_env_035": 25, "corebuster_env_037": 30, "corebuster_env_042": 10,
        "corebuster_env_038": 15}},
    "realtimeLights": {"original": 16, "proposed": 24, "perRendererMax": 4},
    "carvingObstacles": {"original": 20, "proposed": 24, "perTask": {
        "corebuster_env_031": 2, "corebuster_env_032": 2, "corebuster_env_041": 8, "corebuster_env_033": 4,
        "corebuster_env_034": 6, "reserve": 2}},
    "materials": {"original": 15, "proposed": 26, "perTask": {
        "existing (031 and 032, including shared kit finishes)": 11, "corebuster_env_041": 3, "corebuster_env_033": 3,
        "corebuster_env_034": 1, "corebuster_env_042": 2, "corebuster_env_036": 4, "corebuster_env_037": 2}},
}
TARGETS = {
    "occupiedWallLineMin": 0.85,
    "clearFloorMin": 0.70, "clearFloorMax": 0.80,
    "openSpacesMin": 3, "loopRoutesMin": 2,
    "laneWidthTarget": 3.0, "laneWidthFloor": 2.0,
}

# ---------------------------------------------------------------- write plan JSON (world coordinates)
def wrect(r):
    return {"min": world(r[0], r[1]), "max": world(r[2], r[3])}


def bay_depth(b):
    """Free depth the footprint takes from its wall; corners report their larger side."""
    u0, v0, u1, v1 = b[2:6]
    by_wall = {"South": v1, "North": D - v0, "West": u1, "East": W - u0}
    return by_wall.get(b[1], max(u1 - u0, v1 - v0))


def bay_length(b):
    u0, v0, u1, v1 = b[2:6]
    return u1 - u0 if b[1] in ("South", "North") else v1 - v0 if b[1] in ("West", "East") else max(u1 - u0, v1 - v0)


for b in BUDGETS.values():
    if "perTask" in b:
        b["perTask"] = [{"task": k, "count": v} for k, v in b["perTask"].items()]

plan = {
    "task": "corebuster_env_040",
    "status": "approved by the owner on 2026-10-02",
    "surveySceneSha256": S["sceneSha256"],
    "interior": {"min": {"x": round(X0, 5), "z": round(Z0, 5)}, "max": {"x": round(X1, 5), "z": round(Z1, 5)}},
    "coordinates": "world X/Z metres; east is +X, north is +Z; footprints are movement-band (floor to floor + 2.2 m) limits",
    "targets": TARGETS,
    "budgets": BUDGETS,
    "bays": [dict(id=b[0], wall=b[1], footprint=wrect(b[2:6]),
                  depth=round(bay_depth(b), 2), length=round(bay_length(b), 2),
                  heightBand=b[6], content=b[7], owner=b[8],
                  built=b[8] in ("corebuster_env_031", "corebuster_env_032"),
                  reservationMargin=round(margin(b[2:6]), 2)) for b in BAYS],
    "serviceOnly": [dict(id=s[0], wall=s[1], reservation=s[2], rule=s[3], owner=s[4]) for s in SERVICE_ONLY],
    "anchors": [dict(id=a[0], footprint=wrect(a[1:5]), maxColliderHeight=ANCHOR_MAX_HEIGHT, content=a[5],
                     owner="corebuster_env_034", reservationMargin=round(margin(a[1:5]), 2)) for a in ANCHORS],
    "openSpaces": [dict(id=o[0], name=o[1], centre={"x": round(S["centre"]["x"], 3), "z": round(S["centre"]["z"], 3)} if o[2] is None else None,
                        radius=R_OUT if o[2] is None else None,
                        polygon=None if o[2] is None else [world(*p) for p in o[2]],
                        projectedClearArea=round(open_area(o[2]), 1), carries=o[3]) for o in OPEN_SPACES],
    "lanes": [dict(id=l[0], name=l[1], polyline=[world(*p) for p in l[2]], requiredWidth=l[3]) for l in LANES],
    "loops": [dict(id=l[0], name=l[1], polyline=[world(*p) for p in l[2]], via=l[3]) for l in LOOPS],
    "projected": {
        "clearFloorFraction": round(clear_fraction, 3),
        "clearFloorArea": round(clear_fraction * W * D, 1),
        "occupiedWallLine": round(wall_fraction, 3),
        "occupiedWallLinePerWall": [{"wall": k, "fraction": v} for k, v in wall_per.items()],
        "nonReservedWallLength": round(wall_length, 2),
        "laneWidths": [{"id": r[0], "width": round(r[4], 2)} for r in lane_rows],
    },
}
for o in plan["openSpaces"]:
    for k in [k for k, v in o.items() if v is None]:
        del o[k]
os.makedirs(os.path.dirname(PLAN_OUT), exist_ok=True)
with open(PLAN_OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump(plan, f, indent=2)
    f.write("\n")

# ---------------------------------------------------------------- SVG
SC = 22.0
PAD = 40
LEG = 330
WIDTH = PAD * 2 + W * SC + LEG
HEIGHT = PAD * 2 + D * SC + 40


def P(u, v):
    return (PAD + u * SC, PAD + (D - v) * SC)


def rect_svg(r, cls, extra=""):
    x, y = P(r[0], r[3])
    return '<rect class="%s" x="%.1f" y="%.1f" width="%.1f" height="%.1f"%s/>' % (cls, x, y, (r[2] - r[0]) * SC, (r[3] - r[1]) * SC, extra)


def poly_svg(pts, cls, close=True):
    s = " ".join("%.1f,%.1f" % P(*p) for p in pts)
    return '<%s class="%s" points="%s"/>' % ("polygon" if close else "polyline", cls, s)


OWN = {"corebuster_env_031": "own031", "corebuster_env_032": "own032", "corebuster_env_041": "own041", "corebuster_env_033": "own033"}
out = []
out.append('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" font-family="Segoe UI, Arial, sans-serif" font-size="11">' % (WIDTH, HEIGHT))
out.append("""<style>
.bg{fill:#f6f3ec}.room{fill:#ffffff;stroke:#222;stroke-width:2}
.arena{fill:#cfe8d4;stroke:#3d8b4f;stroke-width:1.5}.inner{fill:none;stroke:#3d8b4f;stroke-dasharray:4 3}
.open{fill:#e3f2e6;stroke:#3d8b4f;stroke-width:1;stroke-dasharray:6 3}
.keep{fill:#f4c7c3;fill-opacity:.55;stroke:#c0392b;stroke-width:1}
.spawn{fill:#f4c7c3;fill-opacity:.45;stroke:#c0392b;stroke-width:1}
.conn{fill:none;stroke:#e8a09a;stroke-opacity:.55;stroke-linecap:round}
.own031{fill:#6b5b4b;stroke:#2b241d}.own032{fill:#5c6f7a;stroke:#222d33}
.own041{fill:#c99a4a;stroke:#6e4f17}.own033{fill:#b5523b;stroke:#5e2416}
.anchor{fill:#e6c33c;stroke:#6b5a10;stroke-width:1.5}
.lane{fill:none;stroke:#1f6fb2;stroke-width:2.5;stroke-dasharray:1 0}
.loop{fill:none;stroke:#7b3fa0;stroke-width:1.8;stroke-dasharray:5 3}
.svc{stroke:#b5523b;stroke-width:4;stroke-dasharray:3 3}
.lbl{fill:#111;font-weight:600}.sm{fill:#333;font-size:10}.ttl{font-size:16;font-weight:700}
</style>""")
out.append('<rect class="bg" width="%d" height="%d"/>' % (WIDTH, HEIGHT))
out.append(rect_svg((0, 0, W, D), "room"))
for o in OPEN_SPACES:
    if o[2]:
        out.append(poly_svg(o[2], "open"))
cx, cy = P(CU, CV)
out.append('<circle class="arena" cx="%.1f" cy="%.1f" r="%.1f"/>' % (cx, cy, R_OUT * SC))
out.append('<circle class="inner" cx="%.1f" cy="%.1f" r="%.1f"/>' % (cx, cy, S["ringInnerRadius"] * SC))
for name, cs in CONNECTORS:
    out.append('<polyline class="conn" stroke-width="%.1f" points="%s"/>' % (2 * HALF * SC, " ".join("%.1f,%.1f" % P(*p) for p in cs)))
for name, k in KEEPOUTS:
    out.append(rect_svg(k, "keep"))
for i, s in enumerate(SPAWNS):
    x, y = P(*s)
    out.append('<circle class="spawn" cx="%.1f" cy="%.1f" r="%.1f"/><text class="sm" x="%.1f" y="%.1f" text-anchor="middle">%d</text>' % (x, y, SPAWN_R * SC, x, y + 4, i + 1))
# Service-only stretches drawn along the wall.
for (a, b), wall in [((13.497, 19.003), "North"), ((7.197, 11.297), "East"), ((3.697, 17.697), "West")]:
    if wall == "North":
        p, q = P(a, D), P(b, D)
    elif wall == "East":
        p, q = P(W, a), P(W, b)
    else:
        p, q = P(0, a), P(0, b)
    out.append('<line class="svc" x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f"/>' % (p[0], p[1], q[0], q[1]))
for b in BAYS:
    r = b[2:6]
    out.append(rect_svg(r, OWN[b[8]]))
    x, y = P((r[0] + r[2]) / 2, (r[1] + r[3]) / 2)
    out.append('<text class="lbl" x="%.1f" y="%.1f" text-anchor="middle" fill="#fff" style="fill:#fff">%s</text>' % (x, y + 4, b[0]))
for a in ANCHORS:
    r = a[1:5]
    out.append(rect_svg(r, "anchor"))
    x, y = P((r[0] + r[2]) / 2, (r[1] + r[3]) / 2)
    out.append('<text class="lbl" x="%.1f" y="%.1f" text-anchor="middle">%s</text>' % (x, y + 4, a[0]))
for l in LOOPS:
    out.append(poly_svg(l[2], "loop"))
for r in lane_rows[:len(LANES)]:
    out.append(poly_svg(r[2], "lane", close=False))
    x, y = P(*r[2][0])
    out.append('<text class="sm" x="%.1f" y="%.1f" style="fill:#1f6fb2;font-weight:600">%s %.1f m</text>' % (x, y - 5, r[0], r[4]))
for o in OPEN_SPACES:
    if o[2]:
        pu = sum(p[0] for p in o[2]) / len(o[2])
        pv = sum(p[1] for p in o[2]) / len(o[2])
    else:
        pu, pv = CU, CV + 1.0
    x, y = P(pu, pv)
    out.append('<text class="lbl" x="%.1f" y="%.1f" text-anchor="middle" style="fill:#256b36">%s</text>' % (x, y, o[0]))
# Doors and terminals.
for g in S["gates"]:
    gu, gv = loc(g["position"])
    x, y = P(min(max(gu, 0), W), gv)
    out.append('<text class="sm" x="%.1f" y="%.1f" text-anchor="%s">%s</text>' % (x + (-6 if gu > W / 2 else 6), y - 8, "end" if gu > W / 2 else "start",
                                                                                "East door" if gu > W / 2 else "Boss Hall Door"))
for t, label in [(S["redTerminal"], "Red Terminal"), (S["masterTerminal"], "Master Terminal")]:
    tu, tv = loc(t["position"])
    x, y = P(tu, tv if tu < 5 else tv - 1.6)
    out.append('<text class="sm" x="%.1f" y="%.1f" text-anchor="%s">%s</text>' % (x + (8 if tu < 5 else 0), y + (4 if tu < 5 else -6) + (0 if tu < 5 else 0), "start" if tu < 5 else "middle", label))
# Compass, scale, title.
out.append('<text class="ttl" x="%d" y="24">North-West Room density and open-space plan (corebuster_env_040)</text>' % PAD)
x0, y0 = P(0, -0.9)
out.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#222" stroke-width="2"/><text class="sm" x="%.1f" y="%.1f">5 m</text>' % (x0, y0 + 8, x0 + 5 * SC, y0 + 8, x0 + 5 * SC + 4, y0 + 12))
nx, ny = P(W - 1, -0.9)
out.append('<text class="lbl" x="%.1f" y="%.1f" text-anchor="end">N &#8593; (+Z); east is +X</text>' % (nx, ny + 12))
# Legend.
lx = PAD + W * SC + 24
ly = PAD + 6
items = [("own031", "Built: turbines (031)"), ("own032", "Built: electrical banks (032)"), ("own041", "Machine rows (041)"),
         ("own033", "Tanks, manifolds, drops (033)"), ("anchor", "Loop anchors, low cover (034)"), ("open", "Secondary open space"),
         ("arena", "Central arena, radius 8 m"), ("keep", "Gameplay keep-out / spawn disc")]
for cls, text in items:
    out.append('<rect class="%s" x="%d" y="%d" width="18" height="12"/><text x="%d" y="%d">%s</text>' % (cls, lx, ly, lx + 26, ly + 10, text))
    ly += 20
out.append('<line class="conn" stroke-width="10" x1="%d" y1="%d" x2="%d" y2="%d"/><text x="%d" y="%d">Survey connector (3 m)</text>' % (lx, ly + 6, lx + 18, ly + 6, lx + 26, ly + 10))
ly += 20
out.append('<line class="svc" x1="%d" y1="%d" x2="%d" y2="%d"/><text x="%d" y="%d">Wall services only (reserved)</text>' % (lx, ly + 6, lx + 18, ly + 6, lx + 26, ly + 10))
ly += 20
out.append('<line class="lane" x1="%d" y1="%d" x2="%d" y2="%d"/><text x="%d" y="%d">Planned lane (min 3.0 m)</text>' % (lx, ly + 6, lx + 18, ly + 6, lx + 26, ly + 10))
ly += 20
out.append('<line class="loop" x1="%d" y1="%d" x2="%d" y2="%d"/><text x="%d" y="%d">Loop route</text>' % (lx, ly + 6, lx + 18, ly + 6, lx + 26, ly + 10))
ly += 34
pr = plan["projected"]
for line in ["Projected at full build-out:",
             "Occupied wall line %.0f%% (target %.0f%%+)" % (pr["occupiedWallLine"] * 100, TARGETS["occupiedWallLineMin"] * 100),
             "Clear floor %.0f%% = %.0f m2 (target %.0f-%.0f%%)" % (pr["clearFloorFraction"] * 100, pr["clearFloorArea"], TARGETS["clearFloorMin"] * 100, TARGETS["clearFloorMax"] * 100),
             "Planned lanes: " + ", ".join("%s %.1f m" % (r[0], r[4]) for r in lane_rows[:len(LANES)]),
             "Narrowest connector %.1f m" % min(r[4] for r in lane_rows[len(LANES):]),
             "Open spaces: arena + %d pockets; loops: %d" % (len(OPEN_SPACES) - 1, len(LOOPS)),
             "",
             "Footprints are movement-band limits.",
             "Wall services (Y 2.38-6.18) and the",
             "overhead (Y 6.18-10.78) run over every",
             "bay outside the reserved columns.",
             "Survey: " + S["sceneSha256"][:8] + "..."]:
    out.append('<text class="sm" x="%d" y="%d">%s</text>' % (lx, ly, line))
    ly += 15
out.append("</svg>")
with open(SVG_OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(out) + "\n")

print("bays %d, anchors %d, open spaces %d, lanes %d (+%d connectors), loops %d" % (len(BAYS), len(ANCHORS), len(OPEN_SPACES), len(LANES), len(CONNECTORS), len(LOOPS)))
print("projected clear floor %.3f (%.1f m2 of %.1f), occupied wall line %.3f of %.2f m" % (clear_fraction, clear_fraction * W * D, W * D, wall_fraction, wall_length), wall_per)
for r in lane_rows:
    print("  lane %-55s need %.1f  width %.2f" % (r[0], r[3], r[4]))
for o in plan["openSpaces"]:
    print("  open %s %-38s %.1f m2" % (o["id"], o["name"], o["projectedClearArea"]))
for b in plan["bays"] + plan["anchors"]:
    print("  %s margin %.2f" % (b["id"], b["reservationMargin"]))
