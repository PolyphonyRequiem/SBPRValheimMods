#!/usr/bin/env /usr/bin/python3
"""Generate the v0.1 placeholder icon for the Local Map item (cartography, card
t_cb831069), filling a gap that caused a playtest bug: local_map_v0.1.png was
NEVER shipped, so the Local Map (an equipable TwoHandedWeapon) fell back to the
magenta ConstructItemShell placeholder AND — like every equipable with an opaque
icon — showed no blue "equipped" indicator (bug t_b9a111ca, Daniel 2026-06-19).

One 512x512 RGBA icon on a TRANSPARENT canvas (equipable-icon transparency rule):
  • local_map_v0.1.png — a rolled parchment map: an unfurled parchment sheet with
    a faint route line, a marked X, and a couple of curled edges. The parchment is
    the item silhouette; everything around it is transparent so the slot's equipped
    highlight shows through.

Deliberate PLACEHOLDER (icon-asset doctrine): recognizable in the slot is the bar;
polished art is a v0.x follow-up. Regenerate at the same filename with:
    python3 scripts/gen_local_map_icon_v01.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

SIZE = 512
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "icons", "items")

PARCH = (214, 196, 156, 255)      # parchment fill
PARCH_HI = (230, 214, 178, 255)   # lighter parchment highlight
PARCH_EDGE = (150, 120, 80, 255)  # burnt edge
INK = (78, 56, 34, 255)           # route/mark ink
ROUTE = (120, 70, 44, 255)        # dashed route line
MARK = (150, 40, 32, 255)         # red X destination


def _load_font(size):
    for path in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    ):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def draw_map(img):
    d = ImageDraw.Draw(img)
    cx = cy = SIZE / 2

    # ── Parchment sheet: a slightly trapezoidal unfurled page, gently rotated ──
    # Drawn on its own layer so we can rotate the whole sheet for a hand-held feel.
    sheet = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sheet)
    w, h = SIZE * 0.60, SIZE * 0.46
    x0, y0 = cx - w / 2, cy - h / 2
    x1, y1 = cx + w / 2, cy + h / 2
    # Page body with a faint perspective (top edge a touch narrower).
    inset = w * 0.06
    page = [(x0 + inset, y0), (x1 - inset, y0), (x1, y1), (x0, y1)]
    sd.polygon(page, fill=PARCH, outline=PARCH_EDGE)
    # Soft top highlight band.
    sd.polygon([(x0 + inset, y0), (x1 - inset, y0),
                (x1 - inset * 1.4, y0 + h * 0.18), (x0 + inset * 1.4, y0 + h * 0.18)],
               fill=PARCH_HI)

    # ── Route: a dashed wandering line across the parchment ──
    pts = [
        (x0 + w * 0.16, y1 - h * 0.22),
        (x0 + w * 0.36, y0 + h * 0.40),
        (x0 + w * 0.54, y1 - h * 0.34),
        (x1 - w * 0.20, y0 + h * 0.30),
    ]
    for i in range(len(pts) - 1):
        ax, ay = pts[i]
        bx, by = pts[i + 1]
        seg = max(2, int(math.hypot(bx - ax, by - ay) / 18))
        for s in range(seg):
            if s % 2 == 0:
                t0 = s / seg
                t1 = (s + 1) / seg
                sd.line([(ax + (bx - ax) * t0, ay + (by - ay) * t0),
                         (ax + (bx - ax) * t1, ay + (by - ay) * t1)],
                        fill=ROUTE, width=5)

    # ── Destination X (red) at the route end ──
    ex, ey = pts[-1]
    r = SIZE * 0.028
    sd.line([(ex - r, ey - r), (ex + r, ey + r)], fill=MARK, width=7)
    sd.line([(ex - r, ey + r), (ex + r, ey - r)], fill=MARK, width=7)

    # ── A couple of faint contour/grid ticks to read as a map ──
    for gx in range(1, 4):
        lx = x0 + w * gx / 4
        sd.line([(lx, y0 + h * 0.10), (lx, y1 - h * 0.10)], fill=(150, 130, 95, 90), width=2)

    # Rotate the whole sheet a few degrees for a held-map feel.
    sheet = sheet.rotate(-7, resample=Image.Resampling.BICUBIC, center=(cx, cy))
    img = Image.alpha_composite(img, sheet)

    # ── Two curled roll edges (left + right) so it reads as a MAP, not a sign ──
    roll = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    rd = ImageDraw.Draw(roll)
    roll_w = w * 0.10
    for side in (-1, 1):
        rcx = cx + side * (w / 2) * 0.96
        rd.rounded_rectangle([rcx - roll_w / 2, y0 - h * 0.06, rcx + roll_w / 2, y1 + h * 0.06],
                             radius=roll_w / 2, fill=(196, 176, 134, 255), outline=PARCH_EDGE, width=3)
        rd.ellipse([rcx - roll_w / 2, y0 - h * 0.06 - roll_w * 0.5,
                    rcx + roll_w / 2, y0 - h * 0.06 + roll_w * 0.5],
                   fill=(176, 154, 112, 255), outline=PARCH_EDGE)
    roll = roll.rotate(-7, resample=Image.Resampling.BICUBIC, center=(cx, cy))
    img = Image.alpha_composite(img, roll)

    return img


def make_local_map():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    img = draw_map(img)
    # Gentle drop of internal contrast so it sits like a painted icon (no cast shadow).
    return img


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.normpath(os.path.join(OUT_DIR, "local_map_v0.1.png"))
    im = make_local_map()
    im.save(path)
    print(f"wrote {path} ({im.size[0]}x{im.size[1]} {im.mode})")


if __name__ == "__main__":
    main()
