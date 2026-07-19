---
status: current
---

# Tracer 6 — Crafting branch evidence (v3: T022 Masterwork joined-client delivery)

This folder collects the v3 joined-client / live-artifact proofs for the Crafting
branch (Tracer 6) that supersede or extend the v2 host-first evidence.

## T022 — Masterwork joined-client Workmanship issuance + delivery

Acceptance: `AT-MASTERWORK-ISSUE`, `AT-ITEM-UPGRADE-PRESERVE`, `AT-ITEM-TRANSFER`,
`AT-ITEM-TAMPER-DEGRADE`.

The v2 evidence (`docs/v2/evidence/homestead-progression/tracer-6-crafting/T022-MASTERWORK.md`)
proved Masterwork only on a listen-host crafter and deferred the pure joined-client
issuance/transfer. The prior QA run
(`docs/v3/research/QA-T022-masterwork-joined-client.md`) proved that deferral was a
design gap: server-only integrity key + `Player.m_localPlayer` issuance gate made the
four ATs structurally unreachable on a dedicated-server topology. PR #388 closed the gap
with a bounded per-peer ZRpc server→client issuance + validation delivery channel.

`T022-MASTERWORK-JOINED-CLIENT.md` is the node-own decision-grade artifact for the
remediated head (PR #388 @ `8ccf6d3`): the four ATs verified at the delivery + data
layer via link-compiled real execution, the three runtime seams verified to bind live on
the real game assembly (isolated t009l dedicated-server boot + `MetadataLoadContext`
probe), and the GUI-pixel last mile reasoned — the accepted shape merged for the sibling
personal Character-Effect nodes T026 / T025 / T030.

Raw capture: `capture/t022-masterwork-nodeown-live-20260719-105702.log`.
