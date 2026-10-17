"""corebuster_env_057: deterministic first-principles assembly identification plate.

Uses the project's hand-drawn stencil glyphs; no fonts, stock imagery or AI assets.
Run from any directory with Python and Pillow. Unity owns import settings and metadata.
"""
from pathlib import Path
import hashlib
from PIL import Image, ImageDraw
from stencil_font import text_mask, fit_cap

DEST = Path(__file__).resolve().parents[2] / "Assets/FPS/Art/BossHall/Wayfinding"


def generate():
    image = Image.new("RGB", (2048, 512), (19, 27, 32))
    draw = ImageDraw.Draw(image)
    # Printed yellow edge bands, interrupted at the corners by black diagonal cuts.
    amber = (226, 177, 61)
    draw.rectangle((16, 16, 2031, 33), fill=amber)
    draw.rectangle((16, 478, 2031, 495), fill=amber)
    for x in (16, 1904):
        draw.rectangle((x, 16, x + 127, 495), fill=amber)
        for y in range(-128, 640, 64):
            draw.polygon([(x, y), (x + 127, y + 127), (x + 127, y + 156), (x, y + 29)], fill=(19, 27, 32))
    label = "FINAL ASSEMBLY"
    cap = min(218, fit_cap(label, 1630, weight=18))
    mask = text_mask(label, cap, weight=18)
    image.paste((236, 232, 211), ((2048 - mask.width) // 2, (512 - mask.height) // 2), mask)
    DEST.mkdir(parents=True, exist_ok=True)
    path = DEST / "T_Final_Assembly.png"
    image.save(path, optimize=False)
    return path


if __name__ == "__main__":
    result = generate()
    print(f"{result.name}: SHA-256 {hashlib.sha256(result.read_bytes()).hexdigest()}")
