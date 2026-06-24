#!/usr/bin/env /usr/bin/python3
"""Generate the v0.1 HELD-MESH texture for the Local Map item (cartography, bug
ticket-localmap-hoe-model, 2026-06-24).

Daniel on the v0.2.36 build: the procedural folded-leather held mesh still read
wrong and the player still posed as if holding a hoe. New direction:
  • mesh  -> a flat rectangle ~0.5 m wide x 0.3 m tall (handled in Assets.cs)
  • look  -> a map PAINTED ON A DEER HIDE, with Valheim-level pixelation
  • stance-> the build-Hammer hold stance while equipped (handled in LocalMap.cs)

This script paints the ALBEDO that gets sampled as the held sheet's _MainTex
(instanced over the vanilla leather material so Valheim's lit shader + the leather
normal grain still apply). It is NOT an inventory icon -> it lives in
assets/textures/ (NOT assets/icons/items/, which the equipable-icon transparency
test scans) and ships OPAQUE (a held world surface, not a UI sprite).

Valheim-level pixelation: authored at a low native resolution (160x96, exactly the
5:3 mesh aspect) and loaded Point-filtered in-game, so texels stay chunky on the
0.5 m sheet. The paint is deliberately rough/dithered, not vector-clean.

Colorblind-safe (Daniel is red-green colorblind; design Pillar 2 = value over hue):
the map ink reads as DARK charcoal/umber on a LIGHT tan hide — separated by
BRIGHTNESS, never by a red-vs-green hue cue. No red X on green land.

Regenerate at the same filename with:
    python3 scripts/gen_local_map_held_texture_v01.py
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

# Native authoring resolution = the mesh aspect (0.5 m : 0.3 m = 5:3). Small on
# purpose so in-game Point filtering yields chunky, Valheim-grade texels.
W, H = 160, 96
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "assets", "textures")

random.seed(0xDEE2)  # deterministic output (reproducible bytes for the repo)

# ── Earthy palette — separated by VALUE, not hue (colorblind-safe) ──────────────
HIDE_MID   = (196, 165, 119)   # tanned deer-hide base
HIDE_LIGHT = (214, 187, 146)   # sun-bleached high spots
HIDE_DARK  = (165, 132, 90)    # worn / shadowed hide
HIDE_EDGE  = (120, 92, 58)     # darkened, oiled hide rim
INK        = (54, 40, 28)      # charcoal map ink (darkest — primary marks)
INK_SOFT   = (92, 70, 48)      # faded ink (contours, hatching)
OCHRE      = (150, 110, 60)    # ochre paint accent (mid value, NOT a red cue)
PAINT_PALE = (224, 205, 170)   # pale painted "parchment" patch the map sits on


def _clamp(v):
    return max(0, min(255, int(v)))


def _jitter(c, amt):
    return tuple(_clamp(ch + random.randint(-amt, amt)) for ch in c)


def hide_base(img):
    """Fill with a mottled tanned-hide albedo: blotchy value variation + grain."""
    px = img.load()
    # Low-frequency mottling via a few soft radial blobs.
    blobs = []
    for _ in range(14):
        bx = random.uniform(0, W)
        by = random.uniform(0, H)
        br = random.uniform(W * 0.10, W * 0.32)
        light = random.choice([True, False])
        blobs.append((bx, by, br, light))
    for y in range(H):
        for x in range(W):
            r, g, b = HIDE_MID
            for bx, by, br, light in blobs:
                d = math.hypot(x - bx, y - by)
                if d < br:
                    k = (1.0 - d / br) * 0.5
                    tgt = HIDE_LIGHT if light else HIDE_DARK
                    r += (tgt[0] - HIDE_MID[0]) * k
                    g += (tgt[1] - HIDE_MID[1]) * k
                    b += (tgt[2] - HIDE_MID[2]) * k
            # Fine grain noise (the leather tooth).
            n = random.randint(-10, 10)
            px[x, y] = (_clamp(r + n), _clamp(g + n), _clamp(b + n), 255)


def hide_edge(img):
    """Darken/oil the rim and nick a few irregular notches so the rectangle still
    READS as a cut deer hide rather than a clean card (the mesh stays rectangular;
    the texture sells the hide)."""
    d = ImageDraw.Draw(img)
    # Oiled rim: concentric darkening near the border.
    for inset in range(0, 7):
        a = int(150 * (1 - inset / 7.0))
        col = HIDE_EDGE + (a,)
        d.rectangle([inset, inset, W - 1 - inset, H - 1 - inset], outline=col)
    # A handful of darker nicks / wear bites along the edges.
    for _ in range(26):
        side = random.randint(0, 3)
        if side == 0:      # top
            x = random.randint(2, W - 3); y = random.randint(0, 2)
        elif side == 1:    # bottom
            x = random.randint(2, W - 3); y = random.randint(H - 3, H - 1)
        elif side == 2:    # left
            x = random.randint(0, 2); y = random.randint(2, H - 3)
        else:              # right
            x = random.randint(W - 3, W - 1); y = random.randint(2, H - 3)
        rr = random.randint(1, 2)
        d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=_jitter(HIDE_EDGE, 14) + (255,))


def paint_patch(img):
    """A pale, slightly irregular painted ground the map ink sits on — like primer
    brushed onto the hide. Centered, leaving a hide margin all around."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    mx, my = int(W * 0.12), int(H * 0.16)
    pts = []
    # Wobbly rounded rectangle outline (hand-brushed edge).
    steps = 64
    for i in range(steps):
        t = i / steps * 2 * math.pi
        # base ellipse-ish, then square it up a bit toward a rectangle
        ex = (W / 2) + math.cos(t) * (W / 2 - mx)
        ey = (H / 2) + math.sin(t) * (H / 2 - my)
        # pull toward rectangle corners
        ex = (W / 2) + (ex - W / 2) * 1.06
        ey = (H / 2) + (ey - H / 2) * 1.06
        ex += random.uniform(-2.0, 2.0)
        ey += random.uniform(-1.4, 1.4)
        pts.append((ex, ey))
    d.polygon(pts, fill=PAINT_PALE + (210,))
    layer = layer.filter(ImageFilter.GaussianBlur(0.6))
    return Image.alpha_composite(img, layer)


