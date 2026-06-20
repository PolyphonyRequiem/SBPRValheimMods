#!/usr/bin/env python3
"""
disc-ring-geom-check.py — reproduces the geometry claim in
docs/v2/planning/cartography-impl-spec.md §2E.5.7 (minimap DISC content-to-ring
transparent margin, card t_642687dd).

WHAT IT PROVES (pure geometry, no shader — runnable on the CI box):

  The corner minimap disc shows a TRANSPARENT annulus between the cartography
  content and the bronze bezel ring. Root cause is NOT #204's modal reframe
  (that touches DisplayedSpanMeters, the disc takes the ViewSpanMeters>0 branch)
  and NOT a fog-of-war reveal shortfall (a reveal gap renders vanilla's OPAQUE
  grey cloud; the screenshot annulus is crisp SATURATED game-world = alpha 0 =
  nothing drawn there). It is the integer-FLOORED upscale in LayoutMapRect:

      MapSurface.LayoutMapRect (MapSurface.cs:471-473):
          upscale = max(1, TargetPx // size)        # INTEGER floor
          edge    = size * upscale
          _mapRect.sizeDelta = (edge, edge)
      CircularRawImage mesh radius  meshR = edge / 2         (inscribed circle)

      EnsureBezelTexture (MapSurface.cs:1207-1211) — sizes the ring off the RAW
      TargetPx, NOT the floored edge:
          holeR      = TargetPx*0.5 - TargetPx*BezelInsetFrac
          ringOuterR = holeR + max(TargetPx*BezelRingFrac, BezelRingMinPx)

  When TargetPx/size is not an integer, the floor shrinks `edge` below TargetPx,
  so meshR < holeR and a transparent ring-gap opens (game world shows through,
  because the disc has no backdrop). At the 900 px modal the same floor is a
  near-no-op at most sizes (a 1-px slack on a 900-px rect), which is WHY #204's
  modal reads clean while the disc gaps at the SAME survey.Size — they floor
  differently against two TargetPx. The disc's `survey.Size == 33` (pixelSize 64)
  is a lucky coincidence where 200//33=6 → meshR 99.0 ≈ holeR 98.67; the spec's
  own warnings note the live auto-map m_pixelSize "may differ from 64 m/px"
  (requirements §198), and the table reads mm.m_pixelSize LIVE
  (SurveyorTableTag.cs:359) — so size is NOT guaranteed 33, and at most other
  sizes the disc gaps 5-32 px.

THE FIX (§2E.5.7, LOCKED approach A — "grow content to the ring, size-independent"):
  Size the cartography rect to the FULL TargetPx square (decouple the on-screen
  mesh extent from the integer fog-grid upscale), so meshR = TargetPx/2 for EVERY
  survey size on BOTH surfaces. The fog texture still upscales by the integer
  factor as a TEXTURE (uvRect sampling is unaffected — CircularRawImage samples
  uvRect per-vertex regardless of rect px size); only the SILHOUETTE grows to the
  ring. meshR = TargetPx/2 = holeR + insetPx sits just OUTSIDE holeR (covered by
  the ring's inner edge) and strictly INSIDE ringOuterR (no #159/issue-6 bleed).

All constants are lifted from MapSurface.cs at the lines noted. Read-only
reasoning aid + the AT-DISC-RING regression guard (exit non-zero if the geometry
relation regresses). Run: python3 scripts/disc-ring-geom-check.py
"""
import math
import sys

# ── Constants lifted verbatim from MapSurface.cs ──────────────────────────────
BezelInsetFrac = 6.0 / 900.0    # MapSurface.cs:123
BezelRingFrac  = 10.0 / 900.0   # MapSurface.cs:124
BezelRingMinPx = 4.5            # MapSurface.cs:130
MODAL_TARGET_PX = 900           # MapViewer.cs:34
DISC_TARGET_PX  = 200           # MapViewer.cs:36

# survey.Size = 2*ceil(radius/pixelSize)+1. radius locked 1000 m (LocalMapController:41).
# pixelSize = vanilla Minimap.m_pixelSize, read LIVE (SurveyorTableTag.cs:359). The decomp
# value is 64f but the auto-map's live value is not guaranteed (requirements §198), so we
# sweep the plausible band rather than assume one size.
def survey_size(radius_m, pixel_size):
    cr = math.ceil(radius_m / pixel_size)
    return 2 * cr + 1


def hole_r(target_px):
    return target_px * (0.5 - BezelInsetFrac)


def ring_outer_r(target_px):
    return hole_r(target_px) + max(target_px * BezelRingFrac, BezelRingMinPx)


