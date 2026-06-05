# CI / Release automation

Two GitHub Actions workflows automate what was assembled by hand on 2026-06-04.

## The hard problem they solve
A CI runner has no Valheim. The build needs `assembly_valheim.dll` + the Unity
and BCL assemblies (`mscorlib`, `System`, `netstandard`, …). Those all ship in
the **free dedicated server** (Steam app **896660**, anonymous login — no game
ownership, nothing copyrighted committed to the repo). Both workflows pull it
with SteamCMD and point `VALHEIM_MANAGED` at its `Managed` folder. BepInEx core
comes from `scripts/fetch-sdk.sh` (pinned Thunderstore pack).

## `ci.yml` — every push / PR that touches the mod
1. SteamCMD-fetch server assemblies (cached weekly).
2. `dotnet build -c Release`.
3. **Reflection-drift guard:** loads the real `assembly_valheim.dll` via
   `MetadataLoadContext` and asserts the private/named vanilla members the
   client-refresh layer depends on still exist (`Player.UpdateKnownRecipesList`,
   `PieceTable.m_availablePieces`, `Piece.PieceCategory.Max`, …). If a game
   update renames one, the patch would silently no-op ("logs green ≠ playable") —
   this fails the build instead of shipping a dud.
4. Packs the modpack and uploads it as a build artifact for inspection.

## `release.yml` — on a pushed tag (`v*`)
1. Build (same SteamCMD reference setup).
2. `scripts/pack-modpack.sh` → **deterministic** zip + `.sha256`.
3. Create/refresh the GitHub Release **for that tag** and upload the assets.
   Each tag owns its own immutable asset; a previously-shipped asset is never
   mutated.
4. Open a PR against `main` bumping `installer.ps1`'s `ExpectedSha256` to the new
   hash — **your gate.**

### Why this ordering is safe (the 2026-06-04 lesson)
That night a new zip was uploaded to an existing release *before* the installer's
pinned hash was updated, so for a window the public one-liner failed with
"checksum mismatch." This pipeline is **publish-then-PR**: the new asset is
published, but the live installer keeps pinning the *previous* hash and serving
the *previous* asset until you merge the bump PR. There is never a broken window.
Deterministic packing means re-running a release reproduces the identical hash.

## Cutting a release
```bash
# bump <Version> in src/SBPR.Trailborne/SBPR.Trailborne.csproj first if needed
git tag v0.1.0-playtest
git push origin v0.1.0-playtest
# → release.yml builds, publishes the asset, opens the installer-pin PR.
# Review + merge that PR to point the public installer at the new build.
```

## Pack locally (same artifact CI builds)
```bash
dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release
scripts/pack-modpack.sh --dll src/SBPR.Trailborne/bin/Release/SBPR.Trailborne.dll --out dist
```
