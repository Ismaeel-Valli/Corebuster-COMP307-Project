"""Draw the corebuster_render_002 Start Workshop grime decals: rust streaks, leak stains, floor scuffs, oil puddles,
wall-edge grime and riveted bolt plates, plus normal maps for the scuffs and the plate.

Run with: python Tools/Art/generate_start_workshop_grime.py
Requires numpy and Pillow. Every shape is synthesised here from seeded random numbers (spectral noise, gaussian
profiles, hand-placed polylines and discs), so the output is reproducible and uses no photograph, scan or stock
decal. The RGBA textures are the base maps of project-owned URP decal materials: the alpha is the decal's coverage,
and the colour is defined even where the alpha is 0, so mipmaps and filtering never pull in a dark fringe.

Orientation contract with StartWorkshopGrime.cs (bottom of the PNG = v 0):
  * Grime_Rust_Streak: the streak source is the TOP row; runs hang downward.
  * Grime_Edge_Strip: the WALL side is the BOTTOM rows; grime fades toward the top.
Every texture fades to alpha 0 at its borders so no decal shows its projector box.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parents[2] / "Assets/FPS/Art/Textures/StartWorkshop/Grime"
SEED = 3020002


# --- Numeric helpers ---------------------------------------------------------------------------------------------------

def rng_for(name):
    """One independent, reproducible generator per texture, so editing one texture never changes another."""
    return np.random.default_rng([SEED, sum(ord(c) * (i + 1) for i, c in enumerate(name))])


def fractal(h, w, rng, beta=2.0):
    """Tileable fractal noise in 0..1: white noise shaped by a 1/f^beta amplitude spectrum."""
    fy = np.fft.fftfreq(h)[:, None] * h
    fx = np.fft.fftfreq(w)[None, :] * w
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    spectrum = np.fft.fft2(rng.standard_normal((h, w))) / f ** (beta / 2.0)
    spectrum[0, 0] = 0.0
    n = np.real(np.fft.ifft2(spectrum))
    n = n / (4.0 * n.std() + 1e-9) + .5
    return np.clip(n, 0.0, 1.0)


def blur(a, sigma):
    """Gaussian blur through the frequency domain (wraps at the borders, which every texture then fades out)."""
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    kernel = np.exp(-2.0 * (np.pi * sigma) ** 2 * (fx * fx + fy * fy))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * kernel))


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def window(h, w, x_frac, y_top_frac, y_bottom_frac):
    """Border fade: alpha reaches 0 at every edge, over the given fraction of the size."""
    x = np.linspace(0, 1, w)[None, :]
    y = np.linspace(0, 1, h)[:, None]
    wx = smoothstep(0.0, x_frac, x) * smoothstep(0.0, x_frac, 1.0 - x)
    wy = smoothstep(0.0, y_top_frac, y) * smoothstep(0.0, y_bottom_frac, 1.0 - y)
    return wx * wy


def lerp_colour(a, b, t):
    a = np.array(a, dtype=np.float64)
    b = np.array(b, dtype=np.float64)
    return a[None, None, :] * (1.0 - t[..., None]) + b[None, None, :] * t[..., None]


def rgba(colour, alpha):
    img = np.concatenate([np.clip(colour, 0, 255), np.clip(alpha, 0, 1)[..., None] * 255.0], axis=2)
    return Image.fromarray(np.uint8(np.round(img)), "RGBA")


def normal_from_height(height, strength):
    """Tangent-space (Y up) normal map from a height field whose image rows run top to bottom."""
    gy, gx = np.gradient(height)
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(height)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / length, ny / length, nz / length], axis=2) * .5 + .5
    return Image.fromarray(np.uint8(np.round(n * 255.0)), "RGB")


def save(img, name):
    OUT.mkdir(parents=True, exist_ok=True)
    img.save(OUT / name, optimize=True)
    print(f"wrote {name} {img.size[0]}x{img.size[1]}")


# --- Textures ----------------------------------------------------------------------------------------------------------

def rust_streak():
    """Rust runs hanging from a source at the top edge: 6 wandering streaks that thin and break up as they fall."""
    w, h = 256, 1024
    rng = rng_for("rust")
    x = np.arange(w)[None, :].astype(np.float64)
    y = np.arange(h)[:, None].astype(np.float64)
    alpha = np.zeros((h, w))
    for _ in range(6):
        cx = w * rng.uniform(.2, .8)
        width = rng.uniform(4, 13)
        length = h * rng.uniform(.5, .97)
        sway = rng.uniform(2, 9)
        period = rng.uniform(90, 260)
        phase = rng.uniform(0, 6.28)
        centre = cx + sway * np.sin(y / period * 6.28 + phase) + .35 * sway * np.sin(y / (period * .37) + phase * 2)
        across = np.exp(-(((x - centre) / (width * (1.0 - .45 * y / h))) ** 2) * 1.4)
        along = np.clip(1.0 - y / length, 0, 1) ** rng.uniform(.7, 1.5)
        # Streaks pulse: water runs in surges, so the rust is thicker in bands.
        bands = .55 + .75 * fractal(h, 1, rng, beta=1.6)
        # A run ends in a drip: a soft bead at its tip.
        tip = np.exp(-(((y - length * .93) / (width * 1.5)) ** 2)) * np.exp(-(((x - centre) / (width * .9)) ** 2))
        alpha = 1.0 - (1.0 - alpha) * (1.0 - np.clip(across * along * bands * .85 + tip * .35, 0, 1))
    # The source: a wide rusty bleed where the water came from.
    source = np.exp(-((y / 46.0) ** 2)) * (.45 + .55 * fractal(h, w, rng, beta=2.2))
    alpha = 1.0 - (1.0 - alpha) * (1.0 - .5 * source)
    grain = fractal(h, w, rng, beta=1.3)
    alpha = alpha * (.55 + .75 * grain)
    alpha = np.clip(alpha * .8, 0, .78) * window(h, w, .06, .012, .05)
    fresh = np.clip(y / h, 0, 1) * np.ones((1, w))
    tone = np.clip(.75 * fractal(h, w, rng, beta=2.6) + .3 * fresh, 0, 1)
    colour = lerp_colour((92, 40, 18), (176, 96, 42), tone)
    save(rgba(colour, alpha), "Grime_Rust_Streak.png")


def leak_stain():
    """A dried leak: tide-mark rings around a damp, darker core, with a wobbling, noise-warped outline."""
    n = 512
    rng = rng_for("leak")
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    dx, dy = (xx - n / 2) / (n / 2), (yy - n / 2) / (n / 2)
    warp = fractal(n, n, rng, beta=3.4) - .5
    warp2 = fractal(n, n, rng, beta=2.4) - .5
    r = np.sqrt(dx * dx + dy * dy * 1.06) + warp * .5 + warp2 * .07
    inside = smoothstep(.98, .80, r)
    rings = np.zeros((n, n))
    for radius, sharp, weight in ((.5, .05, .6), (.7, .035, .85), (.9, .028, 1.0)):
        rings += weight * np.exp(-((r - radius) / sharp) ** 2)
    core = smoothstep(.62, .18, r + .16 * (fractal(n, n, rng, beta=3.0) - .5))
    grain = .72 + .5 * fractal(n, n, rng, beta=2.6)
    alpha = np.clip((inside * .14 + core * .28 + rings * .42) * grain, 0, .62) * window(n, n, .10, .10, .10)
    tone = np.clip(rings * .8 + .25 * fractal(n, n, rng, beta=3.0), 0, 1)
    colour = lerp_colour((34, 30, 27), (108, 82, 46), tone)
    save(rgba(colour, alpha), "Grime_Leak_Stain.png")


def scuff():
    """Floor traffic wear: dark rubber skids and pale scrape lines, all trending along one direction (the texture's U)."""
    w, h = 512, 256
    rng = rng_for("scuff")
    rubber = Image.new("L", (w, h), 0)
    scratch = Image.new("L", (w, h), 0)
    rd, sd = ImageDraw.Draw(rubber), ImageDraw.Draw(scratch)

    def curve(t, y0, bend, tilt):
        return (w * .08 + t * w * .84, y0 + tilt * (t - .5) * h * .5 + bend * np.sin(t * np.pi))

    drag_tilt = rng.uniform(-.25, .25)
    for _ in range(3):
        y0, bend, tilt = rng.uniform(.3, .7) * h, rng.uniform(-10, 10), drag_tilt + rng.uniform(-.06, .06)
        thickness = rng.uniform(4, 10)
        a, b = sorted((rng.uniform(0, .5), rng.uniform(.5, 1)))
        for t in np.linspace(a, b, 90):
            cx, cy = curve(t, y0, bend, tilt)
            taper = np.sin(np.pi * (t - a) / (b - a + 1e-9)) ** .6
            rr = thickness * taper * .5
            rd.ellipse((cx - rr * 1.6, cy - rr, cx + rr * 1.6, cy + rr), fill=int(255 * rng.uniform(.5, 1)))
    for _ in range(34):
        y0, bend, tilt = rng.uniform(.1, .9) * h, rng.uniform(-8, 8), drag_tilt + rng.uniform(-.22, .22)
        a, b = sorted((rng.uniform(0, .6), rng.uniform(.4, 1)))
        pts = [curve(t, y0, bend, tilt) for t in np.linspace(a, b, 24)]
        sd.line(pts, fill=int(255 * rng.uniform(.4, 1)), width=int(rng.integers(1, 3)))
    grain = fractal(h, w, rng, beta=1.2)
    rub = np.asarray(rubber.filter(ImageFilter.GaussianBlur(1.6)), dtype=np.float64) / 255.0 * (.55 + .7 * grain)
    scr = np.asarray(scratch.filter(ImageFilter.GaussianBlur(.6)), dtype=np.float64) / 255.0 * (.5 + .8 * grain)
    edge = window(h, w, .07, .12, .12)
    a_rub, a_scr = np.clip(rub * .62, 0, .62) * edge, np.clip(scr * .42, 0, .42) * edge
    alpha = 1.0 - (1.0 - a_rub) * (1.0 - a_scr)
    weight = (a_scr / np.maximum(a_rub + a_scr, 1e-6))[..., None]
    colour = np.array((16, 15, 14))[None, None, :] * (1 - weight) + np.array((160, 156, 142))[None, None, :] * weight
    save(rgba(colour, alpha), "Grime_Scuff.png")
    height = -.7 * (scr / max(scr.max(), 1e-6)) - .25 * (rub / max(rub.max(), 1e-6))
    save(normal_from_height(blur(height, .8), 5.0), "Grime_Scuff_N.png")


def oil_puddle():
    """An oil spill: near-black core, a slightly raised iridescent rim, satellite drops and a smeared tail."""
    n = 512
    rng = rng_for("oil")
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    dx, dy = (xx - n * .45) / (n * .36), (yy - n / 2) / (n * .30)
    warp = fractal(n, n, rng, beta=3.6) - .5
    r = np.sqrt(dx * dx + dy * dy) + warp * .55
    body = smoothstep(1.0, .55, r)
    # A smear tail dragged to the right by a foot or a wheel.
    tail = np.exp(-(((dy + .1) / .28) ** 2)) * smoothstep(.9, 1.7, dx + 1.0) * smoothstep(2.4, 1.6, dx + 1.0)
    shape = np.clip(body + tail * .55, 0, 1)
    for _ in range(9):
        cx, cy = n * rng.uniform(.12, .9), n * rng.uniform(.12, .88)
        rad = rng.uniform(4, 15)
        shape = np.maximum(shape, smoothstep(rad, rad * .55, np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)) * rng.uniform(.6, 1.0))
    rim = np.exp(-((r - .92) / .07) ** 2) * (.5 + .6 * fractal(n, n, rng, beta=2.0))
    alpha = np.clip(shape * .84, 0, .84) * window(n, n, .06, .06, .06)
    # The rim breaks light into a faint teal-to-violet sheen; the rest is nearly black.
    hue = np.clip(.5 + .5 * np.sin(r * 9.0 + warp * 9.0), 0, 1)
    sheen = lerp_colour((28, 74, 66), (76, 44, 96), hue)
    core = np.array((13, 12, 10))[None, None, :] * np.ones((n, n, 1))
    mix = np.clip(rim * .55, 0, 1)[..., None]
    colour = core * (1 - mix) + sheen * mix
    save(rgba(colour, alpha), "Grime_Oil_Puddle.png")


