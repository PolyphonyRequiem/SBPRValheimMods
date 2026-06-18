#!/usr/bin/env /usr/bin/python3
"""Generate the v0.1 placeholder icon for the v3 Iron Compass (card t_ee61472f).

One 512x512 RGBA icon, in the same warm-vignette register as the other v0.1
placeholders (cartographers_kit_v0.1.png, sunstone_lens_v0.1.png etc.):
  • iron_compass_v0.1.png — a dark iron compass ring around a parchment dial with
    N/E/S/W ticks and a red-tipped needle pointing north.

This is a deliberate PLACEHOLDER (the icon-asset doctrine): the mechanic (the
camera-yaw HUD needle) is the acceptance target, polished art is a v0.x follow-up.
It exists so the item ships a recognizable icon instead of the magenta
ConstructItemShell fallback (and so SpecCheck's C1 boot check passes). The
held-trinket WORLD mesh is separately deferred to v0.2+ (requirements.md:696) —
this icon is unrelated to that deferral. Regenerate at the same filename with:
    python3 scripts/gen_iron_compass_icon_v01.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageOps, ImageFont

SIZE = 512
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "icons", "items")


def warm_backdrop(size, center=(70, 48, 30), edge=(16, 10, 6)):
    """Radial warm vignette, matching the existing v0.1 placeholder backdrops."""
    bg = Image.new("RGB", (size, size), center)
    mask = Image.new("L", (size, size), 0)
    px = mask.load()
    cx = cy = size / 2
    mr = math.hypot(cx, cy)
    for y in range(size):
        for x in range(size):
            r = math.hypot(x - cx, y - cy) / mr
            px[x, y] = min(255, int(255 * (r ** 1.5)))
    overlay = Image.new("RGB", (size, size), edge)
    return Image.composite(overlay, bg, mask).convert("RGBA")


def radial_glow(size, color, radius_frac=0.42, falloff=2.2):
    """A soft additive glow disc centered in the frame."""
    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = glow.load()
    cx = cy = size / 2
    rmax = size * radius_frac
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - cx, y - cy)
            if d <= rmax:
                a = (1.0 - (d / rmax)) ** falloff
                px[x, y] = (color[0], color[1], color[2], int(255 * a))
    return glow.filter(ImageFilter.GaussianBlur(size // 40))


def _load_font(size):
    for path in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    ):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def draw_compass(img):
    d = ImageDraw.Draw(img)
    cx = cy = SIZE / 2

    # ── Dark iron case (outer ring) ───────────────────────────────────────────
    ring_outer = SIZE * 0.42
    ring_inner = SIZE * 0.33
    d.ellipse([cx - ring_outer, cy - ring_outer, cx + ring_outer, cy + ring_outer],
              fill=(58, 52, 47, 255), outline=(150, 132, 110, 255), width=7)
    # Inner case rim (a second darker bevel ring).
    d.ellipse([cx - ring_inner * 1.07, cy - ring_inner * 1.07,
               cx + ring_inner * 1.07, cy + ring_inner * 1.07],
              outline=(28, 24, 20, 255), width=6)

    # ── Parchment dial face ───────────────────────────────────────────────────
    d.ellipse([cx - ring_inner, cy - ring_inner, cx + ring_inner, cy + ring_inner],
              fill=(214, 198, 160, 255), outline=(120, 104, 80, 255), width=3)

    # ── Cardinal + intercardinal tick marks ───────────────────────────────────
    for i in range(8):
        ang = math.radians(i * 45)
        long_tick = (i % 2 == 0)  # N/E/S/W get the long ticks
        r0 = ring_inner * (0.74 if long_tick else 0.82)
        r1 = ring_inner * 0.96
        x0, y0 = cx + r0 * math.sin(ang), cy - r0 * math.cos(ang)
        x1, y1 = cx + r1 * math.sin(ang), cy - r1 * math.cos(ang)
        d.line([(x0, y0), (x1, y1)], fill=(74, 60, 40, 255),
               width=7 if long_tick else 4)

    # ── N / E / S / W labels ──────────────────────────────────────────────────
    font = _load_font(int(SIZE * 0.072))
    lr = ring_inner * 0.58
    for label, ang in (("N", 0), ("E", 90), ("S", 180), ("W", 270)):
        a = math.radians(ang)
        lx, ly = cx + lr * math.sin(a), cy - lr * math.cos(a)
        col = (150, 40, 32, 255) if label == "N" else (70, 56, 38, 255)
        bbox = d.textbbox((0, 0), label, font=font)
        tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        d.text((lx - tw / 2 - bbox[0], ly - th / 2 - bbox[1]), label, font=font, fill=col)

    # ── The needle (red north tip + pale south tail), drawn on a rotated layer ─
    needle = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    nd = ImageDraw.Draw(needle)
    half_w = SIZE * 0.045
    tip = ring_inner * 0.66
    # North (red) — a tapered triangle pointing up.
    nd.polygon([(cx, cy - tip), (cx - half_w, cy), (cx + half_w, cy)],
               fill=(196, 54, 42, 255), outline=(120, 28, 22, 255))
    # South (pale steel) — tapered triangle pointing down.
    nd.polygon([(cx, cy + tip), (cx - half_w, cy), (cx + half_w, cy)],
               fill=(196, 200, 205, 255), outline=(120, 122, 126, 255))
    img = Image.alpha_composite(img, needle)

    # ── Center hub pin ────────────────────────────────────────────────────────
    d2 = ImageDraw.Draw(img)
    hub = SIZE * 0.035
    d2.ellipse([cx - hub, cy - hub, cx + hub, cy + hub],
               fill=(40, 36, 32, 255), outline=(150, 132, 110, 255), width=3)
    return img


def frame(img):
    """Thin dark border, matching the other placeholders."""
    rgb = img.convert("RGB")
    rgb = ImageOps.expand(rgb, border=12, fill=(18, 11, 6))
    return rgb.resize((SIZE, SIZE)).convert("RGBA")


def make_compass():
    img = warm_backdrop(SIZE)
    img = Image.alpha_composite(img, radial_glow(SIZE, (120, 110, 90), 0.44, 2.4))
    img = draw_compass(img)
    return frame(img)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.normpath(os.path.join(OUT_DIR, "iron_compass_v0.1.png"))
    im = make_compass()
    im.save(path)
    print(f"wrote {path} ({im.size[0]}x{im.size[1]} {im.mode})")


if __name__ == "__main__":
    main()
