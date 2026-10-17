"""Draw the corebuster_art_003 Start Workshop signage: wall slogans, the P-3 tank ID plate, the hazard chevron strip
and the robot-arm blueprint poster.

Run with: python Tools/Art/generate_start_workshop_signage.py
Requires Pillow. All lettering is the hand-built stencil set in stencil_font.py; every shape and all wear are drawn
here from seeded random numbers, so the output is reproducible and uses no font file, stock decal or photo.
The "TO CORRIDORS" board is drawn by generate_exit_portal.py with the same lettering and wear.
"""

import math
import random
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFilter

import stencil_font as sf

OUT = Path(__file__).resolve().parents[2] / "Assets/FPS/Art/Textures/StartWorkshop"
CREAM = (224, 220, 199)
YELLOW = (226, 166, 48)
DARK = (28, 29, 26)
BARE_STEEL = (104, 106, 100)
RUST = (112, 62, 30)
SEED = 3070030


# ---------------------------------------------------------------------------------------------------------------------
# Wear. Everything works on Pillow images only, like the other generators.

def smooth_noise(size, cell, seed):
    """Low-frequency value noise in [0, 255]: random cells, bicubic-upscaled."""
    rng = random.Random(seed)
    w, h = size
    cw, ch = max(2, w // cell + 2), max(2, h // cell + 2)
    small = Image.new("L", (cw, ch))
    small.putdata([rng.randrange(256) for _ in range(cw * ch)])
    return small.resize((w + 2 * cell, h + 2 * cell), Image.BICUBIC).crop((cell, cell, cell + w, cell + h))


def fbm(size, seed, cells=(160, 48, 12)):
    """Three octaves of smooth noise, weighted towards the broad one."""
    out = smooth_noise(size, cells[0], seed)
    out = Image.blend(out, smooth_noise(size, cells[1], seed + 1), .35)
    return Image.blend(out, smooth_noise(size, cells[2], seed + 2), .2)


def constant(size, value):
    return Image.new("L", size, value)


def chip_field(size, rng, count, radius, wrap=False):
    """Scattered paint chips: small irregular blobs, each a cluster of overlapping ellipses."""
    w, h = size
    field = Image.new("L", size, 0)
    d = ImageDraw.Draw(field)
    for _ in range(count):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        for _ in range(rng.randint(1, 4)):
            r = rng.uniform(.3, 1) * radius
            ox, oy = rng.uniform(-radius, radius), rng.uniform(-radius, radius)
            for dx in ((-w, 0, w) if wrap else (0,)):
                d.ellipse((x + ox + dx - r, y + oy - r * rng.uniform(.5, 1), x + ox + dx + r, y + oy + r), fill=255)
    return field


def scratches(size, rng, count, length, wrap=False, fill=255):
    field = Image.new("L", size, 0)
    d = ImageDraw.Draw(field)
    w, h = size
    for _ in range(count):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        a, n = rng.uniform(-math.pi, math.pi), rng.uniform(.3, 1) * length
        for dx in ((-w, 0, w) if wrap else (0,)):
            d.line((x + dx, y, x + dx + n * math.cos(a), y + n * math.sin(a)), fill=fill, width=rng.choice((1, 1, 2)))
    return field


def worn_paint(mask, seed, chips=1.0, scratch_count=None, bleed=.22, wrap=False):
    """Stencil paint alpha: soft overspray, uneven coverage, chips biased to the letter edges, and scratches."""
    rng = random.Random(seed)
    size = mask.size
    unit = max(1, min(size) // 400)
    halo = mask.filter(ImageFilter.GaussianBlur(2.5 * unit)).point(lambda v: int(v * bleed))
    alpha = ImageChops.lighter(mask.filter(ImageFilter.GaussianBlur(.6 * unit)), halo)
    alpha = ImageChops.multiply(alpha, fbm(size, seed).point(lambda v: 175 + v * 80 // 255))
    k = 2 * (3 * unit) + 1
    edges = ImageChops.subtract(mask, mask.filter(ImageFilter.MinFilter(k)))
    edge_zone = ImageChops.lighter(edges.filter(ImageFilter.GaussianBlur(2 * unit)).point(lambda v: min(255, v * 4)),
                                   constant(size, 28))
    # Wear comes in patches: some stretches of a letter are nearly clean, others badly flaked.
    patches = smooth_noise(size, max(8, min(size) // 5), seed + 3).point(lambda v: max(0, v - 90) * 255 // 165)
    field = chip_field(size, rng, int(size[0] * size[1] / 1600 * chips), 3 * unit, wrap)
    weight = ImageChops.multiply(edge_zone, patches.point(lambda v: 60 + v * 195 // 255))
    lost = ImageChops.multiply(field.filter(ImageFilter.GaussianBlur(.7 * unit)), weight).point(
        lambda v: 255 if v > 70 else 0)
    alpha = ImageChops.subtract(alpha, lost)
    count = scratch_count if scratch_count is not None else size[0] * size[1] // 9000
    alpha = ImageChops.subtract(alpha, scratches(size, rng, count, 26 * unit, wrap, 200))
    return alpha


def weather_panel(image, seed, border=.06, rust_from=(), streaks=True, chips=1.0):
    """Opaque sign wear: grime at the edges, dirt blotches, chipped edges showing bare steel, scratches, rust runs."""
    rng = random.Random(seed)
    w, h = image.size
    size = image.size
    unit = max(1, min(size) // 400)
    rgb = image.convert("RGB")

    band = max(6, int(min(w, h) * border))
    vignette = Image.new("L", size, 0)
    vd = ImageDraw.Draw(vignette)
    for i in range(band):
        vd.rectangle((i, i, w - 1 - i, h - 1 - i), outline=int(150 * (1 - i / band) ** 2))
    vignette = vignette.filter(ImageFilter.GaussianBlur(band / 4))
    dirt = fbm(size, seed + 5).point(lambda v: max(0, v - 120) * 90 // 135)
    grime = ImageChops.lighter(vignette, dirt)
    if streaks:
        runs = Image.new("L", size, 0)
        rd = ImageDraw.Draw(runs)
        for _ in range(w // (14 * unit)):
            x = rng.uniform(0, w)
            length = rng.uniform(.15, .6) * h
            for y in range(int(length)):
                rd.point((x + math.sin(y / 23) * unit, y), fill=int(60 * (1 - y / length)))
        grime = ImageChops.lighter(grime, runs.filter(ImageFilter.GaussianBlur(1.5 * unit)))
    rgb.paste(Image.new("RGB", size, (14, 13, 11)), (0, 0), grime)

    # Chipped paint along the border and a few in the field; each chip gets a darker rim.
    edge_band = Image.new("L", size, 0)
    ImageDraw.Draw(edge_band).rectangle((0, 0, w - 1, h - 1), outline=255, width=band)
    field = chip_field(size, rng, int(w * h / 5000 * chips), 4 * unit)
    patches = smooth_noise(size, max(8, min(size) // 4), seed + 6).point(lambda v: max(0, v - 100) * 255 // 155)
    weight = ImageChops.multiply(ImageChops.lighter(edge_band.filter(ImageFilter.GaussianBlur(band / 3)),
                                                    constant(size, 10)), patches)
    lost = ImageChops.multiply(field.filter(ImageFilter.GaussianBlur(.8 * unit)), weight).point(
        lambda v: 255 if v > 60 else 0)
    rim = lost.filter(ImageFilter.MaxFilter(2 * unit + 1))
    rgb.paste(Image.new("RGB", size, (20, 19, 17)), (0, 0), rim.point(lambda v: v * 150 // 255))
    rgb.paste(Image.new("RGB", size, BARE_STEEL), (0, 0), lost)

    rgb.paste(Image.new("RGB", size, (190, 190, 182)), (0, 0),
              scratches(size, rng, w * h // 7000, 30 * unit).point(lambda v: v * 70 // 255))

    if rust_from:
        rust = Image.new("L", size, 0)
        rd = ImageDraw.Draw(rust)
        for x, y in rust_from:
            for _ in range(3):
                sx = x + rng.uniform(-3, 3) * unit
                length = rng.uniform(25, 90) * unit
                for i in range(int(length)):
                    rd.ellipse((sx - 1.6 * unit, y + i - unit, sx + 1.6 * unit, y + i + unit),
                               fill=int(150 * (1 - i / length)))
        rgb.paste(Image.new("RGB", size, RUST), (0, 0), rust.filter(ImageFilter.GaussianBlur(1.2 * unit)))
    return rgb


def rivet(d, x, y, r, head=(96, 98, 92), shadow=(18, 18, 16)):
    d.ellipse((x - r + 1, y - r + 2, x + r + 1, y + r + 2), fill=shadow)
    d.ellipse((x - r, y - r, x + r, y + r), fill=head)
    d.ellipse((x - r * .45, y - r * .55, x + r * .1, y - r * .05), fill=tuple(min(255, c + 45) for c in head))


def gear(mask, cx, cy, radius, teeth=8, fill=255):
    """A stencil-cut gear: toothed ring, open centre, and two bridges holding the centre island."""
    d = ImageDraw.Draw(mask)
    body, tip, hole = radius * .76, radius, radius * .36
    points = []
    for i in range(teeth):
        a = 2 * math.pi * i / teeth
        half = math.pi / teeth
        for angle, r in ((a - half * .95, body), (a - half * .45, tip), (a + half * .45, tip), (a + half * .95, body)):
            points.append((cx + r * math.cos(angle), cy + r * math.sin(angle)))
    d.polygon(points, fill=fill)
    d.ellipse((cx - hole, cy - hole, cx + hole, cy + hole), fill=0)
    bridge = max(3, radius * .09)
    for a in (-math.pi / 2, math.pi / 2):
        x0, y0 = cx + hole * .6 * math.cos(a), cy + hole * .6 * math.sin(a)
        x1, y1 = cx + body * .98 * math.cos(a), cy + body * .98 * math.sin(a)
        d.rectangle((min(x0, x1) - bridge / 2, min(y0, y1), max(x0, x1) + bridge / 2, max(y0, y1)), fill=0)


def paint_layer(mask, seed, colour, **wear):
    """RGBA wall paint: the colour everywhere (so filtering never fringes dark), worn stencil alpha."""
    size = mask.size
    shade = fbm(size, seed + 9).point(lambda v: 225 + v * 30 // 255)
    rgb = Image.merge("RGB", [ImageChops.multiply(constant(size, c), shade) for c in colour])
    rgb.putalpha(worn_paint(mask, seed, **wear))
    return rgb


# ---------------------------------------------------------------------------------------------------------------------
# Wall slogans: stencilled straight onto the wall, so the textures are transparent outside the paint.

def stacked_slogan(lines, size, seed, filename, gear_teeth=8):
    """Left-aligned stacked lines at one cap height with a gear below, filling the canvas like the reference."""
    w, h = size
    margin = int(w * .07)
    weight = 18
    cap = min(sf.fit_cap(line, w - 2 * margin, weight) for line in lines)
    leading, gear_d = cap * .36, cap * 1.35
    block = len(lines) * cap + (len(lines) - 1) * leading + leading * 1.4 + gear_d
    cap *= min(1, (h - 2 * margin) / block)
    leading, gear_d = cap * .36, cap * 1.35
    block = len(lines) * cap + (len(lines) - 1) * leading + leading * 1.4 + gear_d
    mask = Image.new("L", size, 0)
    y = (h - block) / 2
    for line in lines:
        sf.place(mask, line, (margin, y), cap, weight)
        y += cap + leading
    y += leading * .4
    gear(mask, margin + gear_d / 2, y + gear_d / 2, gear_d / 2, gear_teeth)
    paint_layer(mask, seed, CREAM).save(OUT / filename)


def small_robots_panel():
    """The reference's dark painted panel beside the exit, with cream stencil lettering and a gear."""
    w, h = 1024, 1536
    rng = random.Random(SEED + 40)
    panel = Image.new("RGB", (w, h), (46, 49, 48))
    tone = fbm((w, h), SEED + 41).point(lambda v: v * 40 // 255)
    panel.paste(Image.new("RGB", (w, h), (70, 74, 72)), (0, 0), tone)
    d = ImageDraw.Draw(panel)
    d.rectangle((0, 0, w - 1, h - 1), outline=(24, 25, 24), width=10)
    d.rectangle((26, 26, w - 27, h - 27), outline=(86, 90, 86), width=4)
    d.rectangle((40, 40, w - 41, h - 41), outline=(30, 32, 31), width=2)

    lines = ["SMALL", "ROBOTS", "BIG", "FUTURE"]
    margin = 120
    cap = min(sf.fit_cap(line, w - 2 * margin) for line in lines)
    leading = cap * .34
    gear_d = cap * 1.5
    block = 4 * cap + 3 * leading + leading * 1.6 + gear_d
    mask = Image.new("L", (w, h), 0)
    y = (h - block) / 2
    for line in lines:
        sf.place(mask, line, (margin, y), cap)
        y += cap + leading
    y += leading * .6
    gear(mask, w / 2, y + gear_d / 2, gear_d / 2, 9)
    layer = paint_layer(mask, SEED + 42, CREAM, bleed=.12)
    panel.paste(layer, (0, 0), layer)

    bolts = [(x, y) for x in (70, w - 70) for y in (70, h // 3, 2 * h // 3, h - 70)]
    panel = weather_panel(panel, SEED + 43, rust_from=[(x, y + 10) for x, y in bolts])
    d = ImageDraw.Draw(panel)
    for x, y in bolts:
        rivet(d, x, y, 13)
    # A little sticker-free realism: one replacement bolt that never got painted over.
    rivet(d, rng.choice((70, w - 70)), h // 2, 12, head=(150, 150, 142))
    panel.save(OUT / "Slogan_Small_Robots_Big_Future.png")


# ---------------------------------------------------------------------------------------------------------------------
# P-3 tank identification plate, wrapped around the tank's lower shell between its bands.

def tank_id_plate():
    w, h = 800, 512
    plate = Image.new("RGB", (w, h), (38, 40, 39))
    d = ImageDraw.Draw(plate)
    d.rectangle((0, 0, w - 1, h - 1), outline=(20, 21, 20), width=8)
    d.rectangle((18, 18, w - 19, h - 19), outline=YELLOW, width=6)

    # Diagonal hazard band along the foot of the plate.
    stripe_top, stripe_bottom = h - 104, h - 40
    band = Image.new("RGB", (w, stripe_bottom - stripe_top), DARK)
    bd = ImageDraw.Draw(band)
    for x in range(-100, w + 100, 56):
        bd.polygon(((x, 0), (x + 28, 0), (x + 28 - 64, 64), (x - 64, 64)), fill=YELLOW)
    plate.paste(band.crop((40, 0, w - 40, band.height)), (40, stripe_top))

    mask = Image.new("L", (w, h), 0)
    sf.place(mask, "P-3", (64, 108), 200, 20, anchor="lt")
    sf.place(mask, "PRESSURE", (w - 60, 100), 48, 11, anchor="rt")
    sf.place(mask, "VESSEL", (w - 60, 162), 48, 11, anchor="rt")
    sf.place(mask, "MAX 12 BAR", (w - 60, 256), 38, 11, anchor="rt")
    sf.place(mask, "SERIAL 0307", (w - 60, 312), 28, 9, anchor="rt")
    layer = paint_layer(mask, SEED + 50, CREAM, bleed=.1, chips=.7)
    plate.paste(layer, (0, 0), layer)
    ImageDraw.Draw(plate).line((w - 300, 234, w - 60, 234), fill=YELLOW, width=4)

    bolts = [(46, 46), (w - 46, 46), (46, h - 22), (w - 46, h - 22)]
    plate = weather_panel(plate, SEED + 51, border=.08, rust_from=bolts, streaks=False)
    d = ImageDraw.Draw(plate)
    for x, y in bolts[:2]:
        rivet(d, x, y, 10)
    plate.save(OUT / "Tank_ID_P3.png")


# ---------------------------------------------------------------------------------------------------------------------
# Hazard chevron strip: tileable horizontally, chevrons pointing along +U.

def hazard_chevron():
    w, h, period = 512, 128, 128
    rng = random.Random(SEED + 60)
    strip = Image.new("RGB", (w, h), DARK)
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)
    top, bottom, depth, thick = 14, h - 14, 44, 46
    mid = (top + bottom) / 2
    for i in range(-1, w // period + 1):
        x = i * period + 24
        d.polygon(((x, top), (x + thick, top), (x + thick + depth, mid), (x + thick, bottom), (x, bottom),
                   (x + depth, mid)), fill=255)
    paint = worn_paint(mask, SEED + 61, chips=.8, bleed=.05, wrap=True)
    strip.paste(Image.new("RGB", (w, h), YELLOW), (0, 0), paint)
    sd = ImageDraw.Draw(strip)
    sd.rectangle((0, 0, w - 1, 7), fill=(18, 18, 16))
    sd.rectangle((0, h - 8, w - 1, h - 1), fill=(18, 18, 16))
    # Grime settles on the lower lip; scuffs wrap around the tile so the repeat stays seamless.
    grime = Image.new("L", (w, h), 0)
    gd = ImageDraw.Draw(grime)
    for y in range(h):
        gd.line((0, y, w, y), fill=int(max(0, y - h * .55) / (h * .45) * 110))
    strip.paste(Image.new("RGB", (w, h), (16, 14, 11)), (0, 0), grime)
    strip.paste(Image.new("RGB", (w, h), (150, 150, 140)), (0, 0),
                scratches((w, h), rng, 40, 20, wrap=True).point(lambda v: v * 90 // 255))
    strip.save(OUT / "Hazard_Chevron.png")


# ---------------------------------------------------------------------------------------------------------------------
# Robot-arm blueprint poster: an original drawing of the corebuster_art_002 arm.

BLUE = (24, 62, 102)
LINE = (208, 226, 236)
FAINT = (58, 100, 138)


def dimension(d, a, b, offset, label, normal, label_gap=22):
    """A dimension line offset from a-b, with extension lines, arrowheads and a centred stencil label."""
    (x0, y0), (x1, y1) = a, b
    nx, ny = normal
    p0 = (x0 + nx * offset, y0 + ny * offset)
    p1 = (x1 + nx * offset, y1 + ny * offset)
    d.line((x0 + nx * 8, y0 + ny * 8, p0[0] + nx * 10, p0[1] + ny * 10), fill=LINE, width=2)
    d.line((x1 + nx * 8, y1 + ny * 8, p1[0] + nx * 10, p1[1] + ny * 10), fill=LINE, width=2)
    d.line((*p0, *p1), fill=LINE, width=2)
    length = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
    ux, uy = (p1[0] - p0[0]) / length, (p1[1] - p0[1]) / length
    for (px, py), s in ((p0, 1), (p1, -1)):
        d.polygon(((px, py), (px + s * (ux * 16 - uy * 6), py + s * (uy * 16 + ux * 6)),
                   (px + s * (ux * 16 + uy * 6), py + s * (uy * 16 - ux * 6))), fill=LINE)
    cx, cy = (p0[0] + p1[0]) / 2 + nx * label_gap, (p0[1] + p1[1]) / 2 + ny * label_gap
    return cx, cy, label


def blueprint_poster():
    w, h = 1536, 1024
    rng = random.Random(SEED + 70)
    paper = Image.new("RGB", (w, h), BLUE)
    tone = fbm((w, h), SEED + 71).point(lambda v: v * 46 // 255)
    paper.paste(Image.new("RGB", (w, h), (40, 88, 132)), (0, 0), tone)
    d = ImageDraw.Draw(paper)
    for x in range(48, w - 40, 32):
        d.line((x, 48, x, h - 48), fill=FAINT if (x - 48) % 160 else (70, 116, 156), width=1)
    for y in range(48, h - 40, 32):
        d.line((48, y, w - 48, y), fill=FAINT if (y - 48) % 160 else (70, 116, 156), width=1)
    d.rectangle((30, 30, w - 31, h - 31), outline=LINE, width=5)
    d.rectangle((44, 44, w - 45, h - 45), outline=LINE, width=2)

    text = Image.new("L", (w, h), 0)
    sf.place(text, "BETTER", (86, 88), 118, 20)
    sf.place(text, "MACHINES", (86, 236), 118, 20)
    sf.place(text, "BRIGHTER", (86, 422), 88, 16)
    sf.place(text, "TOMORROW", (86, 534), 88, 16)
    ImageDraw.Draw(text).rectangle((86, 382, 560, 392), fill=255)

    # Side elevation of the six-axis arm (proportions from corebuster_art_002), drawn in line work.
    base = (1060, 690)
    shoulder = (1060, 520)
    elbow = (1236, 300)
    wrist = (1000, 196)
    tool = (888, 268)
    ink = LINE
    d.rectangle((base[0] - 150, base[1], base[0] + 150, base[1] + 34), outline=ink, width=4)
    for bx in (base[0] - 120, base[0] + 120):
        d.ellipse((bx - 8, base[1] + 9, bx + 8, base[1] + 25), outline=ink, width=2)
    d.polygon(((base[0] - 96, base[1]), (base[0] + 96, base[1]), (base[0] + 70, shoulder[1] + 50),
               (base[0] - 70, shoulder[1] + 50)), outline=ink, width=4)

    def link(a, b, half):
        ax, ay = a
        bx, by = b
        length = math.hypot(bx - ax, by - ay)
        nx, ny = -(by - ay) / length * half, (bx - ax) / length * half
        d.polygon(((ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)), outline=ink, width=4)
        d.line((ax, ay, bx, by), fill=FAINT, width=2)

    link(shoulder, elbow, 44)
    link(elbow, wrist, 32)
    link(wrist, tool, 20)
    for p, r in ((shoulder, 66), (elbow, 50), (wrist, 36)):
        d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=BLUE, outline=ink, width=4)
        d.ellipse((p[0] - r * .35, p[1] - r * .35, p[0] + r * .35, p[1] + r * .35), outline=ink, width=3)
        d.line((p[0] - r - 14, p[1], p[0] + r + 14, p[1]), fill=FAINT, width=1)
        d.line((p[0], p[1] - r - 14, p[0], p[1] + r + 14), fill=FAINT, width=1)
    # Two-finger gripper.
    tx, ty = tool
    d.rectangle((tx - 34, ty - 12, tx + 10, ty + 12), outline=ink, width=3)
    d.line((tx - 34, ty - 12, tx - 70, ty - 34), fill=ink, width=5)
    d.line((tx - 34, ty + 12, tx - 70, ty + 34), fill=ink, width=5)
    # Hydraulic hose along the lower arm.
    d.line([(shoulder[0] + 30 + i * 3, shoulder[1] - 40 - i * 4.2 + 8 * math.sin(i / 6)) for i in range(0, 52, 2)],
           fill=ink, width=2)

    labels = []
    labels.append(dimension(d, (base[0] + 150, base[1] + 34), (base[0] + 150, elbow[1]), 120, "2400", (1, 0), 46))
    labels.append(dimension(d, (tool[0] - 70, base[1] + 34), (base[0] + 150, base[1] + 34), 50, "1850", (0, 1)))
    for cx, cy, label in labels:
        sf.place(text, label, (cx, cy), 26, 9, anchor="mm")

    for name, (px, py), (lx, ly) in (("J1", (base[0] - 40, base[1] - 60), (860, 650)), ("J2", shoulder, (1250, 590)),
                                     ("J3", elbow, (1380, 190)), ("J4", wrist, (1090, 100)),
                                     ("J5", ((wrist[0] + tool[0]) / 2, (wrist[1] + tool[1]) / 2), (820, 130)),
                                     ("J6", (tool[0] - 50, tool[1]), (770, 380))):
        d.line((px, py, lx, ly), fill=LINE, width=2)
        d.ellipse((lx - 28, ly - 28, lx + 28, ly + 28), fill=BLUE, outline=LINE, width=3)
        sf.place(text, name, (lx, ly), 24, 9, anchor="mm")

    # Title block.
    tb = (w - 520, h - 210, w - 60, h - 60)
    d.rectangle(tb, outline=LINE, width=3)
    d.line((tb[0], tb[1] + 64, tb[2], tb[1] + 64), fill=LINE, width=2)
    d.line((tb[0], tb[1] + 110, tb[2], tb[1] + 110), fill=LINE, width=2)
    d.line((tb[0] + 230, tb[1] + 64, tb[0] + 230, tb[3]), fill=LINE, width=2)
    sf.place(text, "CB-RA6 SERVICE ARM", (tb[0] + 20, tb[1] + 20), 30, 11)
    sf.place(text, "SCALE 1:20", (tb[0] + 20, tb[1] + 78), 20, 8)
    sf.place(text, "SHEET 02/03", (tb[0] + 250, tb[1] + 78), 20, 8)
    sf.place(text, "WORKSHOP BAY 03", (tb[0] + 20, tb[1] + 120), 20, 8)
    sf.place(text, "SIX AXIS / REV C", (tb[0] + 250, tb[1] + 120), 20, 8)
    sf.place(text, "BUILD IT RIGHT", (86, h - 116), 34, 11)

    ink_layer = Image.new("RGB", (w, h), (236, 236, 222))
    alpha = worn_paint(text, SEED + 72, chips=.35, bleed=.05, scratch_count=0)
    paper.paste(ink_layer, (0, 0), alpha)

    # Paper wear: fold creases, a water stain, faded edges, tape at the corners.
    crease = Image.new("L", (w, h), 0)
    cd = ImageDraw.Draw(crease)
    cd.line((w // 2 + 6, 0, w // 2 - 4, h), fill=90, width=3)
    cd.line((0, h // 2 - 5, w, h // 2 + 3), fill=70, width=3)
    paper.paste(Image.new("RGB", (w, h), (120, 160, 190)), (0, 0), crease.filter(ImageFilter.GaussianBlur(2)))
    stain = Image.new("L", (w, h), 0)
    ImageDraw.Draw(stain).ellipse((180, 690, 470, 930), outline=90, width=10)
    paper.paste(Image.new("RGB", (w, h), (18, 44, 70)), (0, 0), stain.filter(ImageFilter.GaussianBlur(9)))
    paper = weather_panel(paper, SEED + 73, border=.05, streaks=False, chips=.25)
    td = ImageDraw.Draw(paper, "RGBA")
    for cx, cy, a in ((22, 22, 40), (w - 22, 22, -40), (22, h - 22, -40), (w - 22, h - 22, 40)):
        r = math.radians(a)
        pts = [(cx + ex * math.cos(r) - ey * math.sin(r), cy + ex * math.sin(r) + ey * math.cos(r))
               for ex, ey in ((-70, -18), (70, -18), (70, 18), (-70, 18))]
        td.polygon(pts, fill=(214, 200, 150, 170))
    for _ in range(3):
        x, y = rng.randrange(120, w - 120), rng.randrange(120, h - 120)
        td.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(14, 30, 50, 200))
    paper.save(OUT / "Blueprint_Robot_Arm.png")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    stacked_slogan(["TOOLS", "PEOPLE", "PROGRESS"], (1024, 1024), SEED + 10, "Slogan_Tools_People_Progress.png")
    stacked_slogan(["MAINTAIN", "BUILD", "IMPROVE", "SURVIVE"], (1024, 1280), SEED + 20,
                   "Slogan_Maintain_Build_Improve_Survive.png", gear_teeth=10)
    small_robots_panel()
    tank_id_plate()
    hazard_chevron()
    blueprint_poster()
    print(f"Wrote 6 original Start Workshop signage textures to {OUT}")


if __name__ == "__main__":
    main()
