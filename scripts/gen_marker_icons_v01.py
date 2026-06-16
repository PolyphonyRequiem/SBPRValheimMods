#!/usr/bin/env /usr/bin/python3
"""Generate v0.1 PLACEHOLDER marker-sign icons for the four Marker Sign types.

Per docs/v2/planning/marker-signs-impl-spec.md §1.1, the first cut ships placeholder
art (POI = magnifying glass, mining = pickaxe, shelter = tent, portal = circle per
Q5). These are deliberately simple, high-contrast type-coded glyphs on a warm
parchment disc so they read both as a build-menu icon and as a minimap pin sprite.
Art can be regenerated later without a code change at the SAME filename (the
`_v0.1` suffix matches the existing convention: ink_red_v0.1.png, cairn_marker_v0.1.png).

Run:  python3 scripts/gen_marker_icons_v01.py
Out:  assets/icons/items/marker_{poi,mining,shelter,portal}_v0.1.png  (256x256, RGBA)
"""
import os
import math
from PIL import Image, ImageDraw

SIZE = 256
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "icons", "items")

# Warm parchment disc with a darker rim — reads on the map and in the wood-toned menu.
DISC_FILL = (222, 200, 158, 255)
DISC_RIM = (92, 64, 36, 255)
GLYPH = (40, 28, 18, 255)        # dark ink glyph
GLYPH_ACCENT = (120, 86, 50, 255)


def base_disc():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = 14
    d.ellipse([pad, pad, SIZE - pad, SIZE - pad], fill=DISC_FILL, outline=DISC_RIM, width=10)
    return img, d


def save(img, name):
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.normpath(os.path.join(OUT_DIR, name))
    img.save(path)
    print("wrote", path)


def poi():
    """Magnifying glass."""
    img, d = base_disc()
    cx, cy, r = 108, 104, 46
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=GLYPH, width=14)
    # handle
    ang = math.radians(45)
    x0 = cx + r * math.cos(ang)
    y0 = cy + r * math.sin(ang)
    x1 = x0 + 60 * math.cos(ang)
    y1 = y0 + 60 * math.sin(ang)
    d.line([x0, y0, x1, y1], fill=GLYPH, width=22)
    save(img, "marker_poi_v0.1.png")


def mining():
    """Pickaxe — a curved pick HEAD arcing across the top, a HAFT hanging from its
    centre. Drawn as a downward-bowing arc (so it reads as a pick head, not a cross)
    plus a vertical handle from the arc's midpoint."""
    img, d = base_disc()
    # Curved pick head: an arc bowing downward across the upper third. pieslice/arc
    # stroked thick. Bounding box chosen so only the lower arc shows as the head.
    d.arc([56, 40, 200, 168], start=200, end=340, fill=GLYPH, width=20)
    # Sharpen the two head tips into points.
    d.polygon([(58, 96), (84, 92), (74, 116)], fill=GLYPH)      # left tip
    d.polygon([(198, 96), (172, 92), (182, 116)], fill=GLYPH)   # right tip
    # Haft: vertical handle from the arc's centre down to the lower disc.
    d.line([128, 86, 128, 198], fill=GLYPH, width=20)
    save(img, "marker_mining_v0.1.png")


def shelter():
    """Simple tent — triangle with a centre seam."""
    img, d = base_disc()
    apex = (128, 70)
    left = (72, 188)
    right = (184, 188)
    d.polygon([apex, left, right], outline=GLYPH, width=14, fill=None)
    # centre seam
    d.line([apex, (128, 188)], fill=GLYPH, width=12)
    # door flap
    d.polygon([(128, 188), (112, 188), (128, 150)], fill=GLYPH)
    save(img, "marker_shelter_v0.1.png")


def portal():
    """Q5: simple circle for now (wood-portal silhouette is the eventual target)."""
    img, d = base_disc()
    cx, cy = 128, 128
    r_out, r_in = 62, 40
    d.ellipse([cx - r_out, cy - r_out, cx + r_out, cy + r_out], outline=GLYPH, width=16)
    d.ellipse([cx - r_in, cy - r_in, cx + r_in, cy + r_in], outline=GLYPH_ACCENT, width=8)
    save(img, "marker_portal_v0.1.png")


if __name__ == "__main__":
    poi()
    mining()
    shelter()
    portal()
    print("Done — 4 placeholder marker icons generated.")
