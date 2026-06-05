---
title: "Trailborne M0 — Playtest Script"
status: historical
purpose: M0 playtest script/log — point-in-time.
---

# Trailborne M0 — Playtest Script

**Build:** SBPR Trailborne 0.1.0
**Server:** Niflheim (RequiemSoul, `192.168.1.x:2456` over LAN, password as configured)
**Join code:** 192302 (Steam crossplay)
**Server load confirmed:** ✅ `[Info :SBPR Trailborne] [Trailborne] ZNetScene registration complete.`
**Three surfaces registered:** ✅ `piece_sbpr_orienteering_table`, `piece_sbpr_path_lamp`, `SBPR_TrailblazersSpade`
**Recipes wired:** ✅ Hammer (table), Orienteering Table (spade + lamp)

---

## 1. Install on your client (one-time)

1. Make sure BepInExPack_Valheim is installed in your client's Valheim folder (r2modman or manual).
2. Copy the build artifact to your client:
   - Artifact: `/home/polyphonyrequiem/repos/SBPRValheimMods/build/SBPR.Trailborne-0.1.0.zip`
   - Extract `SBPR.Trailborne.dll` + `trailblazers_spade_v0.1.png` into your client's `BepInEx/plugins/SBPR.Trailborne/`
3. Start Valheim. In the BepInEx console window during launch, look for:
   ```
   [Info   :SBPR Trailborne] [Trailborne] Awake — SBPR Trailborne 0.1.0 booting
   [Info   :SBPR Trailborne] [Trailborne] Harmony patches applied.
   ```

## 2. Connect

- Join Niflheim (LAN or Steam join code 192302).
- Spawn into world.

## 3. Acceptance checklist — tick each

- [ ] **Hammer build menu shows "Orienteering Table"** — open Hammer (right-click your equipped Hammer), tab through categories, find it under the Crafting tab. Cost: 10 Wood + 5 Stone.
- [ ] **Place the table** — costs the wood/stone, drops a placeholder workbench-shaped piece.
- [ ] **Standing near the table opens a crafting station** — open inventory `Tab` → Craft tab → station shows as "Orienteering Table" with the spade + lamp recipes listed (greyed out if you lack mats).
- [ ] **Craft the Trailblazer's Spade** — 5 Wood + 5 Stone. Appears in inventory.
- [ ] **Equip the spade** — looks like a vanilla Hoe (placeholder mesh).
- [ ] **Left-click on ground tile** — applies the current path operation (placeholder: vanilla Hoe op).
- [ ] **Cycle path modes via scroll wheel** — same UX as the vanilla Hoe (TWEAK ME: I'll rebind to right-click in a later milestone — but for M0 confirm cycling works at all). Three modes: dirt-path / paved-road / level-clear.
- [ ] **Craft a Path Lamp at the table** — 3 ElderBark (Black Forest "Corewood" stand-in) + 2 Resin.
- [ ] **Place the Path Lamp** — placeholder = vanilla standing wood torch. Light it (E or its equivalent) and verify it emits light at night.
- [ ] **Log out, log back in** — table, spade in inventory, placed lamp all survive.

## 4. Known M0 limitations (don't flag these as bugs)

- Models are vanilla mesh clones — no bespoke art. M0 doctrine: gameplay-first, art in M0.2+.
- Spade icon is reused for the Path Lamp + Orienteering Table in the build menu (placeholder shared icon).
- Path-mode cycle uses vanilla scroll wheel (TWEAK ME: right-click rebind, M0.1 or M1).
- Ink-as-ingredient is NOT in M0 — placeholder vanilla mats only. Inks come in M1.
- Server-gate is stubbed `OnSBServer => true`. M1 wires the real client/server handshake so vanilla clients can join Niflheim without Trailborne (they currently cannot — they'll mismatch the registered prefab list).

## 5. Pings during playtest

If anything in the checklist hard-fails (e.g. piece doesn't appear, server kicks you), grab:
1. Your client's `BepInEx/LogOutput.log` (or screenshot of the in-game error)
2. Server log: `sg docker -c "docker logs niflheim-server --tail 200"` from RequiemSoul

Drop those + the symptom and I'll diagnose.

## 6. Server log confirmation (already verified by Starbright)

```
[Info   :   BepInEx] Loading [SBPR Trailborne 0.1.0]
[Trailborne] Awake — SBPR Trailborne 0.1.0 booting (folder=…, OnSBServer=True)
[Trailborne] Harmony patches applied.
[Trailborne] ZNetScene.Awake postfix — registering content surfaces…
[Trailborne] Registered piece: piece_sbpr_orienteering_table
[Trailborne] Registered piece: piece_sbpr_path_lamp
[Trailborne] Registered item prefab: SBPR_TrailblazersSpade
[Trailborne] ZNetScene registration complete.
[Trailborne] Added recipe for spade.
[Trailborne] ObjectDB wiring complete (items + recipes + hammer pieces).
Game server connected
Session "Niflheim" registered with join code 192302
```

No exceptions from SBPR code. Vanilla `ZNetScene.RemoveObjects` NullRef noise pre-dates the mod (pre-existing image issue, unrelated).
