---
title: "ADR-0004: Releases are deterministic and publish-then-PR"
status: accepted
---

# ADR-0004: Releases are deterministic and publish-then-PR

- **Status:** accepted
- **Date:** 2026-06-04
- **Deciders:** Daniel + Starbright

## Context

The client installer (`installer.ps1`) downloads a modpack zip from a GitHub
Release and verifies it against a **pinned SHA256**. On 2026-06-04 a release was
done by hand: the new zip was uploaded to the release *before* the installer's
pinned hash was updated, so for a window the public one-liner failed with
"checksum mismatch." Two ordering hazards surfaced: (1) mutating a published
asset, and (2) updating the artifact and its pin in the wrong order.

## Decision

Releases are automated (`.github/workflows/release.yml`) with two properties:

1. **Deterministic packaging** — `scripts/pack-modpack.sh` normalizes timestamps
   so identical content always produces an identical SHA256. Each tag owns its own
   immutable asset; we never mutate a previously-shipped asset.
2. **Publish-then-PR** — a tagged build publishes the new asset, then opens a PR
   bumping the installer's pinned SHA. Until that PR merges, the live installer
   keeps pinning the *previous* hash and serving the *previous* (still-published)
   asset. There is therefore never a moment when the public installer is broken.

CI additionally runs a **reflection drift-guard**: it asserts the private/named
vanilla members our Harmony patches hook (e.g. `Player.UpdateKnownRecipesList`,
`PieceTable.m_availablePieces`, `Piece.PieceCategory.Max`) still resolve against
the real `assembly_valheim.dll`. A future game update that renames one fails CI
instead of silently shipping a no-op patch ("logs green ≠ playable").

## Consequences

- Re-running a release reproduces the identical hash (safe to retry).
- The 2026-06-04 broken-window failure mode is structurally impossible.
- **Do not hand-mutate release assets or reorder to "pin-then-publish."** The
  ordering is load-bearing.

## Alternatives considered

- **Mutate the existing release in place** (what was done by hand): creates the
  broken window and a non-reproducible asset. Rejected.
