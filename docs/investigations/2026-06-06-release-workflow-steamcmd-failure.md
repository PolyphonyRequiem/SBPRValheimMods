---
title: "Investigation: Release workflow fails at SteamCMD; releases published by hand"
status: historical
last_updated: 2026-06-06
---

# Investigation: Release workflow fails at SteamCMD; releases published by hand

- **Date:** 2026-06-06
- **Investigator:** Starbright (with Daniel)
- **Trigger:** Preparing a v0.2.3 release; asked to understand how publishing
  actually works before pushing a tag.
- **Status:** Root cause found. Fix proposed, NOT yet applied (needs Daniel's gate
  — it's a CI workflow change).

## TL;DR

The `Release` workflow (`.github/workflows/release.yml`) has **failed on every
recent tag** (v0.2.1-playtest, v0.2.2-playtest) at the step **"Install SteamCMD +
fetch server assemblies (on cache miss)."** The releases exist anyway because they
were finished **by hand** after CI choked — re-introducing exactly the manual path
ADR-0004 exists to eliminate.

Root cause is a **cache-key mismatch** between `ci.yml` and `release.yml`, made
worse by GitHub Actions cache **branch-scoping**. Release never hits the warm
cache, so it always falls through to the live SteamCMD install — the fragile step
— on every run.

## Evidence

`gh run list` shows the pattern cleanly:

| Tag | CI | Release |
|---|---|---|
| v0.2.2-playtest | ✅ success (on `v1`) | ❌ **failure** |
| v0.2.1-playtest | ✅ success (on `v1`) | ❌ **failure** |

The failed Release run (27052049008) step rollup:

```
✅ Resolve tag
✅ Checkout (the tag)
✅ Setup .NET SDK
✅ Compute weekly cache epoch
✅ Cache Valheim server assemblies        ← cache MISS (key didn't match)
❌ Install SteamCMD + fetch server ...    ← FAILS here
⤵  Configure build reference paths        skipped
⤵  Build (Release)                        skipped
⤵  Pack modpack (deterministic)           skipped
⤵  Create / update the release ...        skipped   ← so assets never auto-published
⤵  Bump installer.ps1 SHA / Open PR       skipped   ← so pin-PR never auto-opened
```

The **CI** run on the same commit (27052030115) shows its SteamCMD step
**`skipped`** — i.e. CI got a **cache hit** and never exercised the fragile path.
CI is green *because* it avoids the failing step; Release is red *because* its
mismatched key forces it through that step every time.

## Root cause: mismatched cache keys (+ branch-scoping)

```yaml
# ci.yml:62
key: valheim-managed-${{ env.VALHEIM_SERVER_APPID }}-${{ hashFiles('.github/workflows/ci.yml') }}-week${{ steps.weekstamp.outputs.week }}

# release.yml:77
key: valheim-managed-${{ env.VALHEIM_SERVER_APPID }}-week${{ steps.weekstamp.outputs.week }}
```

Two independent reasons Release can't reuse CI's warm Managed-folder cache:

1. **Different key shape.** CI's key embeds `hashFiles('.github/workflows/ci.yml')`;
   Release's doesn't. Even on the same week epoch the two keys never collide, so a
   cache saved by CI is invisible to Release (and vice-versa).
2. **Branch/ref scoping — and the workflows aren't on `main`.** GitHub Actions
   caches are readable from the ref that saved them **or from the repo default
   branch** (`main`). Two compounding facts here:
   - The workflows (`ci.yml`, `release.yml`) live **only on `v1`, not on `main`**
     (verified: `git cat-file -e main:.github/workflows/release.yml` → absent;
     `v1` → present). So **CI never runs on `main`**, which means **no cache is
     ever warmed on the default branch.**
   - CI warms its cache on `v1` / PR refs (branch-scoped). Release runs on a **tag
     ref** (`refs/tags/v0.2.x`). A tag ref can read `main`'s caches (none exist)
     but **cannot** read `v1`'s branch-scoped cache.

   **Consequence that changes the fix priority:** even if the key *shapes* were
   identical, Release would STILL cold-miss, because the only warm cache lives on
   `v1` and a tag ref can't reach it. So key-alignment alone (option 2 below) does
   NOT fix this — hardening the fetch (option 1) is **necessary**, not optional.

Net effect: **Release always misses → always runs live SteamCMD → fails whenever
that step is flaky** (transient apt mirror, Steam rate-limit/timeout, or runner
disk pressure during `app_update 896660 validate`, which pulls a multi-hundred-MB
dedicated server).

## Why this matters (the second-order damage)

When Release fails at SteamCMD, the **publish + pin-PR steps are skipped**. So the
automation's whole reason for existing — ADR-0004's *publish-then-PR, no broken
window* — silently doesn't happen. Someone then publishes the asset by hand. That
re-opens the 2026-06-04 manual-release hazard (upload-then-scramble) the pipeline
was built to make structurally impossible. **A flaky CI release step quietly
reverts the team to the exact unsafe process the ADR banned.**

## Fix options (NOT applied — CI change, needs Daniel's gate)

Ranked by robustness:

1. **Make the build self-sufficient on cache miss (best).** The SteamCMD step
   already runs on miss; the problem is it's *fragile*, not absent. Harden it:
   - Add a retry wrapper around the `steamcmd ... +app_update` call (Steam fetch is
     the flaky part): 3 attempts with backoff.
   - Consider `app_update` **without** `validate` on CI (validate re-hashes every
     file — slower, more timeout-prone; the anonymous server download is already
     integrity-checked by Steam).
2. **Align the cache keys so Release can hit a warm cache (defence in depth).**
   Make both workflows use the **same** key shape, e.g.
   `valheim-managed-${APPID}-week${epoch}` in BOTH. Note this only helps if a cache
   under that key is reachable from the tag ref — see (3).
3. **Warm the cache on a ref the tag can read.** GitHub cache scoping: a tag ref
   can read caches created on the **default branch** (`main`). So either (a) run a
   tiny scheduled/`main`-push job that primes `valheim-managed-${APPID}-week${epoch}`
   on `main`, or (b) accept that Release will cold-fetch and just make the fetch
   robust per (1). Option (1)+(3a) together = both fast and reliable.
4. **Pre-host the Managed reference set** as a private release asset / artifact and
   `curl` it in CI instead of SteamCMD. Eliminates the Steam dependency entirely at
   the cost of maintaining a mirror (must refresh on game patches). Heaviest, most
   reliable.

**Recommended:** (1) first (smallest, kills the actual failure), then (2) for
hygiene. (3a)/(4) only if SteamCMD remains flaky after retries.

## How to cut a release TODAY (until the workflow is fixed)

Two viable paths:

- **A — Trust the automation, watch it, hand-finish only on failure.**
  `git push origin v0.2.3-playtest` → poll the Release run → if it dies at
  SteamCMD, re-run it (`gh run rerun <id>` — transient failures often pass on
  retry) before falling back to hand-publish.
- **B — Local pack + `gh release create` + manual pin-PR**, in the ADR-0004 order
  (publish asset first, THEN open/merge the installer-pin PR — never reverse). The
  local packer (`scripts/pack-modpack.sh`) produces the byte-identical
  deterministic artifact CI would, so the hand-published asset is legitimate. This
  is what the prior sessions did when CI failed.

Path A is preferred (keeps the safety properties); B is the fallback.

## Verification commands (reusable)

```bash
# Did the most recent Release run pass, and where did it die?
gh run list --workflow Release --limit 5
gh run view <id> --json conclusion,jobs \
  --jq '{conclusion, steps:[.jobs[].steps[]|{name,conclusion}]}'

# Compare the two cache keys (the smoking gun)
grep -nE 'key:.*valheim-managed' .github/workflows/ci.yml .github/workflows/release.yml
```

## Open follow-ups

- [ ] Decide + apply a fix option (Daniel-gated; CI workflow change).
- [ ] After fixing, cut a throwaway test tag to confirm Release goes green
      end-to-end (publish + auto-PR) before trusting it for a real release.
- [ ] Until then, every release must be watched, and hand-finished in ADR-0004
      order if SteamCMD trips.
