"""Draw the original Start Workshop labels, way-finding, hazard-trim and pegboard textures.

Run with: python Tools/Art/generate_start_workshop_graphics.py
Requires Pillow. All shapes and lettering are drawn here; no stock decal art is used.
"""

import math
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont


OUT = Path(__file__).resolve().parents[2] / "Assets/FPS/Art/Textures/StartWorkshop"
CREAM = (224, 220, 199, 255)
YELLOW = (226, 166, 48, 255)
DARK = (28, 29, 26, 255)


def font(size):
    return ImageFont.load_default(size=size)


def label(draw, xy, value, size, fill, anchor=None):
    draw.text(xy, value, font=font(size), fill=fill, anchor=anchor, stroke_width=0)


def blueprint():
    image = Image.new("RGBA", (512, 320), (17, 43, 57, 255))
    d = ImageDraw.Draw(image)
    d.rectangle((12, 12, 499, 307), outline=(87, 139, 151, 255), width=3)
    d.rectangle((23, 23, 488, 296), outline=(59, 103, 118, 255), width=1)
    for x in range(46, 490, 46):
        d.line((x, 68, x, 286), fill=(38, 77, 91, 255), width=1)
    for y in range(75, 290, 36):
        d.line((28, y, 482, y), fill=(38, 77, 91, 255), width=1)
    label(d, (34, 22), "WORKSHOP / SERVICE PLAN", 25, CREAM)
    d.line((32, 62, 480, 62), fill=(116, 173, 178, 255), width=3)
    # A simple original section diagram: two reservoirs, pipework, and a valve.
    d.rounded_rectangle((58, 127, 144, 239), 14, outline=CREAM, width=4)
    d.rounded_rectangle((325, 127, 411, 239), 14, outline=CREAM, width=4)
    d.line((144, 171, 325, 171), fill=CREAM, width=5)
    d.line((144, 211, 325, 211), fill=CREAM, width=5)
    d.ellipse((211, 157, 251, 197), outline=YELLOW, width=4)
    d.line((231, 139, 231, 214), fill=YELLOW, width=4)
    d.line((211, 139, 251, 139), fill=YELLOW, width=4)
    label(d, (60, 252), "INLET", 19, CREAM)
    label(d, (322, 252), "CORE VENT", 19, CREAM)
    label(d, (459, 266), "03", 21, YELLOW, anchor="mm")
    image.save(OUT / "Workshop_Service_Plan.png")


def warning():
    image = Image.new("RGBA", (512, 256), (42, 37, 28, 255))
    d = ImageDraw.Draw(image)
    d.rectangle((10, 10, 501, 245), outline=YELLOW, width=10)
    d.rectangle((25, 25, 486, 230), outline=(103, 79, 41, 255), width=3)
    d.polygon(((103, 51), (170, 188), (36, 188)), outline=YELLOW, width=8)
    d.ellipse((92, 104, 114, 126), outline=YELLOW, width=5)
    d.line((103, 126, 103, 164), fill=YELLOW, width=7)
    label(d, (204, 73), "CORE GAS", 39, CREAM)
    d.line((203, 124, 461, 124), fill=YELLOW, width=4)
    label(d, (204, 145), "VENT AREA / 03", 24, YELLOW)
    image.save(OUT / "Core_Gas_Warning.png")


