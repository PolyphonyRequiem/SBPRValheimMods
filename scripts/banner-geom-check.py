#!/usr/bin/env python3
"""
banner-geom-check.py — reproduces the geometry claim in
docs/investigations/2026-06-17-cairn-banner-issue10-force-vs-constraint.md

It shows that the cairn banner's rock-drape sphere colliders (default ON) are
larger than the tail's horizontal standoff from the pile, so the lower tail rest-
intersects them and is physically caged — the leading explanation for why raising
SBPR_BannerWindMult to 20x still only leaned the banner ~10-15deg (a free tail at
20x should lean ~64deg off vertical).

All constants are lifted from src/SBPR.Trailborne/Features/Cairns/CairnTag.cs at
the lines noted; this is a read-only reasoning aid, no game code is touched.

Run: python3 scripts/banner-geom-check.py
"""
import math

# Pile geometry — CairnTag.cs:52-58
PileBaseRadiusT1   = 0.42
PileBaseRadiusStep = 0.06
PileHeightT1       = 0.34
PileHeightStep     = 0.12
PileBaseY          = 0.05
TopTaperFrac       = 0.78

# Banner seating — CairnTag.cs:401 (mount height), :402 (lateral offset), :400 (drop)
offsetX     = 0.30
mountHeight = 0.20
dropY       = 1.15

# What Daniel tested live (commit 076b43e) and physics constants
mult_tested = 20.0
gravity     = 9.8

def report_tier(tier):
    baseR  = PileBaseRadiusT1 + PileBaseRadiusStep * (tier - 1)
    pileH  = PileHeightT1     + PileHeightStep     * (tier - 1)
    pileTopY = PileBaseY + pileH
    mountY   = pileTopY + mountHeight        # kitbash-local mount Y
    print(f"--- tier {tier}: baseR={baseR:.2f} pileH={pileH:.2f} "
          f"pileTopY={pileTopY:.2f} mountY(kitbash)={mountY:.2f}")
    # 4 spheres down the pile axis (CairnTag.cs:594-616), expressed banner-local
    # (subtract bannerLocal = (offsetX, mountY, 0)). Tail hangs at banner-local
    # X=0, Y in [0, -dropY].
    for i in range(4):
        k      = i / 3
        pileY  = PileBaseY + pileH * k
        radius = max(0.12, baseR * (1 - TopTaperFrac * k)) + 0.04
        cx     = 0 - offsetX                 # sphere centre X, banner-local
        cy     = pileY - mountY              # sphere centre Y, banner-local
        ty     = min(max(cy, -dropY), 0.0)   # nearest tail point Y (clamped to segment)
        dist   = math.hypot(0 - cx, ty - cy) # tail-to-centre distance
        tag    = "<<< TAIL CAGED" if dist < radius else "clear"
        print(f"  sphere{i}: centre(banner-local)=({cx:.2f},{cy:.2f}) "
              f"r={radius:.2f}  nearest-tail-dist={dist:.2f}  {tag}")

for tier in (1, 3):
    report_tier(tier)

lean = math.degrees(math.atan(mult_tested / gravity))
print()
print(f"Free-tail lean at mult={mult_tested:.0f}, intensity=1: "
      f"atan({mult_tested}/{gravity}) = {lean:.0f} deg off vertical.")
print("Daniel observed ~10-15 deg at mult=20 (commit 076b43e). The ~50 deg "
      "deficit => a CONSTRAINT eats the deflection, not weak force.")
