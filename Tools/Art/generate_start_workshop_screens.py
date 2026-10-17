"""Draw the corebuster_env_016 Start Workshop screen content: the CORE STATUS graph, a diagnostic readout, a schematic,
a small panel gauge screen and the segmented LED strip.

Run with: python Tools/Art/generate_start_workshop_screens.py
Requires Pillow. Lettering is the hand-built stencil set in stencil_font.py. Every shape is drawn here from seeded random
numbers, so the output is reproducible and uses no font file, stock image or photo. The textures are used as both the base
map (dimmed) and the emission map of project-owned URP/Lit materials, so they read as lit displays.
"""

import math
import random
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFilter

import stencil_font as sf

OUT = Path(__file__).resolve().parents[2] / "Assets/FPS/Art/Textures/StartWorkshop"
GLASS = (6, 12, 13)
TEAL = (70, 236, 205)
TEAL_DIM = (24, 96, 88)
AMBER = (247, 176, 52)
RED = (240, 76, 58)
SEED = 3160016


def canvas(size):
    """Dark glass with a faint vertical falloff, like a switched-on CRT-style panel."""
    w, h = size
    img = Image.new("RGB", size, GLASS)
    d = ImageDraw.Draw(img)
    for y in range(h):
        k = 1 + .35 * math.sin(math.pi * y / h)
        d.line((0, y, w, y), fill=tuple(int(c * k) for c in GLASS))
    return img


def text(img, s, xy, cap, color, weight=11, anchor="lt", max_width=None):
    """Stencil text drawn in a colour onto an RGB image."""
    mask = Image.new("L", img.size, 0)
    box = sf.place(mask, s, xy, cap, weight, anchor=anchor, max_width=max_width)
    img.paste(Image.new("RGB", img.size, color), (0, 0), mask)
    return box


