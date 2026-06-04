#!/usr/bin/env /usr/bin/python3
"""Iconify v3: brute-force hard center crop kills the cast shadow,
paste cropped tile onto a dark warm vignetted backdrop, frame it."""
import sys
from PIL import Image, ImageFilter, ImageOps

def make_backdrop(size, center=(72, 50, 32), edge=(20, 12, 7)):
    w = h = size
    bg = Image.new("RGB", (w, h), center)
    mask = Image.new("L", (w, h), 0)
    px = mask.load()
    cx, cy = w/2, h/2
    mr = (cx*cx + cy*cy) ** 0.5
    for y in range(h):
        for x in range(w):
            r = ((x-cx)**2 + (y-cy)**2) ** 0.5 / mr
            px[x, y] = min(255, int(255 * (r ** 1.5)))
    overlay = Image.new("RGB", (w, h), edge)
    return Image.composite(overlay, bg, mask)

def feather_mask(size, inset=40):
    """Soft-edged rectangular mask: opaque interior, feathered border."""
    w = h = size
    m = Image.new("L", (w, h), 0)
    inner = Image.new("L", (w-inset*2, h-inset*2), 255)
    m.paste(inner, (inset, inset))
    return m.filter(ImageFilter.GaussianBlur(inset // 2))

def frame(img):
    img = ImageOps.expand(img, border=10, fill=(18, 11, 6))
    img = ImageOps.expand(img, border=2, fill=(110, 72, 32))
    img = ImageOps.expand(img, border=4, fill=(18, 11, 6))
    return img

def iconify(in_path, out_path,
            crop_box=(0.18, 0.12, 0.82, 0.78),  # x0,y0,x1,y1 as fractions
            size=768):
    src = Image.open(in_path).convert("RGB")
    w, h = src.size
    x0, y0, x1, y1 = (int(crop_box[0]*w), int(crop_box[1]*h),
                      int(crop_box[2]*w), int(crop_box[3]*h))
    crop = src.crop((x0, y0, x1, y1))
    # Square-pad with the crop's own corner color (parchment edge tone)
    cw, ch = crop.size
    s = max(cw, ch)
    edge_color = crop.getpixel((cw//2, 5))  # top center is usually parchment
    sq = Image.new("RGB", (s, s), edge_color)
    sq.paste(crop, ((s-cw)//2, (s-ch)//2))
    # Light painterly blur to flatten micro-detail
    sq = sq.filter(ImageFilter.SMOOTH)
    # Resize crop to fit inside backdrop with ~10% margin
    work = int(size * 0.84)
    sq = sq.resize((work, work), Image.LANCZOS)
    # Composite onto backdrop with feathered edges so the parchment fades into the vignette
    bd = make_backdrop(size)
    off = (size - work) // 2
    mask = feather_mask(work, inset=30)
    bd.paste(sq, (off, off), mask)
    out = frame(bd).resize((512, 512), Image.LANCZOS)
    out.save(out_path, "PNG")
    print(f"wrote {out_path}")

if __name__ == "__main__":
    iconify(sys.argv[1], sys.argv[2])
