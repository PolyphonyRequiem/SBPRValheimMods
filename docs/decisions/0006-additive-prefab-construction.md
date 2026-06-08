---
title: "ADR-0006: Additive prefab construction — no runtime prefab cloning"
status: accepted
---

# ADR-0006: Additive prefab construction — no runtime prefab cloning

- **Status:** accepted
- **Date:** 2026-06-08
- **Deciders:** Daniel + Starbright

## Context

Every hard bug in Trailborne to date traces to one root cause: we built our
content by **cloning a vanilla prefab and then subtracting** the parts we didn't
want. This is *subtractive* construction, and it has bitten us repeatedly:

- **The v0.2.7 client soft-lock.** The cairn cloned `Pickable_Stone` (which carries
  a `ZNetView`) onto the live cairn at runtime, then `DestroyImmediate`'d that
  ZNetView. Nested-instantiate-inside-the-init-ZDO-window + DestroyImmediate
  orphaned null-ZDO entries in `ZNetScene.m_instances`, which vanilla
  `ZNetScene.RemoveObjects` then dereferenced every frame → repeating NRE.
  Confirmed against `ZNetView.Awake` (the "Double ZNetView" warning at the
  `m_useInitZDO && m_initZDO == null` branch).
- **The cairn-as-bonfire mess (v0.2.7→0.2.8).** The cairn cloned `bonfire`, which
  meant inheriting its entire fire system — `Fireplace` (re-asserts its flame
  objects every 2 s via `InvokeRepeating("UpdateFireplace")`), an `Aoe` that
  deals fire damage, a `CinderSpawner`, `EffectArea`, `SmokeSpawner`. We spent two
  releases *muzzling* these one at a time and kept missing one (the `Aoe` burn,
  then the `CinderSpawner`). You cannot reliably suppress a system that
  re-asserts itself — and you can never be sure you found every component the
  donor brought along.

The common shape: **clone → inherit everything → fight to suppress the unwanted
parts.** The donor always has one more component than you remembered. Subtraction
never converges.

A key fact closes the central unknown: a working **`ZNetView` does not require a
clone.** `ZNetView.Awake` (verified against the decompiled `assembly_valheim`)
needs only (1) `ZDOMan.instance != null` and (2) a `GetPrefabName()` whose hash is
registered in `ZNetScene`. It reads three **public** serialized fields —
`m_persistent`, `m_type`, `m_distant` — and creates its own ZDO from them
(`CreateNewZDO`). An `AddComponent<ZNetView>()` with those fields set, on a
GameObject whose name is registered in the prefab table, is a fully valid
networked object. The donor was never giving us anything we cannot set ourselves.

## Decision

**Build SBPR content prefabs ADDITIVELY: start from `new GameObject()` and
`AddComponent` only the components we intend.** Never `Instantiate` a vanilla
prefab (or any ZNetView-bearing object) to use as a mutable base at runtime, and
never register a clone of one as our prefab template.

We MAY read vanilla prefabs as **blueprints** — reading shared meshes, materials,
particle systems, `EffectList`s, and field values off a vanilla prefab obtained
via `ZNetScene.GetPrefab` (which returns the inactive template and fires no Awake).
Reading an asset reference is not cloning. Copying an `EffectList` value or a
shared mesh onto our own constructed GameObject is reference, not inheritance.

The networked skeleton (`Piece` + `WearNTear` + `ZNetView`) is assembled by
`AddComponent` with fields set explicitly. Visuals are constructed GameObjects
carrying only `MeshFilter`/`MeshRenderer` (+ `ParticleSystem`/`Light`/`AudioSource`
for cosmetics). The prefab is registered in `ZNetScene` by name, exactly as today.

Use the offline prefab X-ray tool (`vprefab inspect <name>`, `~/.local/bin/vprefab`)
to read a vanilla blueprint before constructing our own version — additive
construction must be *informed*, and the X-ray is how we learn the donor's real
structure (component layout, mesh names, field values) without instantiating it.

## Consequences

- **We own every component on the object because we put it there.** No surprise
  inherited systems, no suppression arms race, no donor landmines.
- **The class of runtime-clone ZDO-orphan crashes becomes structurally
  impossible** — we never instantiate a ZNetView-bearing object during another
  object's init window, so there is nothing to orphan.
- More upfront work per piece: we set `Piece`/`WearNTear`/`ZNetView` fields and
  wire destroy/hit/placement `EffectList`s ourselves (reference-copied off a clean
  vanilla stone piece such as `stone_floor`). This is the same trade ADR-0001
  already accepted — we write our own wiring in exchange for control + a clean
  story.
- Reading vanilla as a blueprint stays fully clean-room (ADR-0001): we read
  *vanilla* asset/field values to understand vanilla, we copy no third-party code.
- **Do not reintroduce runtime prefab cloning (`Instantiate` of a vanilla/ZNetView
  prefab as a mutable base) without a new ADR.** The clone-then-strip pattern in
  pre-0.2.8 cairn code is the anti-pattern this ADR retires.

## Alternatives considered

- **Clone-then-strip (the status quo through v0.2.8).** Pragmatic and fast to
  start, but it is the exact pattern that produced every major bug above. The
  v0.2.8 fire fix worked by clone-then-strip and is functional, but it leaves the
  donor's networked skeleton (and any future-added donor component) as a standing
  liability. Rejected as the go-forward architecture; superseded by additive
  construction.
- **Adopt Jotunn's prefab/piece helpers.** Would pave from-scratch piece creation,
  but reintroduces the loader dependency ADR-0001 rejected. Rejected for the same
  reasons.
- **Keep cloning but add a "known donor components" suppression registry.** A
  table of everything to strip per donor. Rejected: it is subtraction with extra
  bookkeeping, and it still fails the moment a game update adds a component to the
  donor we didn't anticipate.