def _rough_line(d, p0, p1, fill, width, jitter=0.8, dash=None):
    """A hand-painted line: short segments with small perpendicular jitter, optional
    dashing. dash=(on,off) in pixels."""
    ax, ay = p0
    bx, by = p1
    length = math.hypot(bx - ax, by - ay)
    n = max(2, int(length / 2))
    drawing = True
    run = 0.0
    for i in range(n):
        t0 = i / n
        t1 = (i + 1) / n
        # perpendicular offset
        nx, ny = -(by - ay), (bx - ax)
        nl = math.hypot(nx, ny) or 1
        off = random.uniform(-jitter, jitter)
        ox, oy = nx / nl * off, ny / nl * off
        sx = ax + (bx - ax) * t0 + ox
        sy = ay + (by - ay) * t0 + oy
        ex = ax + (bx - ax) * t1 + ox
        ey = ay + (by - ay) * t1 + oy
        seg = math.hypot(ex - sx, ey - sy)
        if dash:
            run += seg
            if drawing and run >= dash[0]:
                drawing = False; run = 0.0
            elif not drawing and run >= dash[1]:
                drawing = True; run = 0.0
        if drawing:
            d.line([(sx, sy), (ex, ey)], fill=fill, width=width)


def paint_map(img):
    """Paint the actual map: a coastline/landmass, water hatching, a wandering
    route with a destination X, a couple of mountain carets, and an N compass tick.
    All in dark ink + ochre, value-separated from the hide."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    # ── Landmass: a blobby coast cutting diagonally; water side gets hatching ──
    coast = [
        (W * 0.20, H * 0.86),
        (W * 0.30, H * 0.60),
        (W * 0.26, H * 0.42),
        (W * 0.40, H * 0.30),
        (W * 0.58, H * 0.26),
        (W * 0.74, H * 0.34),
        (W * 0.84, H * 0.52),
        (W * 0.80, H * 0.72),
        (W * 0.66, H * 0.84),
    ]
    cpts = [(x + random.uniform(-1.5, 1.5), y + random.uniform(-1.5, 1.5)) for x, y in coast]
    # coastline stroke
    for i in range(len(cpts)):
        _rough_line(d, cpts[i], cpts[(i + 1) % len(cpts)], INK + (255,), 2, jitter=0.9)

    # Water hatching OUTSIDE the coast (top-left corner of the patch) — short
    # parallel ticks, the classic cartographer's sea shading. Value cue only.
    for i in range(7):
        y = H * 0.20 + i * 2.4
        _rough_line(d, (W * 0.16, y), (W * 0.16 + (6 - i) * 3, y), INK_SOFT + (180,), 1, jitter=0.4)

    # ── Faint interior contour lines (ochre, low value contrast) ──
    for cy in (H * 0.50, H * 0.64):
        pts = [(W * 0.40 + math.sin(k / 3.0) * 5, cy + k) for k in range(-3, 4)]
        for i in range(len(pts) - 1):
            _rough_line(d, pts[i], pts[i + 1], OCHRE + (160,), 1, jitter=0.3)

    # ── Mountains: two ink carets on the landmass ──
    for mxp, myp, s in ((W * 0.55, H * 0.46, 5), (W * 0.63, H * 0.50, 4)):
        d.line([(mxp - s, myp + s), (mxp, myp - s)], fill=INK + (255,), width=2)
        d.line([(mxp, myp - s), (mxp + s, myp + s)], fill=INK + (255,), width=2)

    # ── Route: a dashed wandering trail across the land to a destination ──
    route = [
        (W * 0.30, H * 0.72),
        (W * 0.42, H * 0.62),
        (W * 0.50, H * 0.66),
        (W * 0.60, H * 0.58),
        (W * 0.70, H * 0.62),
    ]
    for i in range(len(route) - 1):
        _rough_line(d, route[i], route[i + 1], INK + (255,), 1, jitter=0.5, dash=(3, 2))

    # ── Destination X (dark charcoal, NOT red — colorblind-safe) at route end ──
    ex, ey = route[-1]
    r = 4
    _rough_line(d, (ex - r, ey - r), (ex + r, ey + r), INK + (255,), 2, jitter=0.5)
    _rough_line(d, (ex - r, ey + r), (ex + r, ey - r), INK + (255,), 2, jitter=0.5)

    # ── Compass tick: a small N + arrow in the upper-right of the patch ──
    nx, ny = W * 0.80, H * 0.30
    d.line([(nx, ny + 5), (nx, ny - 5)], fill=INK + (255,), width=1)          # shaft
    d.line([(nx, ny - 5), (nx - 2, ny - 2)], fill=INK + (255,), width=1)       # arrowhead
    d.line([(nx, ny - 5), (nx + 2, ny - 2)], fill=INK + (255,), width=1)
    # tiny 'N'
    d.line([(nx - 2, ny - 7), (nx - 2, ny - 11)], fill=INK + (255,), width=1)
    d.line([(nx - 2, ny - 11), (nx + 2, ny - 7)], fill=INK + (255,), width=1)
    d.line([(nx + 2, ny - 7), (nx + 2, ny - 11)], fill=INK + (255,), width=1)

    # Slight blur so the paint sits INTO the hide, then composite.
    layer = layer.filter(ImageFilter.GaussianBlur(0.4))
    return Image.alpha_composite(img, layer)


def posterize_grain(img):
    """Final pass: gently quantize the albedo toward chunky painted bands and add a
    touch of dither, so even before in-game Point filtering the texels read as
    hand-painted Valheim art rather than smooth gradients."""
    px = img.load()
    levels = 18
    step = 255 // levels
    for y in range(H):
        for x in range(W):
            r, g, b, a = px[x, y]
            dith = random.randint(-step // 3, step // 3)
            r = _clamp((round((r + dith) / step) * step))
            g = _clamp((round((g + dith) / step) * step))
            b = _clamp((round((b + dith) / step) * step))
            px[x, y] = (r, g, b, 255)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    img = Image.new("RGBA", (W, H), HIDE_MID + (255,))
    hide_base(img)
    img = paint_patch(img)
    img = paint_map(img)
    hide_edge(img)
    posterize_grain(img)
    # Opaque RGB (held world surface, not a UI sprite) — but keep RGBA container so
    # the in-game RGBA32 loader path is uniform; alpha is fully opaque everywhere.
    path = os.path.normpath(os.path.join(OUT_DIR, "local_map_held_v0.1.png"))
    img.save(path)
    # Also drop a 6x nearest-neighbor PREVIEW so the chunky in-hand look is reviewable
    # in chat without launching the game (not shipped; preview only).
    prev = img.resize((W * 6, H * 6), Image.Resampling.NEAREST)
    prev_path = os.path.normpath(os.path.join(OUT_DIR, "local_map_held_v0.1_preview6x.png"))
    prev.save(prev_path)
    print(f"wrote {path} ({img.size[0]}x{img.size[1]} {img.mode})")
    print(f"wrote {prev_path} ({prev.size[0]}x{prev.size[1]} preview, nearest-neighbor 6x)")


if __name__ == "__main__":
    main()
