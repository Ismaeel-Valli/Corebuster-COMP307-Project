"""Draw the measured env_022 layout; no Unity scene or asset writes.

Usage: python Tools/Art/render_centre_room_survey.py [Measurement.json] [output.svg]
"""
import html
import json
from pathlib import Path
import sys


def render(source, output):
    report = json.loads(Path(source).read_text(encoding="utf-8-sig"))
    low, high = report["interior"]["min"], report["interior"]["max"]
    scale, margin = 19, 85
    width = (high["x"] - low["x"]) * scale + margin * 2
    height = (high["z"] - low["z"]) * scale + margin * 2 + 95
    elements = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width:.0f}" height="{height:.0f}" viewBox="0 0 {width} {height}">',
                '<rect width="100%" height="100%" fill="#111923"/>',
                '<g font-family="sans-serif" font-size="13" fill="#e1e8f0">']

    def xy(point):
        return margin + (point["x"] - low["x"]) * scale, margin + (high["z"] - point["z"]) * scale

    def text(x, y, value, size=13):
        elements.append(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}">{html.escape(value)}</text>')

    def box(value, color, opacity=.18):
        x, y = xy(dict(x=value["min"]["x"], z=value["max"]["z"]))
        w = (value["max"]["x"] - value["min"]["x"]) * scale
        h = (value["max"]["z"] - value["min"]["z"]) * scale
        elements.append(f'<rect x="{x:.2f}" y="{y:.2f}" width="{w:.2f}" height="{h:.2f}" stroke="{color}" fill="{color}" fill-opacity="{opacity}"/>')

    def circle(point, radius, color, opacity=0):
        x, y = xy(point)
        elements.append(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="{radius * scale:.2f}" stroke="{color}" fill="{color}" fill-opacity="{opacity}"/>')

    text(margin, 31, "CENTRE ROOM / measured dressing reservations", 21)
    text(margin, 55, "corebuster_env_022 | world X/Z metres | north (+Z) up")
    box(report["interior"], "#8595aa", .05)
    for p in report["navMisses"]:
        circle(p, .07, "#ef8080", 1)
    circle(report["ringCenter"], report["ringOuterRadius"], "#63cba4", .10)
    circle(report["ringCenter"], report["ringInnerRadius"], "#63cba4")
    circle(report["ringCenter"], 3, "#909caf")
    x, y = xy(report["ringCenter"])
    text(x - 42, y - 5, "Reactor study")
    text(x - 34, y + 15, "max. Ø6 m")
    for route in report["routes"]:
        if "across saved gate" in route["name"] or "Workshop exit" in route["name"]:
            continue  # External route may extend far beyond this room's plan.
        points = " ".join(f"{x:.2f},{y:.2f}" for x, y in map(xy, route["corners"]))
        elements.append(f'<polyline points="{points}" fill="none" stroke="#63cba4" stroke-width="1.2"/>')
    for gate in report["gates"]:
        box(gate["approach"], "#67c9e6")
        x, y = xy(gate["position"])
        text(x - 30, y - 8, gate["name"].replace("Centre Room Door ", ""))
    box(report["terminalKeepOut"], "#ebce66")
    x, y = xy(report["terminal"]["position"])
    text(x - 65, y - 12, "Yellow terminal")
    for i, point in enumerate(report["spawns"], 1):
        circle(point["position"], report["spawnRadius"], "#ebce66", .12)
        x, y = xy(point["position"])
        text(x - 7, y + 5, str(i))
    for point in report["occupants"]:
        circle(point["position"], report["spawnRadius"], "#e8a780", .12)
        x, y = xy(point["position"])
        text(x - 23, y + 5, "HoverBot")
    text(margin, height - 94, f'Interior X [{low["x"]:.2f}, {high["x"]:.2f}], Z [{low["z"]:.2f}, {high["z"]:.2f}]')
    text(margin, height - 72, "Cyan: gate approaches · Yellow: spawns / terminal · Green: 4–8 m circulation ring")
    text(margin, height - 50, "Lines: sampled NavMesh paths (reserve 1.5 m each side outside the ring).")
    text(margin, height - 28, "Red dots: NavMesh sample misses. Edit-mode evidence; Play-mode review still required.")
    elements.extend(["</g>", "</svg>"])
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    Path(output).write_text("\n".join(elements), encoding="utf-8")


if __name__ == "__main__":
    render(sys.argv[1] if len(sys.argv) > 1 else "ReviewCaptures/CentreRoom/022/Measurement.json",
           sys.argv[2] if len(sys.argv) > 2 else "ReviewCaptures/CentreRoom/022/Layout.svg")
