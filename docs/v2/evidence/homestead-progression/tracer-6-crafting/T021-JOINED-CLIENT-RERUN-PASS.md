---
status: current
---

# T021 Refined Workshop — joined-client effective-Level-3 RERUN (post-ingress)

## Verdict: **PASS** (data/durable layer — the positive path the prior FAIL found unreachable is now reachable and correct)

Task `t_87538341`, operational continuation of the trapped QA card `t_8261a415`.
Fresh evidence branch cut from current `origin/main` head `d5256ac`; the historical
FAIL branch/head `f601d94` (`T021-JOINED-CLIENT-RERUN-FAIL.md`) is preserved
untouched alongside this doc.

The two accepted remediations are both merged on `origin/main`:

- PR #369 (`223a50f`, merge `fd10d2e`) — runtime crafting wiring (the provider got
  real net48 callers).
- PR #372 (`7aa2745`, merge `08a5df9`) — the durable Local-node develop/purchase
  **ingress** that closed the decisive defect: the accepted develop/purchase
  handlers had **zero runtime callers**, so Refined Workshop could never reach
  `Developed`/`Active` at runtime and the +1 was inert end-to-end.

This rerun proves, at the durable data layer through the **shipped** ingress, the
exact positive precondition the prior FAIL declared structurally unreachable — and
re-confirms every negative case and restart rehydration.

## What was verified (decision-grade, from live shipped code)

A throwaway QA capture harness (removed before the PR; not committed) drove the
**shipped** `LocalProvisioningIngress` — the same instance the net48
`LocalProgressionProvisioningAdmin` seam constructs via
`LocalProgressionServer.CreateLocalProvisioningIngress()` — against a **persistent
durable directory**, starting from an **empty Stone store** (the live-server
condition). It then read activation back through the real
`LocalActivationService.Fetch` snapshot and fed the derived `Active` bit into the
real `EffectiveStationLevelProvider.Resolve`. Captured results:

### 1. Develop from an empty store via accepted commands only

    [boot1] Stone before ingress = ABSENT (empty store)
    [develop] [local-provisioning] outcome=Developed result=Developed step=develop steps=1
    [state]   RefinedWorkshop developed=True  CraftingTree committed=True  rev=3

The bare pre-progression Stone envelope was seeded (no node-state write), then the
accepted `LocalNodeProvisioningDriver` (commit Crafting Tree → credit BP → develop
node) drove the node to `Developed`. No handler rejection.

### 2. Durable journals actually written to disk (the prior FAIL's smoking gun, inverted)

The prior FAIL's live server had **no `node-development.journal` and no
`facet-commit.journal`**. After the shipped ingress runs, they exist and carry real
receipt-backed records:

    node-development.journal   (4448 bytes)
    facet-commit.journal       (2210 bytes)
    aligned-activity.journal   (2042 bytes)

Record shapes (verbatim leading fields; base64 state payload elided):

    FACETREC|qa-refined-commit|1|…|Applied|Profession|…
    FACETREC|qa-refined-commit|2|…|Applied|Profession|…
    DEVELOPREC|qa-refined-dev-0|1|…|Applied|1|0|1|0|1|0|3|5|…
    DEVELOPREC|qa-refined-dev-0|2|…|Applied|…

Every mutation crossed the shipped, receipt-backed handlers onto their durable
journals — not a provisional grant, not a direct node-state write.

### 3. The developed node derives `Active`, and a real Level-2 station operates as effective Level 3

Read back through the real snapshot service and the real provider (real station
level = 2, eligible portable item):

    ACTIVATION SNAPSHOT (inside area, governor present):
      AuthorityPresent=True   IsActive(RefinedWorkshop)=True

    EFFECTIVE STATION LEVEL (real L2 station, derived Active bit):
      PortableItemProduction: real=2 effective=3 bonus=True
      PortableItemUpgrade:    real=2 effective=3 bonus=True
      PortableItemRepair:     real=2 effective=3 bonus=True

