"""Hand-built stencil lettering for the Start Workshop graphics (corebuster_art_003).

Every glyph is defined here from rectangles, rounded rectangles and straight strokes on a 100-unit cap height, so
the project's lettering has no font file and no third-party licence. Letters with enclosed counters carry the
stencil bridges a cut sheet needs (split stems on B D E F K P R, split bowls on O Q 0 8 6 9, a split apex on A).

Weight is the stroke thickness in units: 18 is the bold display weight, 11 a light weight for small annotations.
"""

import math
from PIL import Image, ImageChops, ImageDraw

CAP = 100.0
SUPERSAMPLE = 4


def _seg(x0, y0, x1, y1, t):
    """A straight stroke of perpendicular thickness t with butt ends."""
    dx, dy = x1 - x0, y1 - y0
    n = math.hypot(dx, dy)
    nx, ny = -dy / n * t / 2, dx / n * t / 2
    return ("poly", [(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)])


def _ring(x0, y0, x1, y1, r, t):
    return [("+", ("rrect", (x0, y0, x1, y1, r))), ("-", ("rrect", (x0 + t, y0 + t, x1 - t, y1 - t, max(r - t, 0))))]


def _glyph(ch, t, g):
    """(advance width, [(op, shape)]) for one character; op is '+' (paint) or '-' (cut)."""
    R = t * 1.4
    rect = lambda *p: ("rect", p)

    def split(w_top=100):
        return ("-", ("rect", (t, 0, t + g, w_top)))

    def bridges(cx, top=True, bottom=True):
        out = []
        if top:
            out.append(("-", ("rect", (cx - g / 2, 0, cx + g / 2, t))))
        if bottom:
            out.append(("-", ("rect", (cx - g / 2, CAP - t, cx + g / 2, CAP))))
        return out

    seg = lambda *p: ("+", _seg(*p, t))
    mid = 50

    if ch == "A":
        # Flat-topped trapezoid with its two counters cut out and the apex split.
        w, top, bar = 68, 16, 62
        h = t / math.cos(math.atan((w - top) / 2 / 100))
        left = lambda y: h + (w - top) / 2 * (100 - y) / 100
        right = lambda y: w - left(y)
        apex = 100 - (w / 2 - h) / ((w - top) / 2) * 100
        low = bar + t * .9
        return w, [("+", ("poly", [(0, 100), ((w - top) / 2, 0), ((w + top) / 2, 0), (w, 100)])),
                   ("-", ("poly", [(w / 2, apex), (right(bar), bar), (left(bar), bar)])),
                   ("-", ("poly", [(left(low), low), (right(low), low), (right(100), 100), (left(100), 100)])),
                   ("-", rect(w / 2 - g / 2, 0, w / 2 + g / 2, apex + 1))]
    if ch == "B":
        w = 62
        return w, [("+", rect(0, 0, t, 100))] + _ring(0, 0, 56, mid + t / 2, R * .8, t) + \
            _ring(0, mid - t / 2, w, 100, R, t) + \
            [("+", rect(0, 0, R, t)), ("+", rect(0, mid - t / 2, R, mid + t / 2)), ("+", rect(0, 100 - t, R, 100)), split()]
    if ch == "C":
        w = 62
        return w, _ring(0, 0, w, 100, R, t) + [("-", rect(w - t - 1, 36, w, 64))]
    if ch == "D":
        w = 64
        return w, _ring(0, 0, w, 100, R, t) + [("+", rect(0, 0, R, t)), ("+", rect(0, 100 - t, R, 100)),
                                               ("+", rect(0, 0, t, 100)), split()]
    if ch == "E":
        w = 54
        return w, [("+", rect(0, 0, t, 100)), ("+", rect(0, 0, w, t)), ("+", rect(0, mid - t / 2, w - 6, mid + t / 2)),
                   ("+", rect(0, 100 - t, w, 100)), split()]
    if ch == "F":
        w = 52
        return w, [("+", rect(0, 0, t, 100)), ("+", rect(0, 0, w, t)), ("+", rect(0, mid - t / 2, w - 6, mid + t / 2)),
                   split()]
    if ch == "G":
        w = 64
        return w, _ring(0, 0, w, 100, R, t) + [("-", rect(w - t - 1, 30, w, mid)), ("+", rect(34, mid, w, mid + t))]
    if ch == "H":
        w = 62
        return w, [("+", rect(0, 0, t, 100)), ("+", rect(w - t, 0, w, 100)), ("+", rect(0, mid - t / 2, w, mid + t / 2))]
    if ch == "I":
        return t, [("+", rect(0, 0, t, 100))]
    if ch == "J":
        w = 54
        return w, [("+", ("rrect", (0, 20, w, 100, R))), ("-", ("rrect", (t, -10, w - t, 100 - t, max(R - t, 0)))),
                   ("-", rect(0, 0, t, 62)), ("+", rect(w - t, 0, w, 80))]
    if ch == "K":
        w = 62
        # The arm runs into the stem so the stencil split, not a butt end, is what separates them.
        ax, ay, bx, by = w - t * .45, -8, t * .5, 70
        leg = ax + (bx - ax) * (50 - ay) / (by - ay)
        return w, [("+", rect(0, 0, t, 100)), seg(ax, ay, bx, by), seg(leg, 50, w - t * .4, 108), split()]
    if ch == "L":
        w = 50
        return w, [("+", rect(0, 0, t, 100)), ("+", rect(0, 100 - t, w, 100))]
    if ch == "M":
        w = 78
        return w, [("+", rect(0, 0, t, 100)), ("+", rect(w - t, 0, w, 100)),
                   ("+", ("poly", [(0, 0), (t * 1.25, 0), (w / 2, 54), (w - t * 1.25, 0), (w, 0),
                                   (w / 2 + t * .55, 76), (w / 2 - t * .55, 76)]))]
    if ch == "N":
        w = 64
        return w, [("+", rect(0, 0, t, 100)), ("+", rect(w - t, 0, w, 100)),
                   ("+", ("poly", [(0, 0), (t + 4, 0), (w, 100), (w - t - 4, 100)]))]
    if ch in "O0":
        w = 66 if ch == "O" else 62
        return w, _ring(0, 0, w, 100, R, t) + bridges(w / 2)
    if ch == "P":
        w = 60
        return w, [("+", rect(0, 0, t, 100))] + _ring(0, 0, w, 60, R * .9, t) + \
            [("+", rect(0, 0, R, t)), ("+", rect(0, 60 - t, R, 60)), ("-", rect(t, 0, t + g, 60))]
    if ch == "Q":
        w = 66
        return w, _ring(0, 0, w, 100, R, t) + bridges(w / 2, bottom=False) + [seg(36, 62, 64, 104)]
    if ch == "R":
        w = 62
        return w, [("+", rect(0, 0, t, 100))] + _ring(0, 0, 60, 60, R * .9, t) + \
            [("+", rect(0, 0, R, t)), ("+", rect(0, 60 - t, R, 60)), seg(30, 52, w - t * .3, 108), split()]
    if ch == "S":
        w = 60
        return w, [("+", ("rrect", (0, 0, w, mid + t / 2, R))),
                   ("-", ("rrect", (t, t, w + 30, mid - t / 2, max(R - t, 0)))),
                   ("+", ("rrect", (0, mid - t / 2, w, 100, R))),
                   ("-", ("rrect", (-30, mid + t / 2, w - t, 100 - t, max(R - t, 0))))]
    if ch == "T":
        w = 60
        return w, [("+", rect(0, 0, w, t)), ("+", rect(w / 2 - t / 2, 0, w / 2 + t / 2, 100))]
    if ch == "U":
        w = 62
        return w, [("+", ("rrect", (0, -40, w, 100, R))), ("-", ("rrect", (t, -60, w - t, 100 - t, max(R - t, 0))))]
    if ch == "V":
        w = 66
        return w, [seg(t * .55, -8, w / 2, 108), seg(w - t * .55, -8, w / 2, 108)]
    if ch == "W":
        w = 92
        return w, [seg(t * .5, -6, 24, 106), seg(w / 2, -6, 24, 106), seg(w / 2, -6, w - 24, 106),
                   seg(w - t * .5, -6, w - 24, 106)]
    if ch == "X":
        w = 64
        return w, [seg(t * .4, -6, w - t * .4, 106), seg(w - t * .4, -6, t * .4, 106)]
    if ch == "Y":
        w = 64
        return w, [seg(t * .4, -6, w / 2, 56), seg(w - t * .4, -6, w / 2, 56), ("+", rect(w / 2 - t / 2, 50, w / 2 + t / 2, 100))]
    if ch == "Z":
        w = 58
        return w, [("+", rect(0, 0, w, t)), ("+", rect(0, 100 - t, w, 100)),
                   ("+", ("poly", [(w - t * 1.25, t), (w, t), (t * 1.25, 100 - t), (0, 100 - t)]))]
    if ch == "1":
        w = 22 + t
        return w, [("+", rect(22, 0, 22 + t, 100)), seg(4, 32, 28, 6)]
    if ch == "2":
        w = 58
        return w, [("+", ("rrect", (0, 0, w, 130, R))), ("-", rect(-30, t, w - t, 200)), ("-", rect(w - t - 1, 48, w + 1, 200)),
                   ("+", ("poly", [(w - t, 34), (w, 34), (w, 48), (t * 1.3, 100 - t), (0, 100 - t)])),
                   ("+", rect(0, 100 - t, w, 100))]
    if ch == "3":
        w = 58
        return w, [("+", ("rrect", (0, 0, w - 4, mid + t / 2, R * .9))),
                   ("-", ("rrect", (-30, t, w - 4 - t, mid - t / 2, max(R * .9 - t, 0)))),
                   ("+", ("rrect", (0, mid - t / 2, w, 100, R))),
                   ("-", ("rrect", (-30, mid + t / 2, w - t, 100 - t, max(R - t, 0)))),
                   ("-", rect(0, mid - t / 2, 16, mid + t / 2))]
    if ch == "4":
        w = 64
        return w, [("+", rect(42, 0, 42 + t, 100)), ("+", rect(0, 66, w, 66 + t)),
                   ("+", ("poly", [(42, 0), (42 + t, 0), (t * 1.2, 66), (0, 66)]))]
    if ch == "5":
        w = 58
        return w, [("+", rect(0, 0, w, t)), ("+", rect(0, 0, t, 50)), ("+", ("rrect", (0, 40, w, 100, R))),
                   ("-", ("rrect", (-30, 40 + t, w - t, 100 - t, max(R - t, 0))))]
    if ch == "6":
        w = 60
        return w, [("+", ("rrect", (0, 0, w, 100, R))), ("-", rect(t, t, w + 30, 42)),
                   ("-", ("rrect", (t, 42 + t, w - t, 100 - t, max(R - t, 0))))] + bridges(w / 2, top=False)
    if ch == "7":
        w = 58
        return w, [("+", rect(0, 0, w, t)), ("+", ("poly", [(w - t, 0), (w, 0), (w, t + 4), (t * 1.3 + 8, 100), (8, 100)]))]
    if ch == "8":
        w = 60
        return w, _ring(4, 0, w - 4, mid + t / 2, R * .9, t) + _ring(0, mid - t / 2, w, 100, R, t) + bridges(w / 2)
    if ch == "-":
        return 36, [("+", rect(0, mid - t / 2, 36, mid + t / 2))]
    if ch == "/":
        return 48, [seg(48 - t * .4, -6, t * .4, 106)]
    if ch == ".":
        return t, [("+", rect(0, 100 - t, t, 100))]
    if ch == ":":
        return t, [("+", rect(0, 24, t, 24 + t)), ("+", rect(0, 100 - t, t, 100))]
    if ch == " ":
        return 36, []
    raise ValueError(f"No stencil glyph for {ch!r}")


