#!/usr/bin/env /usr/bin/python3
"""Generate the v0.1 placeholder threat-fallback glyph for the Sunstone Lens trophy ring
(card t_b8a19487).

One 256x256 RGBA glyph used on the detection ring for hostiles that have NO trophy
(summoned minions, some boss adds) — so a trophy-less threat still appears at the right
bearing/size wearing a generic danger mark instead of vanishing.

  • threat_fallback_v0.1.png — a pale danger glyph (skull-ish wedge + alert outline) on a
    transparent field, tinted at runtime by the aggro-state colour (yellow/orange/red), so
    it must be near-WHITE here for the tint to read.

Deliberate PLACEHOLDER (the icon-asset doctrine): the mechanic (a trophy-less hostile is
still shown, at the right place, in the right threat colour) is the acceptance target;
polished art is a v0.x follow-up. The overlay also has a code-generated fallback if this
PNG is missing, so a missing file degrades to "still visible," never a crash. Regenerate:
    python3 scripts/gen_threat_fallback_icon_v01.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "icons", "items")

# Near-white so the runtime aggro tint (Image.color multiply) controls the hue.
INK = (235, 235, 235, 255)


def make_threat_glyph():
    """A simple, readable danger mark: a rounded triangle 'alert' frame with a skull-ish
    eye/void cluster — recognisable as 'threat' at ~28-64px, neutral (near-white) so the
    aggro tint reads."""
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = cy = SIZE / 2

    # Outer alert triangle (rounded), thick stroke.
    margin = 26
    apex = (cx, margin)
    bl = (margin + 6, SIZE - margin)
    br = (SIZE - margin - 6, SIZE - margin)
    d.line([apex, bl, br, apex], fill=INK, width=14, joint="curve")

    # Exclamation void inside (the classic danger mark), leaving room for a skull read.
    bar_w = 22
    bar_top = cy - 44
    bar_bot = cy + 28
    d.rounded_rectangle([cx - bar_w / 2, bar_top, cx + bar_w / 2, bar_bot],
                        radius=bar_w / 2, fill=INK)
    # Dot.
    r = 15
    d.ellipse([cx - r, cy + 46 - r, cx + r, cy + 46 + r], fill=INK)

    # Soft outline glow so it reads over bright/dark backdrops alike.
    glow = img.filter(ImageFilter.GaussianBlur(3))
    out = Image.alpha_composite(glow, img)
    return out


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    glyph = make_threat_glyph()
    path = os.path.join(OUT_DIR, "threat_fallback_v0.1.png")
    glyph.save(path)
    print(f"wrote {os.path.normpath(path)} ({glyph.size[0]}x{glyph.size[1]} RGBA)")


if __name__ == "__main__":
    main()
