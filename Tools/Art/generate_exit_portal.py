"""Generate the first-principles Start Workshop exit board.

Run: python Tools/Art/generate_exit_portal.py
Requires Pillow. corebuster_art_003 restyled the board with the hand-built stencil lettering (stencil_font.py, no
font file or licence) and the shared sign wear from generate_start_workshop_signage.py. All decoration, weathering
and layout are authored here; no external image is imported. The size (1536 x 320) matches the 4.15 x 0.86 m board.
"""

import random
from pathlib import Path
from PIL import Image, ImageDraw

import stencil_font as sf
from generate_start_workshop_signage import paint_layer, rivet, weather_panel

OUT = Path(__file__).resolve().parents[2] / "Assets/FPS/Art/Textures/StartWorkshop/Portal_To_Corridors.png"


def chevrons(d, x, y, height, direction, fill, count=2):
    """Solid chevrons pointing along direction (+1 right, -1 left), stepped outward from x."""
    depth, thick = height * .42, height * .34
    for i in range(count):
        cx = x - direction * i * (thick + height * .14)
        d.polygon(((cx, y - height / 2), (cx + direction * thick, y - height / 2), (cx + direction * (thick + depth), y),
                   (cx + direction * thick, y + height / 2), (cx, y + height / 2), (cx + direction * depth, y)), fill=fill)


def main():
    w, h = 1536, 320
    rng = random.Random(307013)
    amber = (236, 178, 67)
    cream = (240, 232, 207)
    image = Image.new("RGB", (w, h), (23, 29, 31))
    d = ImageDraw.Draw(image)
    d.rectangle((11, 11, w - 12, h - 12), outline=amber, width=9)
    d.rectangle((29, 29, w - 30, h - 30), outline=(96, 109, 105), width=2)

    # Double chevrons on both sides converge on the lettering, and on the doorway below it.
    arrows = Image.new("L", (w, h), 0)
    chevrons(ImageDraw.Draw(arrows), 96, 150, 150, 1, 255)
    chevrons(ImageDraw.Draw(arrows), w - 96, 150, 150, -1, 255)
    layer = paint_layer(arrows, 307014, amber, bleed=.08, chips=.6)
    image.paste(layer, (0, 0), layer)

    mask = Image.new("L", (w, h), 0)
    sf.place(mask, "TO CORRIDORS", (w / 2, 110), 118, 18, anchor="mm")
    layer = paint_layer(mask, 307015, cream, bleed=.1, chips=.5)
    image.paste(layer, (0, 0), layer)
    d = ImageDraw.Draw(image)
    d.line((300, 188, w - 300, 188), fill=amber, width=4)
    mask = Image.new("L", (w, h), 0)
    sf.place(mask, "FURTHER OPERATIONS AWAIT", (w / 2, 238), 46, 11, anchor="mm")
    layer = paint_layer(mask, 307016, amber, bleed=.06, chips=.4)
    image.paste(layer, (0, 0), layer)

    bolts = [(x, y) for x in (52, w - 53) for y in (53, h - 54)]
    image = weather_panel(image, 307017, border=.05, rust_from=[(x, y + 8) for x, y in bolts])
    d = ImageDraw.Draw(image)
    for x, y in bolts:
        rivet(d, x, y, 8, head=(113, 118, 108))
    for _ in range(3):
        x = rng.randrange(40, w - 40)
        d.line((x, 36, x + rng.randrange(-8, 8), h - 36), fill=(32, 38, 38), width=1)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    image.save(OUT)
    print(OUT)


if __name__ == "__main__":
    main()