def finish(img, seed, glow=1.0):
    """Bloom-ready glow, scanlines, a faint edge vignette and pixel noise; returns an RGB image."""
    rng = random.Random(seed)
    w, h = img.size
    glowed = ImageChops.add(img, img.filter(ImageFilter.GaussianBlur(max(2, w // 220))).point(lambda v: int(v * .55 * glow)))
    d = ImageDraw.Draw(glowed)
    unit = max(1, h // 256)
    dark = Image.new("L", img.size, 0)
    dd = ImageDraw.Draw(dark)
    for y in range(0, h, 2 * unit):
        dd.rectangle((0, y, w, y + unit - 1), fill=46)
    glowed.paste(Image.new("RGB", img.size, (0, 0, 0)), (0, 0), dark)
    vignette = Image.new("L", img.size, 0)
    vd = ImageDraw.Draw(vignette)
    band = max(6, min(w, h) // 10)
    for i in range(band):
        vd.rectangle((i, i, w - 1 - i, h - 1 - i), outline=int(150 * (1 - i / band) ** 2))
    vignette = vignette.filter(ImageFilter.GaussianBlur(band / 3))
    glowed.paste(Image.new("RGB", img.size, (0, 0, 0)), (0, 0), vignette)
    px = glowed.load()
    for _ in range(w * h // 70):
        x, y = rng.randrange(w), rng.randrange(h)
        r, g, b = px[x, y]
        n = rng.randint(-10, 10)
        px[x, y] = (max(0, min(255, r + n)), max(0, min(255, g + n)), max(0, min(255, b + n)))
    return glowed


def frame(img, color=TEAL_DIM, inset=10):
    w, h = img.size
    d = ImageDraw.Draw(img)
    d.rectangle((inset, inset, w - 1 - inset, h - 1 - inset), outline=color, width=max(2, w // 400))
    c = inset * 3
    for (x, y, sx, sy) in ((inset, inset, 1, 1), (w - 1 - inset, inset, -1, 1), (inset, h - 1 - inset, 1, -1),
                           (w - 1 - inset, h - 1 - inset, -1, -1)):
        d.line((x, y, x + sx * c, y), fill=TEAL, width=max(3, w // 300))
        d.line((x, y, x, y + sy * c), fill=TEAL, width=max(3, w // 300))


def grid(img, box, cols, rows, color=TEAL_DIM):
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = box
    for i in range(cols + 1):
        x = x0 + (x1 - x0) * i // cols
        d.line((x, y0, x, y1), fill=color)
    for j in range(rows + 1):
        y = y0 + (y1 - y0) * j // rows
        d.line((x0, y, x1, y), fill=color)


def bar(img, box, fraction, color, back=TEAL_DIM, segments=20):
    """A segmented horizontal bar, lit up to fraction."""
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = box
    gap = max(1, (x1 - x0) // (segments * 5))
    step = (x1 - x0) / segments
    for i in range(segments):
        sx0, sx1 = x0 + i * step + gap, x0 + (i + 1) * step - gap
        d.rectangle((sx0, y0, sx1, y1), fill=color if (i + .5) / segments <= fraction else back)


def core_status(size=(1024, 576)):
    """CORE STATUS: a live-looking pressure trace with a warning band, four core bars and a stable tag."""
    rng = random.Random(SEED)
    w, h = size
    img = canvas(size)
    frame(img)
    text(img, "CORE STATUS", (w * .06, h * .075), h * .095, TEAL, 16)
    text(img, "UNIT P-3 / LINE A", (w * .94, h * .085), h * .045, TEAL_DIM, 9, anchor="rt")

    plot = (int(w * .06), int(h * .25), int(w * .62), int(h * .82))
    grid(img, plot, 14, 8)
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = plot
    # The amber warning band is where the trace must not stay.
    d.rectangle((x0 + 1, y0 + 1, x1 - 1, y0 + (y1 - y0) // 5), outline=None, fill=(58, 38, 8))
    text(img, "LIMIT", (x1 - 8, y0 + 6), h * .035, AMBER, 9, anchor="rt")
    pts = []
    value, drift = .42, 0.0
    n = 96
    for i in range(n):
        drift = drift * .86 + rng.uniform(-.045, .045)
        value = min(.7, max(.12, value + drift + .006 * math.sin(i / 7.0)))
        if i in (60, 61, 62):
            value = min(.74, value + .1)
        pts.append((x0 + (x1 - x0) * i / (n - 1), y1 - (y1 - y0) * value))
    d.line(pts, fill=TEAL, width=max(3, w // 300), joint="curve")
    d.polygon(pts + [(x1, y1), (x0, y1)], fill=(9, 52, 47))
    d.line(pts, fill=TEAL, width=max(3, w // 300), joint="curve")
    px, py = pts[-1]
    d.ellipse((px - 7, py - 7, px + 7, py + 7), fill=AMBER)
    text(img, "PRESSURE", (x0, y1 + h * .03), h * .04, TEAL, 10)
    text(img, "-30S", (x0 + 4, y0 - h * .05), h * .03, TEAL_DIM, 9)
    text(img, "NOW", (x1, y0 - h * .05), h * .03, TEAL_DIM, 9, anchor="rt")

    bx0, bx1 = int(w * .67), int(w * .94)
    for i, level in enumerate((.92, .78, .55, .31)):
        y = h * (.27 + .13 * i)
        text(img, f"CORE 0{i + 1}", (bx0, y), h * .04, TEAL, 10)
        text(img, f"{int(level * 100)}", (bx1, y), h * .04, AMBER if level < .4 else TEAL, 10, anchor="rt")
        bar(img, (bx0, int(y + h * .055), bx1, int(y + h * .08)), level, AMBER if level < .4 else TEAL, segments=16)
    d.rectangle((bx0, int(h * .81), bx1, int(h * .9)), outline=TEAL, width=max(2, w // 450))
    text(img, "STABLE", ((bx0 + bx1) / 2, h * .855), h * .05, TEAL, 12, anchor="mm")
    return finish(img, SEED + 1)


def diagnostics(size=(768, 512)):
    """A page of labelled readouts with segmented level bars."""
    rng = random.Random(SEED + 2)
    w, h = size
    img = canvas(size)
    frame(img)
    text(img, "SYS DIAGNOSTIC", (w * .06, h * .07), h * .085, TEAL, 14)
    text(img, "OK", (w * .94, h * .075), h * .07, TEAL, 14, anchor="rt")
    rows = (("POWER BUS", .9), ("COOLANT", .74), ("PRESSURE", .62), ("ACTUATORS", .95), ("SEAL P-3", .47), ("VENT FAN", .81))
    for i, (label, level) in enumerate(rows):
        y = h * (.22 + .115 * i)
        text(img, label, (w * .06, y), h * .05, TEAL, 10)
        low = level < .5
        bar(img, (int(w * .40), int(y), int(w * .80), int(y + h * .05)), level, AMBER if low else TEAL, segments=18)
        text(img, f"{int(level * 100):02d}", (w * .94, y), h * .05, AMBER if low else TEAL, 10, anchor="rt")
    text(img, f"CYCLE {rng.randint(1000, 9999)}", (w * .06, h * .93), h * .04, TEAL_DIM, 9, anchor="lb")
    return finish(img, SEED + 3)


def schematic(size=(768, 512)):
    """A line schematic: a six-joint arm on a plinth feeding a pipe circuit with valves and a tank."""
    w, h = size
    img = canvas(size)
    frame(img)
    text(img, "ARM CELL / P-3 LINE", (w * .06, h * .07), h * .07, TEAL, 12)
    grid(img, (int(w * .06), int(h * .2), int(w * .94), int(h * .88)), 16, 9, (14, 46, 43))
    d = ImageDraw.Draw(img)
    lw = max(3, w // 200)
    base = (w * .24, h * .78)
    d.rectangle((base[0] - 60, base[1], base[0] + 60, base[1] + 22), outline=TEAL, width=lw)
    joints = [base, (w * .24, h * .66), (w * .35, h * .46), (w * .49, h * .40), (w * .56, h * .53), (w * .62, h * .60)]
    d.line(joints, fill=TEAL, width=lw + 2, joint="curve")
    for jx, jy in joints:
        d.ellipse((jx - 11, jy - 11, jx + 11, jy + 11), outline=AMBER, width=lw)
    tx, ty = joints[-1]
    d.line((tx, ty, tx + 24, ty + 12), fill=AMBER, width=lw + 1)
    d.line((tx, ty, tx + 24, ty - 12), fill=AMBER, width=lw + 1)
    # The pipe circuit: a header to a tank, two valves and a branch back.
    pipe = [(w * .68, h * .30), (w * .90, h * .30), (w * .90, h * .78), (w * .70, h * .78)]
    d.line(pipe, fill=TEAL_DIM, width=lw + 4, joint="curve")
    d.line(pipe, fill=TEAL, width=lw - 1, joint="curve")
    d.ellipse((w * .70, h * .45, w * .86, h * .65), outline=TEAL, width=lw)
    text(img, "P-3", (w * .78, h * .55), h * .06, TEAL, 11, anchor="mm")
    for vx, vy, tag in ((w * .79, h * .30, "V1"), (w * .90, h * .60, "V2")):
        d.polygon([(vx - 11, vy - 11), (vx + 11, vy + 11), (vx + 11, vy - 11), (vx - 11, vy + 11)], outline=AMBER)
        text(img, tag, (vx, vy - 24), h * .04, AMBER, 9, anchor="mb")
    text(img, "AXIS 1-6 NOMINAL", (w * .06, h * .95), h * .04, TEAL_DIM, 9, anchor="lb")
    return finish(img, SEED + 4)


def panel_small(size=(384, 256)):
    """Three vertical gauges, for the fusebox panels."""
    rng = random.Random(SEED + 5)
    w, h = size
    img = canvas(size)
    frame(img, inset=6)
    text(img, "LINE P-3", (w * .08, h * .09), h * .1, TEAL, 12)
    d = ImageDraw.Draw(img)
    for i, name in enumerate(("A", "B", "C")):
        level = rng.choice((.62, .78, .88, .35))
        x0 = w * (.12 + .28 * i)
        colour = AMBER if level < .4 else TEAL
        segments = 10
        for s in range(segments):
            y1 = h * .82 - s * h * .055
            d.rectangle((x0, y1 - h * .04, x0 + w * .16, y1), fill=colour if (s + .5) / segments <= level else TEAL_DIM)
        text(img, name, (x0 + w * .08, h * .93), h * .07, TEAL, 10, anchor="mb")
    return finish(img, SEED + 6)


def led_strip(size=(512, 32)):
    """White segments of uneven brightness on black: the material's emission colour tints them; tiles in U."""
    rng = random.Random(SEED + 7)
    w, h = size
    img = Image.new("RGB", size, (0, 0, 0))
    d = ImageDraw.Draw(img)
    segments = 16
    step = w / segments
    for i in range(segments):
        level = rng.choice((255, 255, 255, 200, 130))
        d.rounded_rectangle((i * step + 3, 5, (i + 1) * step - 4, h - 6), radius=3, fill=(level, level, level))
    return img.filter(ImageFilter.GaussianBlur(.8))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    outputs = {
        "Screen_Core_Status.png": core_status(),
        "Screen_Diagnostics.png": diagnostics(),
        "Screen_Schematic.png": schematic(),
        "Screen_Panel_Small.png": panel_small(),
        "Led_Strip.png": led_strip(),
    }
    for name, image in outputs.items():
        image.save(OUT / name, optimize=True)
        print(f"{name}: {image.size[0]}x{image.size[1]}")


if __name__ == "__main__":
    main()
