# index — docs/qa

Machine-readable manifest of QA harness documentation.

| file | status | purpose |
|------|--------|---------|
| README.md | living | What belongs in docs/qa and the conventions it follows |
| T022-ARRANGE-STATIC-IMPLEMENTATION.md | current | The shipped STATIC arrange phase: manifest schema, nine preconditions including wrapper-level join delivery, and what is deliberately left to VERIFY |
| T022-ARRANGE-CREDENTIAL-PROVISIONING.md | current | Per-run lane-password minting, 0711/0644 cross-uid policy, and readability assertions performed as each consuming uid |

## Related, elsewhere

- `docs/decisions/0009-qa-harness-separate-fail-closed-mod.md` — why the QA harness
  is a separate fail-closed BepInEx mod plus an engine-free external runner that is
  the sole PASS/FAIL composer.
- `AGENTS.md` §"QA live-harness process discipline" — the operational rules for
  running the live rig (GABS daemon topology, process reaping, worktree isolation).
- `docs/qa/T022-ARRANGE-SPEC.md` — the parent arrange specification. **Not yet on
  `main`**; authored on branch `m6-lanepw-solo`. Fold the implementation notes into
  it as §4a when it lands.