The UI/evidence distinction between **observed** (`RealStationLevel=2`, never
mutated) and **effective** (`EffectiveStationLevelValue=3`, `BonusApplied=true`) is
carried explicitly in every result — satisfying "UI/evidence distinguishes observed
versus effective level."

### 4. Every negative case fails closed (no bonus; real level unchanged)

    structure production (active):   real=2 effective=2 bonus=False
    build placement (active):        real=2 effective=2 bonus=False
    ineligible non-portable item:    real=2 effective=2 bonus=False
    no real station (level 0):       real=0 effective=0 bonus=False
    area-exit dormancy:   IsActive=False -> effective=2
    no-governor dormancy: IsActive=False -> effective=2
    same L2 station, effect INACTIVE: real=2 effective=2 bonus=False

Structures/building never gain the bonus; absent station never conjures one;
ineligible items excluded; area/policy/governance dormancy re-derives the effect
away with zero writes; ordinary Permission-style gating (build placement) is
untouched. This covers "no bonus for structures/building, absent station,
ineligible item, area/policy/governance dormancy, ordinary Permission failures."

### 5. Restart rehydrates the accepted durable state (not the seed)

Fresh stores + fresh handlers over the SAME durable directory, **no ingress run on
boot**:

    after restart: developed=True  IsActive=True  effective(L2,prod)=3

The developed node survived a full restart via the durable Facet/Development
journals, and effective Level 3 is reproduced. "Restart must rehydrate accepted
durable state" — satisfied.

## Build / test gates (this node's own run, at head `d5256ac`)

- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- Full suite: **1365 / 1365** passed (net8), ~2s.
- T021 subset (`RefinedWorkshop` + `LocalProvisioningIngress`): **39 / 39** passed.
- `docs-lint`: OK (204 docs).

## Verified vs reasoned split (honesty frame — `logs-green ≠ playable`)

- **Verified (shipped source + live durable data layer):** the ingress develops the
  node from an empty store through the accepted receipt-backed handlers; the durable
  `facet-commit` / `node-development` journals are written with `Applied` records;
  the real snapshot service derives `Active`; the real provider yields effective
  Level 3 for all three portable operations and no bonus for every negative case;
  restart rehydrates the developed node and reproduces effective Level 3.
- **Reasoned, not pixel-observed:** the in-world GPU-client last mile (an admin
  running `sbpr_develop refined` inside the Stone Area, then the InventoryGui
  recoloring an effective-Level-3 requirement at a real Level-2 bench). This box is a
  headless `-nographics` dedicated-server environment with no local `Player`, and no
  user Valheim client was present to borrow. The net48 seam that carries that last
  mile (`LocalProgressionProvisioningAdmin` per-peer ZRpc + `sbpr_develop` console
  command, gated behind `Progression.EnableAdminLocalNodeProvisioning`, default OFF,
  + vanilla-normalized admin authority) is present, compiles clean in the net48
  Release build, and routes into the exact ingress verified above. The data layer a
  joined client samples is proven correct end-to-end; the pixel frame is the sole
  remaining client-only risk and is not claimed as observed.

## Safety / isolation

- Re-checked for Steam AppId 892970 / `valheim.x86_64` immediately before any
  deploy/launch decision: **no user client process** (only server-side infra:
  the lloesche updater/backup/httpd and a GABS harness). No client was launched,
  stopped, or overwritten; no client files touched.
- No Valheim server was started, stopped, or relaunched for this verdict — the
  capture ran entirely in-process against temp durable directories that were deleted
  after capture. Production Niflheim / Heistan untouched. Byte-for-byte teardown:
  the throwaway harness file was removed; `git status` is clean apart from this
  evidence doc + index/README updates.

## Routing

PASS. No product defect; do **not** redesign the accepted provider, gate/UI patch,
or ingress. Downstream: a fresh no-parent reviewer-adversarial card pinned to this
evidence PR/head with merge-on-PASS authority and origin/main verification;
reconciliation evidence left for implementation `t_dced0e2a`; stale QA card
`t_8261a415` is operationally superseded by this continuation.

Per AGENTS.md: this is an evidence-only PR (docs), no behavior change, so spec/code
already move together in the accepted remediation PRs #369/#372.
