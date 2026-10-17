"""Draw the corebuster_env_047 Boss Hall survey as a top-down SVG plan (north, +Z, up).

Usage: python Tools/Art/render_boss_hall_survey.py Docs/BossHall/Measurement_047.json Docs/BossHall/Layout_047.svg
Local authoring tool (Tools/Art/*.py is git-ignored); the SVG it writes is the kept evidence.
"""
import json
import sys

SCALE = 9.0  # pixels per metre
PAD = 60


def main(src, dst):
    r = json.load(open(src, encoding="utf-8"))
    lo, hi = r["interior"]["min"], r["interior"]["max"]
    x0, x1 = lo["x"] - 14, hi["x"] + 14  # room for the incline on the east
    z0, z1 = lo["z"] - 4, hi["z"] + 4
    w = (x1 - x0) * SCALE + PAD * 2
    h = (z1 - z0) * SCALE + PAD * 2 + 150

    def px(x):
        return PAD + (x - x0) * SCALE

    def pz(z):
        return PAD + (z1 - z) * SCALE

    out = []

    def rect(a, b, fill, stroke="none", op=1.0, sw=1.0, dash=None):
        xa, xb = sorted((px(a["x"]), px(b["x"])))
        za, zb = sorted((pz(a["z"]), pz(b["z"])))
        d = f' stroke-dasharray="{dash}"' if dash else ""
        out.append(f'<rect x="{xa:.1f}" y="{za:.1f}" width="{xb - xa:.1f}" height="{zb - za:.1f}" fill="{fill}" fill-opacity="{op}" stroke="{stroke}" stroke-width="{sw}"{d}/>')

    def text(x, z, s, size=11, fill="#222", anchor="middle", weight="normal"):
        out.append(f'<text x="{px(x):.1f}" y="{pz(z):.1f}" font-size="{size}" fill="{fill}" text-anchor="{anchor}" font-weight="{weight}" font-family="Segoe UI, Arial, sans-serif">{s}</text>')

    out.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{w:.0f}" height="{h:.0f}" viewBox="0 0 {w:.0f} {h:.0f}">')
    out.append(f'<rect width="100%" height="100%" fill="#fbfaf7"/>')
    rect(lo, hi, "#ecebe6", "#333", 1, 2)

    zone_fill = {"deep-pocket": ("#f2c27a", .35), "heavy-perimeter": ("#e39a3b", .35), "wall-flush": ("#c9783a", .25),
                 "corner": ("#e39a3b", .2), "flush-only": ("#d9c27a", .45), "reserved": ("#c94a3b", .45)}
    for zn in r["zones"]:
        if zn["kind"] in zone_fill and zn["level"] == "Lower floor":
            f, op = zone_fill[zn["kind"]]
            rect(zn["min"], zn["max"], f, op=op)

    for d in r["deckFootprints"]:
        upper = d["name"].startswith("Upper")
        rect(d["min"], d["max"], "#5b7fa6" if upper else "#9bb3c9", "#3d5a78", .55 if upper else .45, .5)

    for s in r["stairs"]:
        b = s["steps"]
        rect(b["min"], b["max"], "#d4b106", "#7a6600", .8, 1)
        cx = (b["min"]["x"] + b["max"]["x"]) / 2
        cz = (b["min"]["z"] + b["max"]["z"]) / 2
        text(cx, cz + .6, s["label"], 9, "#3d3300")
        for p, c in ((s["foot"]["position"], "#1b7f3a"), (s["top"]["position"], "#7a1b7f")):
            out.append(f'<circle cx="{px(p["x"]):.1f}" cy="{pz(p["z"]):.1f}" r="3" fill="{c}"/>')

    for b in r["bayDoors"]:
        rect(b["bounds"]["min"], b["bounds"]["max"], "#333")
        rect(b["approach"]["min"], b["approach"]["max"], "none", "#333", 1, 1, "4 3")

    t = r["turret"]
    rect(t["sightCorridor"]["min"], t["sightCorridor"]["max"], "#d6336c", "#d6336c", .12, 1, "6 4")
    c = t["pivot"]
    out.append(f'<circle cx="{px(c["x"]):.1f}" cy="{pz(c["z"]):.1f}" r="{t["aimRadius"] * SCALE:.1f}" fill="#d6336c" fill-opacity=".3" stroke="#a61e4d" stroke-width="1.5"/>')
    rect(t["footprint"]["min"], t["footprint"]["max"], "#a61e4d", op=.8)
    f = t["firingOrigin"]
    fx, fz = f["x"] + t["restForward"]["x"] * 6, f["z"] + t["restForward"]["z"] * 6
    out.append(f'<line x1="{px(f["x"]):.1f}" y1="{pz(f["z"]):.1f}" x2="{px(fx):.1f}" y2="{pz(fz):.1f}" stroke="#a61e4d" stroke-width="2"/>')
    text(c["x"], c["z"] - 3.6, "Turret (rest yaw %.0f)" % t["restYaw"], 10, "#a61e4d", weight="bold")

    for a in r["actors"]:
        if a["kind"] == "turret":
            continue
        p = a["position"]
        out.append(f'<circle cx="{px(p["x"]):.1f}" cy="{pz(p["z"]):.1f}" r="{a["envelopeRadius"] * SCALE:.1f}" fill="#c92a2a" fill-opacity=".18" stroke="#c92a2a" stroke-width="1"/>')
        out.append(f'<circle cx="{px(p["x"]):.1f}" cy="{pz(p["z"]):.1f}" r="2.5" fill="#c92a2a"/>')
        label = a["name"].replace("Enemy_", "").replace("CyberSoldier_", "CS ").replace("Robot1_", "R1 ").replace("Elite_", "Elite ")
        text(p["x"], p["z"] + a["envelopeRadius"] + .5, label, 8, "#8a1c1c")

    for rt in r["routes"]:
        if rt["connector"] and len(rt["corners"]) > 1 and all(abs(q["y"] - r["floorTopY"]) < 1 for q in rt["corners"]):
            pts = " ".join(f'{px(q["x"]):.1f},{pz(q["z"]):.1f}' for q in rt["corners"])
            out.append(f'<polyline points="{pts}" fill="none" stroke="#2b8a3e" stroke-width="1.5" stroke-dasharray="5 3"/>')

    prof = [s for s in r["incline"] if s["groundY"] > -900]
    pts = " ".join(f'{px(s["position"]["x"]):.1f},{pz(s["position"]["z"]):.1f}' for s in prof)
    out.append(f'<polyline points="{pts}" fill="none" stroke="#495057" stroke-width="3"/>')
    rev = next((s for s in prof if s["turretVisible"]), None)
    if rev:
        out.append(f'<circle cx="{px(rev["position"]["x"]):.1f}" cy="{pz(rev["position"]["z"]):.1f}" r="4" fill="#d6336c"/>')
        text(rev["position"]["x"], rev["position"]["z"] - 2.4, "turret first visible", 9, "#a61e4d")
    e = r["entry"]
    out.append(f'<line x1="{px(hi["x"]):.1f}" y1="{pz(e["clearMin"]):.1f}" x2="{px(hi["x"]):.1f}" y2="{pz(e["clearMax"]):.1f}" stroke="#fbfaf7" stroke-width="4"/>')
    text(hi["x"] + 5, e["centre"]["z"] + 6.5, "Entry %.2f m, lintel Y %.2f" % (e["width"], e["lintelY"]), 10, "#222")
    text(hi["x"] + 6, e["centre"]["z"] - 4.6, "Corridor Incline Full", 9, "#495057")

    text((lo["x"] + hi["x"]) / 2, hi["z"] + 2, "North wall Z %.3f" % hi["z"], 11)
    text((lo["x"] + hi["x"]) / 2, lo["z"] - 3, "South wall Z %.3f" % lo["z"], 11)
    text(lo["x"] - 1, (lo["z"] + hi["z"]) / 2, "West X %.3f" % lo["x"], 11, anchor="end")
    text(hi["x"] - 1, lo["z"] + 1, "East X %.3f" % hi["x"], 11, anchor="end")

    ly = (z1 - z0) * SCALE + PAD * 2 + 10
    items = [("#9bb3c9", "First-tier gallery deck (Y %.2f)" % r["tier1TopY"]), ("#5b7fa6", "Second-tier deck (Y %.2f)" % r["tier2TopY"]),
             ("#d4b106", "Stair steps (green dot foot, purple dot top)"), ("#c92a2a", "Actor start envelope (2 m)"),
             ("#d6336c", "Turret footprint, head sweep and entry sight corridor"), ("#f2c27a", "Lower-floor deep pocket (5 m)"),
             ("#e39a3b", "Heavy perimeter (3 m) / corner"), ("#d9c27a", "Flush-only"), ("#c94a3b", "Reserved: no body-height dressing"),
             ("#2b8a3e", "Floor connector (NavMesh path, 1.5 m half-width)"), ("#495057", "Incline profile")]
    for i, (col, label) in enumerate(items):
        cx = PAD + (i % 3) * ((w - PAD * 2) / 3)
        cy = ly + (i // 3) * 22
        out.append(f'<rect x="{cx:.1f}" y="{cy:.1f}" width="14" height="14" fill="{col}" fill-opacity=".7"/>')
        out.append(f'<text x="{cx + 20:.1f}" y="{cy + 11:.1f}" font-size="11" font-family="Segoe UI, Arial, sans-serif" fill="#222">{label}</text>')
    out.append(f'<text x="{PAD}" y="{ly + 100:.1f}" font-size="11" font-family="Segoe UI, Arial, sans-serif" fill="#555">corebuster_env_047 Boss Hall survey, scene {r["sceneSha256"][:12]}…, Unity {r["unityVersion"]}, {r["surveyedUtc"]}. North (+Z) up; 1 m = {SCALE:.0f} px. Zones are reservations, not scene objects.</text>')
    out.append("</svg>")
    open(dst, "w", encoding="utf-8").write("\n".join(out))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