def floor_arrow():
    image = Image.new("RGBA", (512, 256), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    for x in (30, 482):
        d.line((x, 22, x, 234), fill=YELLOW, width=8)
    d.line((30, 22, 482, 22), fill=YELLOW, width=5)
    d.line((30, 234, 482, 234), fill=YELLOW, width=5)
    for offset in (-78, 0, 78):
        d.line(((256 + offset) - 28, 143, 256 + offset, 106, (256 + offset) + 28, 143),
               fill=YELLOW, width=13, joint="curve")
    label(d, (256, 179), "TO EXIT", 30, YELLOW, anchor="mm")
    image.save(OUT / "Route_To_Exit.png")


def hazard_trim():
    # Tileable 45-degree yellow/black hazard stripe, used for the corebuster_env_008 base trim and door jambs.
    # 64px diagonal period into a 256px-wide canvas (256 / 64 = 4) makes the horizontal wrap seamless.
    w, h, period, stripe = 256, 128, 64, 32
    image = Image.new("RGBA", (w, h), DARK)
    d = ImageDraw.Draw(image)
    for x in range(-h, w + h, period):
        d.polygon(((x, 0), (x + stripe, 0), (x + stripe - h, h), (x - h, h)), fill=YELLOW)
    d.rectangle((0, 0, w - 1, 7), fill=DARK)
    d.rectangle((0, h - 8, w - 1, h - 1), fill=DARK)
    image.save(OUT / "Hazard_Trim.png")


STEEL = (176, 180, 178, 255)
STEEL_LIGHT = (214, 218, 214, 255)
STEEL_DARK = (112, 116, 116, 255)
GRIP_RED = (152, 40, 32, 255)
GRIP_BLACK = (34, 34, 36, 255)
SHADOW_PAINT = (168, 140, 62, 255)


def wrench(d, cx, top, length, paint):
    ring, jaw, shaft = length * .12, length * .14, max(6, length * .1)
    d.rounded_rectangle((cx - shaft / 2, top + ring, cx + shaft / 2, top + length - jaw), shaft / 2, fill=paint("steel"))
    d.line((cx - shaft / 2 + 2, top + ring * 1.6, cx - shaft / 2 + 2, top + length - jaw * 1.4), fill=paint("light"), width=2)
    d.ellipse((cx - ring, top, cx + ring, top + 2 * ring), fill=paint("steel"))
    d.ellipse((cx - ring * .5, top + ring * .5, cx + ring * .5, top + ring * 1.5), fill=paint("hole"))
    d.ellipse((cx - jaw, top + length - 2 * jaw, cx + jaw, top + length), fill=paint("steel"))
    d.polygon(((cx - jaw * .38, top + length - jaw * 1.1), (cx + jaw * .38, top + length - jaw * 1.1),
               (cx + jaw * .62, top + length + 1), (cx - jaw * .22, top + length + 1)), fill=paint("hole"))


def hammer(d, cx, top, length, paint):
    head_w, head_h, handle = length * .46, length * .13, length * .085
    d.rounded_rectangle((cx - handle / 2, top + head_h * .6, cx + handle / 2, top + length), handle / 2, fill=paint("yellow"))
    d.rounded_rectangle((cx - handle * .62, top + length * .62, cx + handle * .62, top + length), handle * .6, fill=paint("black"))
    d.rectangle((cx - head_w * .38, top, cx + head_w * .5, top + head_h), fill=paint("dark"))
    d.polygon(((cx - head_w * .38, top), (cx - head_w * .62, top - head_h * .25),
               (cx - head_w * .5, top + head_h * .45), (cx - head_w * .38, top + head_h)), fill=paint("dark"))
    d.line((cx - head_w * .3, top + 2, cx + head_w * .45, top + 2), fill=paint("light"), width=2)


def screwdriver(d, cx, top, length, paint, grip):
    handle_w, handle_h, shaft = length * .17, length * .38, max(3, length * .035)
    d.rectangle((cx - shaft / 2, top + handle_h, cx + shaft / 2, top + length - 6), fill=paint("steel"))
    d.polygon(((cx - shaft / 2, top + length - 6), (cx + shaft / 2, top + length - 6), (cx, top + length)), fill=paint("steel"))
    d.rounded_rectangle((cx - handle_w / 2, top, cx + handle_w / 2, top + handle_h), handle_w / 2.2, fill=paint(grip))
    d.line((cx - handle_w / 4, top + 5, cx - handle_w / 4, top + handle_h - 5), fill=paint("black"), width=2)


def pliers(d, cx, top, length, paint):
    pivot = top + length * .34
    for side in (-1, 1):
        d.polygon(((cx, top), (cx + side * length * .07, top + length * .05), (cx + side * length * .06, pivot)), fill=paint("steel"))
        d.polygon(((cx + side * length * .02, pivot), (cx + side * length * .08, pivot),
                   (cx + side * length * .16, top + length), (cx + side * length * .08, top + length)), fill=paint("red"))
    d.ellipse((cx - 6, pivot - 6, cx + 6, pivot + 6), fill=paint("dark"))


def c_clamp(d, cx, top, length, paint):
    r = length * .34
    d.arc((cx - r, top, cx + r, top + 2 * r), 90, 270, fill=paint("red"), width=int(length * .11))
    d.line((cx, top + length * .05, cx + r * .9, top + length * .05), fill=paint("red"), width=int(length * .1))
    d.line((cx, top + 2 * r - length * .05, cx + r * .9, top + 2 * r - length * .05), fill=paint("red"), width=int(length * .1))
    d.rectangle((cx + r * .72, top + length * .1, cx + r * .82, top + 2 * r - length * .12), fill=paint("steel"))
    d.line((cx + r * .5, top + 2 * r + 6, cx + r * 1.05, top + 2 * r + 6), fill=paint("steel"), width=4)


def mallet(d, cx, top, length, paint):
    head_w, head_h, handle = length * .42, length * .2, length * .07
    d.rectangle((cx - handle / 2, top + head_h, cx + handle / 2, top + length), fill=paint("steel"))
    d.rounded_rectangle((cx - handle * .7, top + length * .66, cx + handle * .7, top + length), handle * .6, fill=paint("black"))
    d.rounded_rectangle((cx - head_w / 2, top, cx + head_w / 2, top + head_h), head_h * .3, fill=paint("black"))


def tape_measure(d, cx, top, length, paint):
    d.rounded_rectangle((cx - length / 2, top, cx + length / 2, top + length), length * .22, fill=paint("yellow"))
    d.ellipse((cx - length * .26, top + length * .24, cx + length * .26, top + length * .76), fill=paint("black"))
    d.rectangle((cx - length / 2, top + length - 10, cx - length / 2 + 14, top + length - 4), fill=paint("steel"))


def socket(d, cx, top, length, paint):
    d.rounded_rectangle((cx - length * .3, top, cx + length * .3, top + length), 3, fill=paint("steel"))
    d.ellipse((cx - length * .18, top + 3, cx + length * .18, top + length * .36 + 3), fill=paint("hole"))


def pegboard_tools():
    # Steel pegboard with a painted shadow-board tool layout for the corebuster_env_010 workstation, sized for its
    # 2.8 x 1.1 m panel (about 366 px per metre). One slot is left empty so its painted outline reads as a tool in use.
    w, h, pitch = 1024, 400, 12
    rng = random.Random(3070010)
    board = Image.new("RGBA", (w, h), (62, 66, 64, 255))
    d = ImageDraw.Draw(board)
    for _ in range(320):
        x, y, r, s = rng.randrange(w), rng.randrange(h), rng.randrange(6, 42), rng.randrange(-10, 9)
        d.ellipse((x - r, y - r, x + r, y + r), fill=(62 + s, 66 + s, 64 + s, 255))
    board = board.filter(ImageFilter.GaussianBlur(9))
    d = ImageDraw.Draw(board)
    for y in range(pitch // 2, h, pitch):
        for x in range(pitch // 2, w, pitch):
            d.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(15, 16, 15, 255))
            d.point((x + 2, y + 2), fill=(96, 100, 97, 255))

    layout = [(wrench, 46 + i * 38, 58, 120 + i * 10, None) for i in range(8)]
    layout += [
        (hammer, 400, 76, 196, None),
        (screwdriver, 466, 70, 150, "red"), (screwdriver, 496, 70, 136, "yellow"), (screwdriver, 526, 70, 160, "black"),
        (screwdriver, 556, 70, 128, "red"), (screwdriver, 586, 70, 144, "yellow"),
        (pliers, 650, 64, 176, None), (pliers, 714, 64, 164, None),
        (c_clamp, 800, 64, 150, None), (mallet, 912, 70, 184, None),
        (tape_measure, 960, 286, 64, None),
    ]
    layout += [(socket, 62 + i * 30, 300, 26 + i * 2, None) for i in range(9)]
    hooks = [(x, top) for _, x, top, _, _ in layout if top < 200]
    missing = 7  # the largest wrench

    colours = {"steel": STEEL, "light": STEEL_LIGHT, "dark": STEEL_DARK, "red": GRIP_RED, "yellow": YELLOW,
               "black": GRIP_BLACK, "hole": (0, 0, 0, 0)}

    def draw(target, tool, paint):
        fn, x, top, length, grip = tool
        if fn is screwdriver:
            fn(target, x, top, length, paint, grip)
        else:
            fn(target, x, top, length, paint)

    # Painted silhouettes: every slot's outline, dilated by 3 px, in worn yellow shadow-board paint.
    silhouettes = Image.new("L", (w, h), 0)
    for tool in layout:
        draw(ImageDraw.Draw(silhouettes), tool, lambda part: 0 if part == "hole" else 255)
    silhouettes = silhouettes.filter(ImageFilter.MaxFilter(7))
    board.paste(Image.new("RGBA", (w, h), SHADOW_PAINT), (0, 0), silhouettes.point(lambda v: v * 205 // 255))

    # The hanging tools themselves, with a soft contact shadow below-right of each.
    mask = Image.new("L", (w, h), 0)
    tools = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for i, tool in enumerate(layout):
        if i == missing:
            continue
        draw(ImageDraw.Draw(mask), tool, lambda part: 0 if part == "hole" else 255)
        draw(ImageDraw.Draw(tools), tool, lambda part: colours[part])
    shadow = Image.new("L", (w, h), 0)
    shadow.paste(mask, (4, 6))
    shadow = shadow.filter(ImageFilter.GaussianBlur(3)).point(lambda v: v * 150 // 255)
    board.paste(Image.new("RGBA", (w, h), (8, 8, 8, 255)), (0, 0), shadow)
    board.paste(tools, (0, 0), mask)

    d = ImageDraw.Draw(board, "RGBA")
    for x, top in hooks:
        d.line((x, top - 8, x, top + 3), fill=(38, 39, 38, 255), width=3)
    d.rectangle((40, 296, 340, 300), fill=STEEL_DARK)
    # Wear: fine scratches, grime settling toward the bench, and darker edges.
    for _ in range(90):
        x, y = rng.randrange(w), rng.randrange(h)
        length, angle = rng.randrange(6, 24), rng.uniform(-.5, .5)
        d.line((x, y, x + length * math.cos(angle), y + length * math.sin(angle)), fill=(150, 154, 150, 40), width=1)
    grime = Image.new("L", (w, h), 0)
    gd = ImageDraw.Draw(grime)
    for y in range(h):
        gd.line((0, y, w, y), fill=int(max(0, y - h * .55) / (h * .45) * 90))
    for x in range(40):
        for column in (x, w - 1 - x):
            gd.line((column, 0, column, h), fill=(40 - x) * 2)
    board.paste(Image.new("RGBA", (w, h), (20, 18, 14, 255)), (0, 0), grime)
    board.convert("RGB").save(OUT / "Pegboard_Tools.png")


def start_here_stencil():
    w, h = 512, 512
    image = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    cx = 256
    rng = random.Random(3070014)

    # Worn cream stencil paint (semi-translucent industrial paint on metal)
    STENCIL = (180, 176, 160, 205)

    # Double chevron pointing up (forward)
    pts1 = [
        (cx, 40), (cx + 84, 110), (cx + 56, 110), (cx, 66), (cx - 56, 110), (cx - 84, 110)
    ]
    d.polygon(pts1, fill=STENCIL)

    pts2 = [
        (cx, 120), (cx + 84, 190), (cx + 56, 190), (cx, 146), (cx - 56, 190), (cx - 84, 190)
    ]
    d.polygon(pts2, fill=STENCIL)

    # Stencil cuts down the center apex
    d.line((cx, 30, cx, 195), fill=(0, 0, 0, 0), width=6)

    # Stencil text
    label(d, (cx, 265), "START HERE", 48, STENCIL, anchor="mm")
    label(d, (cx, 350), "GO FURTHER", 48, STENCIL, anchor="mm")

    # Stencil horizontal bridges through text
    d.line((100, 265, 412, 265), fill=(0, 0, 0, 0), width=3)
    d.line((90, 350, 422, 350), fill=(0, 0, 0, 0), width=3)

    # Distressed paint wear: fine scratches and speckles
    for _ in range(140):
        x, y = rng.randrange(w), rng.randrange(h)
        length, angle = rng.randrange(4, 20), rng.uniform(-math.pi, math.pi)
        d.line((x, y, x + length * math.cos(angle), y + length * math.sin(angle)), fill=(0, 0, 0, 0), width=rng.choice([1, 2]))

    for _ in range(300):
        x, y = rng.randrange(w), rng.randrange(h)
        d.point((x, y), fill=(0, 0, 0, 0))

    image.save(OUT / "Floor_Start_Here.png")


def dashed_safety_lines():
    # 512x64 tileable dashed line (4 cycles of 80px dash + 48px gap)
    w, h = 512, 64
    image = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    rng = random.Random(3070015)

    dash_len = 80
    gap = 48
    top_y = 16
    bot_y = 48

    for i in range(4):
        x_start = i * (dash_len + gap)
        x_end = x_start + dash_len
        d.rectangle((x_start, top_y, x_end, bot_y), fill=YELLOW)
        d.line((x_start, top_y, x_end, top_y), fill=(80, 60, 20, 180), width=2)
        d.line((x_start, bot_y, x_end, bot_y), fill=(80, 60, 20, 180), width=2)
        d.line((x_start, top_y, x_start, bot_y), fill=(80, 60, 20, 140), width=2)
        d.line((x_end, top_y, x_end, bot_y), fill=(80, 60, 20, 140), width=2)

        # Scratches inside the dash only
        for _ in range(14):
            sx = rng.randrange(x_start + 4, x_end - 10)
            sy = rng.randrange(top_y + 3, bot_y - 2)
            length = rng.randrange(3, 8)
            d.line((sx, sy, sx + length, sy), fill=(120, 90, 30, 140), width=1)

    image.save(OUT / "Floor_Dashed_Line.png")

    # 256x256 90-degree corner return
    corner = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    cd = ImageDraw.Draw(corner)
    crng = random.Random(3070016)

    # Corner dash
    cd.rectangle((16, 16, 96, 48), fill=YELLOW)
    cd.rectangle((16, 16, 48, 96), fill=YELLOW)
    cd.rectangle((144, 16, 224, 48), fill=YELLOW)
    cd.rectangle((16, 144, 48, 224), fill=YELLOW)

    # Scuffs on corner dashes
    for box in [(16, 16, 96, 48), (16, 48, 48, 96), (144, 16, 224, 48), (16, 144, 48, 224)]:
        for _ in range(10):
            sx = crng.randrange(box[0] + 2, box[2] - 6)
            sy = crng.randrange(box[1] + 2, box[3] - 2)
            cd.line((sx, sy, sx + crng.randrange(3, 6), sy), fill=(80, 60, 20, 140), width=1)

    corner.save(OUT / "Floor_Dashed_Corner.png")


def floor_grates():
    # Long rectangular grate (512x128) - dark industrial weathered steel
    w, h = 512, 128
    image = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)
    frame_w = 12

    d.rectangle((0, 0, w - 1, h - 1), fill=(48, 50, 52, 255), outline=(32, 34, 36, 255), width=2)
    d.rectangle((frame_w, frame_w, w - 1 - frame_w, h - 1 - frame_w), fill=(8, 9, 10, 255))

    pitch = 16
    bar_w = 6
    for x in range(frame_w + 4, w - frame_w - 4, pitch):
        d.rectangle((x, frame_w, x + bar_w, h - 1 - frame_w), fill=(65, 68, 70, 255))
        d.line((x, frame_w, x, h - 1 - frame_w), fill=(95, 100, 105, 255), width=1)
        d.line((x + bar_w, frame_w, x + bar_w, h - 1 - frame_w), fill=(25, 26, 28, 255), width=1)

    mid_y = h // 2
    d.rectangle((frame_w, mid_y - 3, w - 1 - frame_w, mid_y + 3), fill=(45, 48, 50, 255))
    d.line((frame_w, mid_y - 3, w - 1 - frame_w, mid_y - 3), fill=(75, 80, 85, 255), width=1)

    for bx in range(24, w, 48):
        for by in (6, h - 7):
            d.ellipse((bx - 4, by - 4, bx + 4, by + 4), fill=(25, 26, 28, 255), outline=(15, 16, 17, 255), width=1)
            d.ellipse((bx - 2, by - 2, bx + 2, by + 2), fill=(70, 74, 78, 255))

    image.save(OUT / "Floor_Grate_Long.png")

    # Square vent grille (256x256) - dark industrial steel
    vw, vh = 256, 256
    vimage = Image.new("RGBA", (vw, vh), (0, 0, 0, 0))
    vd = ImageDraw.Draw(vimage)
    rim = 16

    vd.rectangle((0, 0, vw - 1, vh - 1), fill=(45, 48, 50, 255), outline=(65, 70, 75, 255), width=2)
    vd.rectangle((rim, rim, vw - 1 - rim, vh - 1 - rim), fill=(8, 9, 10, 255))

    blade_pitch = 18
    blade_h = 10
    for y in range(rim + 6, vh - rim - blade_h, blade_pitch):
        vd.rectangle((rim + 2, y, vw - 1 - rim - 2, y + blade_h), fill=(55, 58, 60, 255))
        vd.line((rim + 2, y, vw - 1 - rim - 2, y), fill=(80, 85, 90, 255), width=2)
        vd.line((rim + 2, y + blade_h, vw - 1 - rim - 2, y + blade_h), fill=(15, 16, 18, 255), width=2)

    for bx in (8, vw - 9):
        for by in (8, vh - 9):
            vd.ellipse((bx - 4, by - 4, bx + 4, by + 4), fill=(25, 26, 28, 255), outline=(15, 16, 17, 255), width=1)
            vd.ellipse((bx - 2, by - 2, bx + 2, by + 2), fill=(70, 74, 78, 255))

    vimage.save(OUT / "Floor_Vent_Grille.png")


def drain_covers():
    # Square drain cover (256x256) - dark cast iron
    w, h = 256, 256
    image = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(image)

    d.rectangle((0, 0, w - 1, h - 1), fill=(42, 45, 48, 255), outline=(28, 30, 32, 255), width=2)
    cx, cy, r = 128, 128, 100
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(8, 10, 12, 255), outline=(65, 70, 75, 255), width=3)

    for cr in (78, 56, 34):
        d.ellipse((cx - cr, cy - cr, cx + cr, cy + cr), outline=(52, 56, 60, 255), width=6)
        d.ellipse((cx - cr - 3, cy - cr - 3, cx + cr + 3, cy + cr + 3), outline=(75, 80, 85, 255), width=1)

    d.ellipse((cx - 16, cy - 16, cx + 16, cy + 16), fill=(45, 48, 52, 255), outline=(70, 75, 80, 255), width=2)
    d.ellipse((cx - 6, cy - 6, cx + 6, cy + 6), fill=(6, 8, 10, 255))

    for i in range(8):
        ang = i * math.pi / 4
        x1 = cx + int(24 * math.cos(ang))
        y1 = cy + int(24 * math.sin(ang))
        x2 = cx + int(94 * math.cos(ang))
        y2 = cy + int(94 * math.sin(ang))
        d.line((x1, y1, x2, y2), fill=(8, 10, 12, 255), width=4)

    for bx in (8, w - 9):
        for by in (8, h - 9):
            d.ellipse((bx - 4, by - 4, bx + 4, by + 4), fill=(25, 26, 28, 255), outline=(15, 16, 17, 255), width=1)
            d.ellipse((bx - 2, by - 2, bx + 2, by + 2), fill=(65, 70, 75, 255))

    image.save(OUT / "Floor_Drain_Square.png")


def make_oil_stain(w, h, seed, num_blobs, blur_radius, num_drips, filename):
    rng = random.Random(seed)
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)
    cx, cy = w // 2, h // 2

    for _ in range(num_blobs):
        rx = rng.randint(w // 8, w // 3)
        ry = rng.randint(h // 8, h // 3)
        ox = rng.randint(-w // 6, w // 6)
        oy = rng.randint(-h // 6, h // 6)
        d.ellipse((cx + ox - rx, cy + oy - ry, cx + ox + rx, cy + oy + ry), fill=210)

    mask = mask.filter(ImageFilter.GaussianBlur(blur_radius))

    d_mask = ImageDraw.Draw(mask)
    for _ in range(num_drips):
        dist = rng.randint(w // 5, int(w * 0.45))
        angle = rng.uniform(0, 2 * math.pi)
        dx = int(cx + dist * math.cos(angle))
        dy = int(cy + dist * math.sin(angle))
        dr = rng.randint(3, max(4, w // 40))
        d_mask.ellipse((dx - dr, dy - dr, dx + dr, dy + dr), fill=rng.randint(140, 230))

    puddle = Image.new("RGBA", (w, h), (18, 16, 14, 0))
    puddle.putalpha(mask)

    core = Image.new("L", (w, h), 0)
    cd = ImageDraw.Draw(core)
    for _ in range(num_blobs // 2):
        rx = rng.randint(w // 12, w // 5)
        ry = rng.randint(h // 12, h // 5)
        ox = rng.randint(-w // 10, w // 10)
        oy = rng.randint(-h // 10, h // 10)
        cd.ellipse((cx + ox - rx, cy + oy - ry, cx + ox + rx, cy + oy + ry), fill=80)
    core = core.filter(ImageFilter.GaussianBlur(blur_radius // 2))

    core_rgba = Image.new("RGBA", (w, h), (8, 7, 6, 0))
    core_rgba.putalpha(core)
    puddle = Image.alpha_composite(puddle, core_rgba)

    puddle.save(OUT / filename)


def oil_stains():
    make_oil_stain(512, 512, 3070018, 35, 16, 30, "Floor_Oil_Stain_Large.png")
    make_oil_stain(256, 256, 3070019, 22, 10, 18, "Floor_Oil_Stain_Medium.png")
    make_oil_stain(256, 256, 3070020, 14, 7, 12, "Floor_Oil_Stain_Small.png")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    blueprint()
    warning()
    floor_arrow()
    hazard_trim()
    pegboard_tools()
    start_here_stencil()
    dashed_safety_lines()
    floor_grates()
    drain_covers()
    oil_stains()
    print(f"Wrote 12 original Start Workshop textures to {OUT}")


if __name__ == "__main__":
    main()
