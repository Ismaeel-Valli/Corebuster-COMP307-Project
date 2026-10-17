"""First-principles soft particle sprites for corebuster_env_017."""
from pathlib import Path
from PIL import Image
import math

OUT = Path("Assets/FPS/Art/Textures/StartWorkshop/Ambient")
OUT.mkdir(parents=True, exist_ok=True)
SIZE = 64


def sprite(name, colour, alpha):
    image = Image.new("RGBA", (SIZE, SIZE))
    pixels = image.load()
    for y in range(SIZE):
        for x in range(SIZE):
            dx = (x + .5 - SIZE / 2) / (SIZE / 2)
            dy = (y + .5 - SIZE / 2) / (SIZE / 2)
            radius = math.hypot(dx, dy)
            pixels[x, y] = (*colour, round(255 * max(0, min(1, alpha(dx, dy, radius)))))
    image.save(OUT / name)


sprite("Steam.png", (197, 220, 224), lambda x, y, r: .23 * max(0, 1 - r * r) ** 2)
sprite("Spark.png", (255, 203, 104), lambda x, y, r: .95 * max(0, 1 - math.hypot(x * 2.4, y * .8)) ** 1.3)
sprite("Dust.png", (239, 226, 189), lambda x, y, r: .38 * max(0, 1 - r) ** 2)