def edge_strip():
    """Dust and grease that collects where the floor meets a wall: heaviest at the wall side (bottom rows), blotchy."""
    w, h = 1024, 128
    rng = rng_for("edge")
    dist = (1.0 - np.linspace(0, 1, h)[:, None]) * np.ones((1, w))   # 0 at the wall (bottom) row, 1 at the top row
    blotch = fractal(h, w, rng, beta=2.2)
    fine = fractal(h, w, rng, beta=1.1)
    reach = .38 + .62 * blotch                                        # how far the grime creeps from the wall, per column
    profile = np.exp(-((dist / (.42 * reach + .05)) ** 1.6))
    # The wall side keeps its full strength: the projector's box ends at the wall, so no fade is needed there.
    alpha = np.clip(profile * (.45 + .8 * fine) * .8, 0, .78) * window(h, w, .10, .10, .0001)
    tone = np.clip(.6 * blotch + .4 * fine, 0, 1)
    colour = lerp_colour((22, 20, 18), (58, 50, 42), tone)
    save(rgba(colour, alpha), "Grime_Edge_Strip.png")


def bolt_plate():
    """A riveted repair plate: steel, a weld bead round the edge, six bolts with rust bleeding below them."""
    n = 256
    rng = rng_for("plate")
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    pad, corner = 22, 14
    # Rounded-square signed distance: negative inside the plate.
    qx, qy = np.abs(xx - n / 2) - (n / 2 - pad - corner), np.abs(yy - n / 2) - (n / 2 - pad - corner)
    sd = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0) - corner
    plate = smoothstep(1.2, -1.2, sd)
    weld = np.exp(-((sd + 8.0) / 2.6) ** 2)
    halo = smoothstep(pad * .75, 0, sd) * (1 - plate)                      # dark grime hugging the plate outside
    bolts = []
    inset = pad + 26
    for bx, by in ((inset, inset), (n - inset, inset), (inset, n - inset), (n - inset, n - inset),
                   (n / 2, inset - 3), (n / 2, n - inset + 3)):
        bolts.append((bx, by))
    head = np.zeros((n, n))
    rust = np.zeros((n, n))
    for bx, by in bolts:
        d = np.sqrt((xx - bx) ** 2 + (yy - by) ** 2)
        head = np.maximum(head, smoothstep(11.5, 9.5, d))
        # Rust runs down (increasing row) from each bolt.
        rust += np.exp(-(((xx - bx) / 6.5) ** 2)) * smoothstep(0, 14, yy - by) * smoothstep(64, 10, yy - by) * (.6 + .4 * rng.random())
    grain = fractal(n, n, rng, beta=1.5)
    brushed = blur(rng.standard_normal((n, n)), 1.0)
    brushed = (brushed - brushed.min()) / (brushed.max() - brushed.min())
    steel = lerp_colour((58, 60, 62), (96, 98, 98), np.clip(.5 * grain + .5 * brushed, 0, 1))
    steel = steel * (1 - .32 * weld[..., None]) + np.array((122, 118, 108))[None, None, :] * (.32 * weld[..., None])
    steel = steel * (1 - .55 * head[..., None]) + np.array((132, 130, 122))[None, None, :] * (.55 * head[..., None])
    rust_mix = np.clip(rust * (.5 + .6 * fractal(n, n, rng, beta=2.0)) * plate, 0, .85)
    body = steel * (1 - rust_mix[..., None]) + np.array((132, 66, 28))[None, None, :] * rust_mix[..., None]
    grime = np.array((20, 18, 16))[None, None, :]
    alpha = np.clip(plate * .96 + halo * .30 * (.6 + .6 * grain), 0, .96) * window(n, n, .03, .03, .03)
    weight = np.clip(halo, 0, 1)[..., None]
    colour = body * (1 - weight) + grime * weight
    save(rgba(colour, alpha), "Grime_Bolt_Plate.png")
    height = plate * 1.0 + weld * .35 + head * .9
    save(normal_from_height(blur(height, .7), 2.4), "Grime_Bolt_Plate_N.png")


def main():
    rust_streak()
    leak_stain()
    scuff()
    oil_puddle()
    edge_strip()
    bolt_plate()


if __name__ == "__main__":
    main()
