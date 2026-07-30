# index — docs/qa

Machine-readable manifest of QA harness documentation.

| file | status | purpose |
|------|--------|---------|
| README.md | living | What belongs in docs/qa and the conventions it follows |
| T022-ARRANGE-SPEC.md | current | **The canonical arrange specification.** Concurrent dual-user topology (#461 option B), invariants I1-I12 with evidence, phase ordering STATIC/SWEEP/STAGE/PROVISION/VERIFY/LAUNCH/READY and the issue that owns each |
| T022-ARRANGE-STATIC-IMPLEMENTATION.md | current | The shipped STATIC arrange phase: manifest schema, nine preconditions including wrapper-level join delivery, and what is deliberately left to VERIFY |
| T022-ARRANGE-STAGING.md | current | The shipped STAGE arrange phase: one manifest stages every artifact to every client, count-agnostic, creates missing plugin directories, and asserts per-client hashes |
| T022-ARRANGE-CREDENTIAL-PROVISIONING.md | current | Per-run lane-password minting, 0711/0644 cross-uid policy, and readability assertions performed as each consuming uid |

## Related, elsewhere

- `docs/decisions/0009-qa-harness-separate-fail-closed-mod.md` — why the QA harness
  is a separate fail-closed BepInEx mod plus an engine-free external runner that is
  the sole PASS/FAIL composer.
- `AGENTS.md` §"QA live-harness process discipline" — the operational rules for
  running the live rig (GABS daemon topology, process reaping, worktree isolation).
- Remaining arrange work: #455 (sweep + idempotency), #456 (post-arrange verification +
  readiness report), #457 (runner cutover). Each maps to a phase in
  `T022-ARRANGE-SPEC.md` §4.