def _draw(d, shape, k, fill):
    kind, p = shape
    if kind == "rect":
        x0, y0, x1, y1 = (v * k for v in p)
        if x1 > x0 and y1 > y0:
            d.rectangle((x0, y0, x1 - 1, y1 - 1), fill=fill)
    elif kind == "rrect":
        x0, y0, x1, y1, r = (v * k for v in p)
        if x1 > x0 and y1 > y0:
            r = min(r, (x1 - x0) / 2, (y1 - y0) / 2)
            if r >= 1:
                d.rounded_rectangle((x0, y0, x1 - 1, y1 - 1), r, fill=fill)
            else:
                d.rectangle((x0, y0, x1 - 1, y1 - 1), fill=fill)
    elif kind == "poly":
        d.polygon([(x * k, y * k) for x, y in p], fill=fill)


def _render_glyph(ch, k, t, g):
    w, ops = _glyph("6" if ch == "9" else ch, t, g)
    canvas = Image.new("L", (max(1, math.ceil(w * k)), math.ceil(CAP * k)), 0)
    d = ImageDraw.Draw(canvas)
    for op, shape in ops:
        _draw(d, shape, k, 255 if op == "+" else 0)
    if ch == "9":
        canvas = canvas.rotate(180)
    return w, canvas


def advance(text, weight=18, tracking=None):
    """Width of a line in cap units (cap height 100)."""
    tracking = weight * .8 if tracking is None else tracking
    total = 0.0
    for i, ch in enumerate(text):
        total += (_glyph("6" if ch == "9" else ch, weight, max(3, weight * .33))[0])
        if i < len(text) - 1:
            total += tracking
    return total


