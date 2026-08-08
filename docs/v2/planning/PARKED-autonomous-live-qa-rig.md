---
title: "PARKED — the autonomous two-client live QA rig (T022)"
status: historical
parked_on: 2026-08-07
parked_by: Daniel (decision), Starbright (write-up)
purpose: Record why the autonomous in-world QA rig is parked, exactly what is parked versus kept, and the written condition that un-parks it.
---

# PARKED — the autonomous live QA rig

**Decision, 2026-08-07: stop building the robot that plays the game. Verify the
remaining cards with joined-client sessions instead.**

This is a **park**, not an abandonment, and not a judgement that the idea was wrong.
Nothing is deleted. The code, the evidence bundles, and the design all stay exactly
where they are. What stops is *reaching for this rig as the way to validate a card*.

## The number that decided it

| | The rig | A person playing and looking |
|---|---|---|
| Build cost | ~33,700 lines, 39 commits, 16 days | — |
| Product findings, lifetime | **one** (2026-08-03: a crafted item came out unstamped) | **thirteen** joined-client artifacts across five tracers, including a real FAIL |
| Findings on 2026-08-07 | zero, after five consecutive walls | — |

Every wall hit on 2026-08-07 was the harness, the configuration, or the operator's own
process — a stale checkout, a missing key in a descriptor, a lane password minted for a
server the runner does not own, a timeout applied to one client but not its sibling, and
a client that boots but never joins. **Not one of them said anything about the mod.**

That ratio is the finding. The rig had become a second product competing with the first,
and the method it was meant to replace already works and already produces artifacts.

## What is PARKED

The **fully-autonomous two-client in-world driver**: launching two licensed clients,
driving them through character select into a lane, executing acceptance legs, and
composing a verdict with no human present.

It is parked because it sits on a stack of integration problems that are individually
solvable and collectively open-ended — Steam's fork timing, process tracking across a
daemon fork, a headless character-select click, environment delivery via a launch
sidecar. Each fix reveals the next.

## What is KEPT and STILL USED

The safety machinery is genuinely good, was proven repeatedly, and applies to **any**
live method including a human one. Do not let the park bury it:

- **Refuses to run when a human's game is open.** Distinguishes a harness-owned client
  from the user's own by process ancestry, and blocks rather than seizing.
- **Production untouched, verified before and after.** The two live servers are never
  targets; the disposable lane is separate and proven separate.
- **Exact-artifact gates.** Deploy and manifest are derived from built bytes and the
  commit, so what is tested cannot silently differ from what was built.
- **Adminlist captured and restored byte-identically**, with the hash proved.
- **Teardown proven on every exit path** from the process table, not asserted.
- **The runtime-window protocol**: request with a scenario and a duration, auto-grant
  inside a granted window, log every grant and what it produced.

These live under `qa/` and in the runtime-access protocol. They are reusable as-is.

## The resume condition — written as a trigger, not a feeling

**Un-park when we need these checks to run unattended on every change.**

That is the only thing the autonomous rig buys that a person does not. The current
milestone does not need it: it needs about nine cards checked **once**, and a person
checking them is one co-designed session.

A cheaper secondary trigger, if someone wants to test the water without committing:
**a client auto-joins the disposable lane twice in a row, unattended, inside one hour.**
If that cannot be demonstrated in a single timeboxed sitting, the park stands.

Absent one of those two, do not spend a runtime window on this rig.

## What replaces it, right now

A **co-designed joined-client session**: Daniel at the machine, working the
shipped-but-unverified cards as one batch rather than one card at a time. The thirteen
existing artifacts were produced this way. Per Daniel's standing rule, in-game tests are
co-designed — the per-client step plan is agreed before anything runs, never improvised
by the agent mid-session.

## The honest caveat

We were plausibly close. On the last attempt the client launched, was tracked, and
reached the main menu; the auto-join code exists and has demonstrably worked live in an
earlier spike. It may be one bug from a verdict.

It may also not be. "One more wall" was predicted and wrong four times on 2026-08-07.
The park is a judgement about the *pattern*, not about the next bug.

## For whoever reads this later

If you are here because you want to validate a card and the rig looked like the obvious
tool: it is parked on purpose, and the reason is above. Check the resume condition first.
If neither trigger is met, the faster path is a person playing the game and looking.
