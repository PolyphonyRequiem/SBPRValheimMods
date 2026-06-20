#!/usr/bin/env /usr/bin/python3
"""Generate v0.1 placeholder icons for the v3 Sunstone Lens feature (card t_2fd7bc7f).

Two 512x512 RGBA icons, in the same warm-vignette register as the other v0.1
placeholders (cartographers_kit_v0.1.png etc.):
  • sunstone_v0.1.png      — a glowing amber crystal shard (the raw Sunstone material)
  • sunstone_lens_v0.1.png — that crystal set in a dark iron ring (the worn Lens)

These are deliberate PLACEHOLDERS (the icon-asset doctrine): the mechanic is the
acceptance target, polished art is a v0.x follow-up. They exist so the items ship a
recognizable icon instead of the magenta ConstructItemShell fallback (and so SpecCheck's
C1 boot check passes for the Lens). Regenerate at the same filenames with:
    python3 scripts/gen_sunstone_icons_v01.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

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


def crystal_polygon(cx, cy, w, h):
    """An elongated hexagonal crystal-shard silhouette."""
    return [
        (cx, cy - h / 2),            # top point
        (cx + w / 2, cy - h / 6),    # upper right
        (cx + w / 2.4, cy + h / 3),  # lower right
        (cx, cy + h / 2),            # bottom point
        (cx - w / 2.4, cy + h / 3),  # lower left
        (cx - w / 2, cy - h / 6),    # upper left
    ]


def draw_crystal(img, cx, cy, w, h, base=(245, 196, 96), facet=(255, 226, 150),
                 dark=(150, 96, 30)):
    """Draw a faceted amber crystal with a bright core and a couple of facet lines."""
    d = ImageDraw.Draw(img)
    poly = crystal_polygon(cx, cy, w, h)
    d.polygon(poly, fill=base + (255,), outline=dark + (255,))
    # Left facet (slightly darker) for a sense of volume.
    d.polygon([poly[0], poly[5], poly[4], (cx, cy + h / 2)], fill=dark + (90,))
    # Bright central facet.
    d.polygon([poly[0], (cx + w / 6, cy), (cx, cy + h / 3), (cx - w / 6, cy)],
              fill=facet + (235,))
    # Highlight glint near the top.
    d.line([poly[0], (cx, cy + h / 4)], fill=(255, 244, 210, 220), width=4)
    return img


def frame(img):
    """Thin dark border, matching the other placeholders."""
    from PIL import ImageOps
    rgb = img.convert("RGB")
    rgb = ImageOps.expand(rgb, border=12, fill=(18, 11, 6))
    return rgb.resize((SIZE, SIZE)).convert("RGBA")


def make_sunstone():
    # Transparent canvas so an equipped highlight could show through (equipable-icon
    # transparency rule, bug t_b9a111ca). The raw Sunstone is a Material (no equip
    # indicator) but is regenerated transparent too for set consistency. The crystal's
    # own glow gives it a lit amber halo on transparency.
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    img = Image.alpha_composite(img, radial_glow(SIZE, (255, 180, 70), 0.40))
    draw_crystal(img, SIZE / 2, SIZE / 2, SIZE * 0.34, SIZE * 0.62)
    # A second small glow on top to make the core read as lit.
    img = Image.alpha_composite(img, radial_glow(SIZE, (255, 220, 130), 0.16, 2.6))
    return img


def make_lens():
    # Transparent canvas (equipable-icon transparency rule, bug t_b9a111ca, Daniel
    # playtest 2026-06-19): the worn Lens reads as a gold crystal set in a dark iron
    # ring; everything OUTSIDE the ring AND the area inside the ring around the crystal
    # is transparent, so the slot's blue equipped highlight shows through. The iron ring
    # is the icon's own border. (frame()'s .convert("RGB") used to flatten the punched
    # hole back to opaque black — that was the visible "black disc" behind the gem.)
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = cy = SIZE / 2
    # Dark iron ring (the frame).
    ring_outer = SIZE * 0.40
    ring_inner = SIZE * 0.30
    d.ellipse([cx - ring_outer, cy - ring_outer, cx + ring_outer, cy + ring_outer],
              fill=(54, 46, 40, 255))
    d.ellipse([cx - ring_inner, cy - ring_inner, cx + ring_inner, cy + ring_inner],
              fill=(0, 0, 0, 0))
    # Iron ring rim highlight.
    d.ellipse([cx - ring_outer, cy - ring_outer, cx + ring_outer, cy + ring_outer],
              outline=(120, 104, 88, 255), width=6)
    # The sunstone set inside the ring (smaller crystal).
    draw_crystal(img, cx, cy, SIZE * 0.24, SIZE * 0.40)
    img = Image.alpha_composite(img, radial_glow(SIZE, (255, 224, 140), 0.14, 2.8))
    return img


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    out = {
        "sunstone_v0.1.png": make_sunstone(),
        "sunstone_lens_v0.1.png": make_lens(),
    }
    for name, im in out.items():
        path = os.path.normpath(os.path.join(OUT_DIR, name))
        im.save(path)
        print(f"wrote {path} ({im.size[0]}x{im.size[1]} {im.mode})")


if __name__ == "__main__":
    main()