def text_mask(text, cap_px, weight=18, tracking=None):
    """An L mask of one line of stencil text, exactly cap_px tall and anti-aliased by supersampling."""
    tracking = weight * .8 if tracking is None else tracking
    gap = max(3, weight * .33)
    k = cap_px * SUPERSAMPLE / CAP
    width = math.ceil(advance(text, weight, tracking) * k) + 2
    line = Image.new("L", (max(1, width), math.ceil(CAP * k)), 0)
    x = 0.0
    for ch in text:
        w, glyph = _render_glyph(ch, k, weight, gap)
        layer = Image.new("L", line.size, 0)
        layer.paste(glyph, (round(x * k), 0))
        line = ImageChops.lighter(line, layer)
        x += w + tracking
    size = (max(1, round(line.width / SUPERSAMPLE)), max(1, round(cap_px)))
    return line.resize(size, Image.LANCZOS)


def place(mask, text, xy, cap_px, weight=18, tracking=None, anchor="lt", max_width=None):
    """Paints stencil text into an L mask. anchor is horizontal l/m/r then vertical t/m/b. Returns the text box.

    With max_width the tracking is widened (never the glyphs) so the line fills that width, like a cut sheet.
    """
    if max_width is not None:
        tracking = weight * .8 if tracking is None else tracking
        spare = max_width / (cap_px / CAP) - advance(text, weight, tracking)
        if len(text) > 1:
            tracking += spare / (len(text) - 1)
    line = text_mask(text, cap_px, weight, tracking)
    x, y = xy
    x -= {"l": 0, "m": line.width / 2, "r": line.width}[anchor[0]]
    y -= {"t": 0, "m": line.height / 2, "b": line.height}[anchor[1]]
    x, y = round(x), round(y)
    layer = Image.new("L", mask.size, 0)
    layer.paste(line, (x, y))
    mask.paste(ImageChops.lighter(mask, layer))
    return x, y, x + line.width, y + line.height


def fit_cap(text, width_px, weight=18, tracking=None):
    """Largest cap height at which a line fits width_px."""
    return width_px / advance(text, weight, tracking) * CAP
