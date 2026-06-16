#!/usr/bin/env /usr/bin/python3
"""Generate the v0.1 PLACEHOLDER icon for the Portal Seed item.

Per docs/v2/planning/ancient-portal-impl-spec.md §2.2 / §0 (C1), the Portal Seed is
an additively-constructed item (Assets.ConstructItemShell) and so MUST ship a real
icon PNG — an empty m_icons crashes the crafting UI, and a missing PNG leaves the
shared magenta fallback that SpecCheck's C1 boot check screams about. This draws a
simple, high-contrast seed/acorn glyph with a faint portal-glow ring behind it on the
same warm-parchment disc the marker icons use, so it reads both in the wood-toned
crafting menu and at small sizes. Art can be regenerated later without a code change at
the SAME filename (the `_v0.1` suffix matches the convention: ink_red_v0.1.png,
cairn_marker_v0.1.png, marker_portal_v0.1.png).

Run:  python3 scripts/gen_portal_seed_icon_v01.py
Out:  assets/icons/items/portal_seed_v0.1.png  (256x256, RGBA)
"""
import os
from PIL import Image, ImageDraw

SIZE = 256
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "icons", "items")

# Warm parchment disc with a darker rim — matches the marker-icon family so the
# Portal Seed sits in the same visual language in the Explorer's Bench menu.
DISC_FILL = (222, 200, 158, 255)
DISC_RIM = (92, 64, 36, 255)
GLYPH = (40, 28, 18, 255)         # dark ink outline
SEED_FILL = (120, 86, 50, 255)    # acorn/seed body — warm brown
SEED_CAP = (74, 52, 30, 255)      # darker cap
PORTAL_GLOW = (96, 150, 150, 150) # faint teal portal ring behind the seed (semi-transparent)


def base_disc():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = 14
    d.ellipse([pad, pad, SIZE - pad, SIZE - pad], fill=DISC_FILL, outline=DISC_RIM, width=10)
    return img, d


def portal_seed():
    """A seed/acorn over a faint portal-glow ring (the seed that becomes a portal)."""
    img, d = base_disc()
    cx, cy = 128, 132

    # Faint portal-glow ring BEHIND the seed — hints that this seed becomes a portal.
    glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    r_out = 70
    gd.ellipse([cx - r_out, cy - r_out, cx + r_out, cy + r_out], outline=PORTAL_GLOW, width=12)
    img.alpha_composite(glow)
    d = ImageDraw.Draw(img)

    # Acorn body — a rounded teardrop (ellipse + pointed base).
    bw, bh = 56, 72          # body half-width / half-height
    top = cy - 28
    # Lower bulb (ellipse).
    d.ellipse([cx - bw, top, cx + bw, top + bh + 36], fill=SEED_FILL, outline=GLYPH, width=8)
    # Pointed base (small triangle so it reads as a seed tip, not a ball).
    d.polygon([(cx - 18, top + bh + 30), (cx + 18, top + bh + 30), (cx, top + bh + 64)],
              fill=SEED_FILL, outline=GLYPH)

    # Acorn cap — a flatter dome with a little stem.
    cap_h = 34
    d.pieslice([cx - bw - 4, top - cap_h, cx + bw + 4, top + cap_h],
               start=180, end=360, fill=SEED_CAP, outline=GLYPH, width=8)
    d.line([cx, top - cap_h + 6, cx, top - cap_h - 14], fill=GLYPH, width=12)  # stem

    save(img, "portal_seed_v0.1.png")


def save(img, name):
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.normpath(os.path.join(OUT_DIR, name))
    img.save(path)
    print("wrote", path)


if __name__ == "__main__":
    portal_seed()
    print("Done — Portal Seed placeholder icon generated.")
