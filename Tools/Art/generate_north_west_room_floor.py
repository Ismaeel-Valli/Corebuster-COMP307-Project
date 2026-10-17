"""Draw the corebuster_env_042 North-West Room floor-definition atlases.

Run with: python Tools/Art/generate_north_west_room_floor.py
Requires numpy and Pillow. Every shape is synthesised here from seeded random numbers and hand-placed geometry, so the
output is reproducible and uses no photograph, scan or stock decal. Colours are the corebuster_art_005 swatches
(NorthWestRoomPalette.json): hazard_yellow, dark_gunmetal, warm_grey_plating and worn_paint_and_grime.

Two 1024x1024 RGBA atlases, each a 2x2 grid of 512 px cells (image row 0 = top; Unity v = 1 - y / 1024).
The cell order is a contract with NorthWestRoomFloor.cs:
  Floor_Markings.png: A (0,0) yellow lane line, B (1,0) chevron pointing +u, C (0,1) arena rings, D (1,1) zone plate.
  Floor_Hardware.png: E (0,0) grate, F (1,0) drain, G (0,1) cable trench, H (1,1) open-space roundel.
Lines and the trench are stretched along u, so their texture varies mainly across v. Colour is defined where alpha is 0,
so mip-mapping never pulls a dark fringe in.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parents[2] / "Assets/FPS/Art/NorthWestRoom/Floor_Detail/Textures"
SEED = 3042042
CELL = 512

HAZARD = (219, 153, 31)       # hazard_yellow 0.86, 0.60, 0.12
GUNMETAL = (64, 71, 69)       # dark_gunmetal 0.25, 0.28, 0.27
WARM_GREY = (128, 122, 107)   # warm_grey_plating 0.50, 0.48, 0.42
PALE = (205, 195, 170)        # a lifted warm grey for the open-space marks


def rng_for(name):
    return np.random.default_rng([SEED, sum(ord(c) * (i + 1) for i, c in enumerate(name))])


def noise(rng, size, scale, stretch=1.0):
    """Smooth value noise in 0..1; stretch > 1 elongates it along u (axis 1)."""
    h, w = size
    gh, gw = max(2, int(h / scale) + 2), max(2, int(w / (scale * stretch)) + 2)
    grid = rng.random((gh, gw)).astype(np.float32)
    img = Image.fromarray((grid * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(img, dtype=np.float32) / 255.0


def rgba(colour, alpha):
    h, w = alpha.shape
    out = np.zeros((h, w, 4), dtype=np.uint8)
    out[..., 0], out[..., 1], out[..., 2] = colour
    out[..., 3] = np.clip(alpha * 255, 0, 255).astype(np.uint8)
    return out


def shade(base, amount):
    """Per-pixel brightness variation of a base colour; amount is an (h, w) array around 1."""
    return (np.array(base, dtype=np.float32)[None, None, :] * amount[..., None]).clip(0, 255).astype(np.uint8)


def paste_cell(atlas, cell, col, row):
    atlas[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL] = cell


def wear_alpha(rng, name, base=0.94, chips=0.34):
    """Worn-paint coverage: mostly solid with scuffs and chips, elongated along u."""
    n1 = noise(rng_for(name + "1"), (CELL, CELL), 18, 5.0)
    n2 = noise(rng_for(name + "2"), (CELL, CELL), 5, 2.0)
    return np.clip(base - chips * np.clip((n1 - .55) * 3.0, 0, 1) - .20 * np.clip((n2 - .62) * 4.0, 0, 1), .25, 1)


def cell_line():
    rng = rng_for("line")
    a = wear_alpha(rng, "line")
    v = np.linspace(0, 1, CELL)[:, None]
    edge = np.clip(np.minimum(v, 1 - v) * CELL / 14.0, 0, 1)  # soft, slightly broken edges
    jitter = noise(rng_for("line_edge"), (CELL, CELL), 9, 6.0)
    a = a * np.clip(edge + (jitter - .5) * .5, 0, 1)
    rgb = shade(HAZARD, .9 + .2 * noise(rng_for("line_tone"), (CELL, CELL), 12, 4.0))
    out = rgba(HAZARD, a)
    out[..., :3] = rgb
    return out


def cell_chevron():
    img = Image.new("L", (CELL, CELL), 0)
    d = ImageDraw.Draw(img)
    m, t = 56, 150  # margin and stroke thickness along u
    pts = [(m, m), (m + t, m), (CELL - m, CELL // 2), (m + t, CELL - m), (m, CELL - m), (CELL - m - t, CELL // 2)]
    d.polygon(pts, fill=255)
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32) / 255.0
    a = a * wear_alpha(rng_for("chev"), "chev", .92, .5)
    out = rgba(HAZARD, a)
    out[..., :3] = shade(HAZARD, .9 + .2 * noise(rng_for("chev_tone"), (CELL, CELL), 14))
    return out


def cell_arena():
    img = Image.new("L", (CELL, CELL), 0)
    d = ImageDraw.Draw(img)
    c = CELL / 2
    for radius, width, dashes in ((CELL * .488, 8, 72), (CELL * .244, 6, 36)):
        step = 360.0 / dashes
        for i in range(dashes):
            if i % 5 == 4:
                continue  # broken, worn ring
            a0 = i * step
            d.arc([c - radius, c - radius, c + radius, c + radius], a0, a0 + step * .62, fill=255, width=width)
    # eight short ticks on the outer ring, so the arena reads as an intended floor feature
    for k in range(8):
        ang = np.deg2rad(k * 45 + 22.5)
        r0, r1 = CELL * .46, CELL * .488
        d.line([(c + r0 * np.cos(ang), c + r0 * np.sin(ang)), (c + r1 * np.cos(ang), c + r1 * np.sin(ang))], fill=255, width=8)
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(.8)), dtype=np.float32) / 255.0
    a = a * .85 * wear_alpha(rng_for("arena"), "arena", 1.0, .5)
    out = rgba(PALE, a)
    return out


def cell_plate():
    rng = rng_for("plate")
    n = noise(rng, (CELL, CELL), 10)
    a = np.full((CELL, CELL), .50, dtype=np.float32) + (n - .5) * .12
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    # fine tread scratches
    a += .06 * (((xx + yy) // 14) % 2) * 0.5
    out = rgba(GUNMETAL, np.clip(a, 0, 1))
    out[..., :3] = shade(GUNMETAL, .85 + .3 * n)
    return out


def cell_grate():
    img = np.zeros((CELL, CELL, 4), dtype=np.uint8)
    base = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(base)
    frame = (92, 90, 80, 255)
    d.rounded_rectangle([14, 14, CELL - 15, CELL - 15], radius=14, fill=frame)
    d.rounded_rectangle([46, 46, CELL - 47, CELL - 47], radius=6, fill=(14, 14, 13, 255))
    bars = 16
    x0, x1 = 46, CELL - 47
    for i in range(bars):
        x = x0 + (x1 - x0) * (i + .5) / bars
        d.rectangle([x - 7, 46, x + 7, CELL - 47], fill=(104, 100, 88, 255))
    d.rectangle([46, CELL // 2 - 8, CELL - 47, CELL // 2 + 8], fill=(92, 90, 80, 255))
    arr = np.asarray(base, dtype=np.float32)
    tone = .88 + .24 * noise(rng_for("grate"), (CELL, CELL), 8)
    arr[..., :3] = (arr[..., :3] * tone[..., None]).clip(0, 255)
    img[:] = arr.astype(np.uint8)
    return img


def cell_drain():
    base = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(base)
    c = CELL / 2
    d.ellipse([c - 236, c - 236, c + 236, c + 236], fill=(92, 90, 80, 255))
    d.ellipse([c - 196, c - 196, c + 196, c + 196], fill=(16, 16, 14, 255))
    for i in range(-5, 6):
        y = c + i * 30
        half = np.sqrt(max(0.0, 190 ** 2 - (y - c) ** 2))
        if half > 18:
            d.rounded_rectangle([c - half + 10, y - 7, c + half - 10, y + 7], radius=6, fill=(104, 100, 88, 255))
    arr = np.asarray(base, dtype=np.float32)
    arr[..., :3] = (arr[..., :3] * (.88 + .24 * noise(rng_for("drain"), (CELL, CELL), 8))[..., None]).clip(0, 255)
    return arr.astype(np.uint8)


def cell_trench():
    rng = rng_for("trench")
    yy = np.arange(CELL)[:, None].astype(np.float32)
    edge = np.minimum(yy, CELL - 1 - yy)
    lip = np.clip(1 - np.abs(edge - 30) / 22.0, 0, 1)         # lighter steel lip
    inner = (edge > 52).astype(np.float32)
    n = noise(rng, (CELL, CELL), 14, 6.0)
    colour = np.zeros((CELL, CELL, 3), dtype=np.float32)
    inner_c = np.array((46, 43, 38), dtype=np.float32)
    lip_c = np.array((104, 100, 88), dtype=np.float32)
    body = inner_c * (.8 + .4 * n[..., None])
    colour = np.where(inner[..., None] > 0, body, lip_c * (.85 + .3 * n[..., None]))
    a = np.clip(.97 * np.clip(edge / 8.0, 0, 1), 0, 1) * np.ones((1, CELL), dtype=np.float32)
    out = np.zeros((CELL, CELL, 4), dtype=np.uint8)
    out[..., :3] = colour.clip(0, 255).astype(np.uint8)
    out[..., 3] = (a * 255).astype(np.uint8)
    return out


def cell_roundel():
    img = Image.new("L", (CELL, CELL), 0)
    d = ImageDraw.Draw(img)
    c = CELL / 2
    r = CELL * .42
    for i in range(40):
        if i % 4 == 3:
            continue
        a0 = i * 9
        d.arc([c - r, c - r, c + r, c + r], a0, a0 + 7, fill=255, width=14)
    for k in range(4):  # four short ticks; reads as a floor mark, not a hazard
        ang = np.deg2rad(k * 90 + 45)
        r0, r1 = CELL * .44, CELL * .48
        d.line([(c + r0 * np.cos(ang), c + r0 * np.sin(ang)), (c + r1 * np.cos(ang), c + r1 * np.sin(ang))], fill=255, width=14)
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(.8)), dtype=np.float32) / 255.0
    a = a * .80 * wear_alpha(rng_for("roundel"), "roundel", 1.0, .5)
    return rgba(PALE, a)


def build(name, cells):
    atlas = np.zeros((CELL * 2, CELL * 2, 4), dtype=np.uint8)
    for (col, row), cell in cells.items():
        paste_cell(atlas, cell, col, row)
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray(atlas, "RGBA").save(OUT / name)
    print("wrote", OUT / name)


if __name__ == "__main__":
    build("Floor_Markings.png", {(0, 0): cell_line(), (1, 0): cell_chevron(), (0, 1): cell_arena(), (1, 1): cell_plate()})
    build("Floor_Hardware.png", {(0, 0): cell_grate(), (1, 0): cell_drain(), (0, 1): cell_trench(), (1, 1): cell_roundel()})
