"""Draw the measured corebuster_env_029 North-West Room layout; no Unity scene or asset writes.

Usage: python Tools/Art/render_north_west_room_survey.py [Measurement.json] [output.svg]
"""
import html
import json
from pathlib import Path
import sys


def render(source, output):
    report = json.loads(Path(source).read_text(encoding="utf-8-sig"))
    low, high = report["interior"]["min"], report["interior"]["max"]
    scale, margin, side, legend = 19, 85, 180, 170
    width = (high["x"] - low["x"]) * scale + side * 2
    height = (high["z"] - low["z"]) * scale + margin + 40 + legend
    elements = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width:.0f}" height="{height:.0f}" viewBox="0 0 {width} {height}">',
                '<rect width="100%" height="100%" fill="#111923"/>',
                '<g font-family="sans-serif" font-size="13" fill="#e1e8f0">']

    def xy(point):
        return side + (point["x"] - low["x"]) * scale, margin + (high["z"] - point["z"]) * scale

    def text(x, y, value, size=13, anchor="start"):
        elements.append(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" text-anchor="{anchor}">{html.escape(value)}</text>')

    def box(value, color, opacity=.18, dash=False):
        x, y = xy(dict(x=value["min"]["x"], z=value["max"]["z"]))
        w = (value["max"]["x"] - value["min"]["x"]) * scale
        h = (value["max"]["z"] - value["min"]["z"]) * scale
        style = ' stroke-dasharray="4 3"' if dash else ""
        elements.append(f'<rect x="{x:.2f}" y="{y:.2f}" width="{w:.2f}" height="{h:.2f}" stroke="{color}" fill="{color}" fill-opacity="{opacity}"{style}/>')

    def circle(point, radius, color, opacity=0):
        x, y = xy(point)
        elements.append(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="{radius * scale:.2f}" stroke="{color}" fill="{color}" fill-opacity="{opacity}"/>')

    def line(points, color, width=1.2):
        coords = " ".join(f"{x:.2f},{y:.2f}" for x, y in map(xy, points))
        elements.append(f'<polyline points="{coords}" fill="none" stroke="{color}" stroke-width="{width}"/>')

    text(side, 31, "NORTH-WEST ROOM / measured dressing reservations", 21)
    text(side, 55, "corebuster_env_029 | world X/Z metres | north (+Z) up | open centre, perimeter machinery")
    box(report["interior"], "#8595aa", .05)
    # Safe body-height dressing: heavy perimeter (3 m) and corners first, then wall-flush-only stretches.
    for zone in report["zones"]:
        if zone["kind"] in ("heavy-perimeter", "corner"):
            box(zone, "#d9a14a", .22)
        elif zone["kind"] == "flush-only":
            box(zone, "#d9a14a", .08, dash=True)
    for p in report["navMisses"]:
        circle(p, .07, "#ef8080", 1)
    circle(report["centre"], report["ringOuterRadius"], "#63cba4", .10)
    circle(report["centre"], report["ringInnerRadius"], "#63cba4")
    x, y = xy(report["centre"])
    text(x, y - 5, "Open centre", anchor="middle")
    text(x, y + 12, "annulus 4-8 m", anchor="middle")
    for route in report["routes"]:
        if route["connector"] and len(route["corners"]) > 1:
            line(route["corners"], "#63cba4")
    for gate in report["gates"]:
        box(gate["approach"], "#67c9e6", .25)
        x, y = xy(gate["position"])
        label = "East door" if "(E)" in gate["name"] else "Boss Hall Door"
        text(x + (8 if gate["outward"]["x"] > 0 else -8), y + 4, label, anchor="start" if gate["outward"]["x"] > 0 else "end")
    for key, label in (("redTerminal", "Red Terminal"), ("masterTerminal", "Master Terminal")):
        terminal = report[key]
        box(terminal["keepOut"], "#ebce66", .22)
        x, y = xy(terminal["position"])
        if key == "redTerminal":
            text(x, y - 8, label, anchor="middle")
        else:
            text(x - 8, y + 4, label, anchor="end")
    for i, spawn in enumerate(report["spawns"], 1):
        circle(spawn["position"], report["spawnRadius"], "#e8a780", .15)
        x, y = xy(spawn["position"])
        text(x, y + 5, str(i), anchor="middle")
    x, y = xy(report["accessMachine"]["position"])
    text(x + 8, y + 4, "Access (Yellow)")
    circle(report["accessMachine"]["position"], .15, "#ebce66", 1)
    base = height - legend + 12
    ground = abs(report["physicalGroundY"]) if abs(report["physicalGroundY"]) < .005 else report["physicalGroundY"]
    for i, value in enumerate([
            f'Interior X [{low["x"]:.2f}, {high["x"]:.2f}], Z [{low["z"]:.2f}, {high["z"]:.2f}]',
            f'Visible floor Y {report["floorTopY"]:.2f} · ceiling underside Y {report["ceilingUndersideY"]:.2f} · physical ground Y {ground:.2f}',
            "Cyan: door approaches · Yellow: terminal access, reach and sightline · Orange discs: 2 m spawn reservations",
            "Green: 4-8 m annulus and connectors (reserve 1.5 m each side of each connector)",
            "Amber: safe floor zones (solid: 3 m heavy perimeter and corners; dashed: wall-flush only)",
            "Red dots: NavMesh sample misses. Edit-mode evidence; the owner's Play-mode walk is still required.",
            f'Scene SHA-256 {report["sceneSha256"][:16]}... · Unity {report["unityVersion"]}']):
        text(side, base + i * 21, value)
    elements.extend(["</g>", "</svg>"])
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    Path(output).write_text("\n".join(elements), encoding="utf-8")


if __name__ == "__main__":
    render(sys.argv[1] if len(sys.argv) > 1 else "ReviewCaptures/NorthWestRoom/029/Measurement.json",
           sys.argv[2] if len(sys.argv) > 2 else "ReviewCaptures/NorthWestRoom/029/Layout.svg")