# CURRENT (buggy) on-screen mesh radius — integer-floored upscale (LayoutMapRect).
def mesh_r_current(target_px, size):
    upscale = max(1, target_px // size)
    edge = size * upscale
    return edge / 2.0


# FIXED mesh radius — rect sized to the full TargetPx square (§2E.5.7 approach A).
def mesh_r_fixed(target_px, size):
    return target_px / 2.0


def sweep(target_px, label):
    hR = hole_r(target_px)
    roR = ring_outer_r(target_px)
    print(f"\n=== {label}  TargetPx={target_px}  holeR={hR:.2f}  ringOuterR={roR:.2f} ===")
    print("  size | CURRENT meshR  gap(holeR-mesh) | FIXED meshR  gap  bleed(mesh-ringOuter)")
    worst_current_gap = -1e9
    worst_fixed_gap = -1e9
    worst_fixed_bleed = -1e9
    # Sweep the odd sizes spanning pixelSize ~ 30..130 m/px at radius 1000.
    for size in range(17, 70, 2):
        mc = mesh_r_current(target_px, size)
        mf = mesh_r_fixed(target_px, size)
        gap_c = hR - mc            # >0  => transparent annulus (BUG)
        gap_f = hR - mf            # <=0 => content meets/overdraws ring (GOOD)
        bleed_f = mf - roR         # >0  => content spills past outer ring (BAD, #159)
        worst_current_gap = max(worst_current_gap, gap_c)
        worst_fixed_gap = max(worst_fixed_gap, gap_f)
        worst_fixed_bleed = max(worst_fixed_bleed, bleed_f)
        flag = "  <-- visible gap" if gap_c > 4 else ""
        print(f"  {size:4d} | {mc:11.1f}  {gap_c:+13.1f} | {mf:9.1f}  {gap_f:+5.1f}  {bleed_f:+7.1f}{flag}")
    return worst_current_gap, worst_fixed_gap, worst_fixed_bleed


def main():
    print((__doc__ or "").split("All constants")[0].rstrip())

    # Spot-check the exact runtime triad the spec assumes (pixelSize 64 -> size 33).
    s33 = survey_size(1000, 64)
    print(f"\n[runtime triad] pixelSize=64 -> survey.Size={s33}: "
          f"disc CURRENT meshR={mesh_r_current(DISC_TARGET_PX, s33):.1f} vs holeR={hole_r(DISC_TARGET_PX):.2f} "
          f"(gap {hole_r(DISC_TARGET_PX)-mesh_r_current(DISC_TARGET_PX, s33):+.1f}px — the lucky near-clean size)")
    s_alt = survey_size(1000, 56)
    print(f"[runtime triad] pixelSize=56 -> survey.Size={s_alt}: "
          f"disc CURRENT meshR={mesh_r_current(DISC_TARGET_PX, s_alt):.1f} vs holeR={hole_r(DISC_TARGET_PX):.2f} "
          f"(gap {hole_r(DISC_TARGET_PX)-mesh_r_current(DISC_TARGET_PX, s_alt):+.1f}px — a representative GAPPED size)")

    disc_cur, disc_fix_gap, disc_fix_bleed = sweep(DISC_TARGET_PX, "DISC")
    modal_cur, modal_fix_gap, modal_fix_bleed = sweep(MODAL_TARGET_PX, "MODAL (must stay clean — #204)")

    print("\n=== ASSERTIONS (AT-DISC-RING geometry guard) ===")
    ok = True

    # 1. The bug is real on the disc: some size leaves a > 4 px transparent annulus.
    if disc_cur > 4.0:
        print(f"  [bug present]  disc CURRENT worst gap = {disc_cur:+.1f}px (> 4px transparent annulus) — reproduces t_642687dd")
    else:
        print(f"  [WARN] disc CURRENT worst gap = {disc_cur:+.1f}px — bug not reproduced; check constants")

    # 2. AT-DISC-RING-1: after the fix, content meets the ring at EVERY size (meshR >= holeR, gap <= 0).
    if disc_fix_gap <= 0.5:
        print(f"  [PASS AT-DISC-RING-1]  disc FIXED worst gap = {disc_fix_gap:+.1f}px (<= 0.5 — content meets ring at all sizes)")
    else:
        print(f"  [FAIL AT-DISC-RING-1]  disc FIXED worst gap = {disc_fix_gap:+.1f}px (> 0.5 — annulus survives)")
        ok = False

    # 3. AT-DISC-RING-2: no bleed past ringOuterR (don't re-open #159 / issue-6 edge-bleed).
    if disc_fix_bleed <= 0.0:
        print(f"  [PASS AT-DISC-RING-2]  disc FIXED worst bleed = {disc_fix_bleed:+.1f}px (<= 0 — content stays inside outer ring)")
    else:
        print(f"  [FAIL AT-DISC-RING-2]  disc FIXED worst bleed = {disc_fix_bleed:+.1f}px (> 0 — content spills past ring)")
        ok = False

    # 4. Regression: the modal (#204) must not be collaterally gapped or bled by the shared-builder change.
    if modal_fix_gap <= 0.5 and modal_fix_bleed <= 0.0:
        print(f"  [PASS modal-unregressed]  modal FIXED gap={modal_fix_gap:+.1f}px bleed={modal_fix_bleed:+.1f}px (#204 preserved)")
    else:
        print(f"  [FAIL modal-unregressed]  modal FIXED gap={modal_fix_gap:+.1f}px bleed={modal_fix_bleed:+.1f}px")
        ok = False

    print("\nRESULT:", "ALL GEOMETRY ASSERTIONS PASS" if ok else "GEOMETRY ASSERTION FAILURE")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
