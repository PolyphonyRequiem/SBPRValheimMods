#!/usr/bin/env /usr/bin/python3
"""Knock out the opaque vignette background from the Cartographer's Kit and
Trailblazer's Spade v0.1 placeholder icons, making them transparent so the
inventory slot's blue "equipped" highlight shows through (bug t_b9a111ca,
Daniel playtest 2026-06-19).

WHY a knockout script instead of a generator patch (as for the compass/sunstone):
these two icons were composited by scripts/iconify_v01_placeholder.py from a FLUX
source render that was NEVER committed (only the flattened v0.1 PNG is in git), so
there is no procedural source to re-emit transparent. This script is the
reproducible, in-repo capture of the fix (AGENTS.md RULE 1): it floods the warm
vignette background inward from the border (the iconify backdrop is a smooth dark
radial vignette with a flat-ish border), leaving the item silhouette opaque.

It is IDEMPOTENT: an already-transparent icon is left unchanged (the border ring is
already alpha 0, so the flood removes nothing). Re-running after a future
iconify-based regen re-applies the knockout.

Run:  python3 scripts/knockout_equipable_icon_bg.py
Out:  rewrites assets/icons/items/{cartographers_kit,trailblazers_spade}_v0.1.png in place (RGBA)
"""
import os
from collections import deque
from PIL import Image, ImageFilter
import numpy as np

OUT_DIR = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "assets", "icons", "items"))

# Per-icon flood tolerance (colour distance from the seeded border colour). The
# iconify backdrop is a near-flat dark frame (border std < 1 measured), so a modest
# tolerance cleanly separates the vignette from the item without eating edges.
TARGETS = {
    "cartographers_kit_v0.1.png": 38,
    "trailblazers_spade_v0.1.png": 34,
}


def knockout(path, tol):
    """Border-seeded flood fill: make background-coloured pixels reachable from the
    frame transparent. Returns (rgba_image, removed_fraction)."""
    src = Image.open(path).convert("RGBA")
    arr = np.asarray(src).astype(np.int16)
    h, w, _ = arr.shape
    rgb = arr[:, :, :3]

    # Seed colour = mean of the four corners (the darkest vignette frame tone).
    seed = np.mean([rgb[0, 0], rgb[0, w - 1], rgb[h - 1, 0], rgb[h - 1, w - 1]], axis=0)
    dist = np.sqrt(((rgb - seed) ** 2).sum(2))
    bg = dist < tol

    # Flood from the border so an interior region that happens to match the seed
    # colour is NOT punched out — only background connected to the frame is removed.
    visited = np.zeros((h, w), bool)
    dq = deque()
    for x in range(w):
        for yy in (0, h - 1):
            if bg[yy, x] and not visited[yy, x]:
                visited[yy, x] = True
                dq.append((yy, x))
    for y in range(h):
        for xx in (0, w - 1):
            if bg[y, xx] and not visited[y, xx]:
                visited[y, xx] = True
                dq.append((y, xx))
    while dq:
        y, x = dq.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not visited[ny, nx] and bg[ny, nx]:
                visited[ny, nx] = True
                dq.append((ny, nx))

    out = np.asarray(src).copy()
    out[visited, 3] = 0
    img = Image.fromarray(out)

    # Soften the 1px alpha cliff so the silhouette doesn't alias against the slot.
    a = img.split()[-1].filter(ImageFilter.GaussianBlur(0.6))
    img.putalpha(a)
    return img, float(visited.sum()) / (h * w)


def main():
    for name, tol in TARGETS.items():
        path = os.path.join(OUT_DIR, name)
        if not os.path.exists(path):
            print(f"SKIP (missing): {path}")
            continue
        img, removed = knockout(path, tol)
        img.save(path)
        print(f"wrote {path} (removed {removed * 100:.0f}% as transparent)")


if __name__ == "__main__":
    main()
