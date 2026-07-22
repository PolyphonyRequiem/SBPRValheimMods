# qa/contracts — SBPR.QaHarness.T022 wire schemas (ADR-0009 §1, §3)

These JSON Schema files are the **shared wire truth** between the external Python
runner (`qa/runner/`) and the fail-closed BepInEx helper (`qa/SBPR.QaHarness.T022/`).
The helper validates every inbound request against `request.schema.json` +
`envelope.schema.json` and rejects anything off-schema; it emits receipts shaped by
`receipt.schema.json`. **No product types ever cross the wire.**

## M1 status: real schemas, code-synced

**M1** (this card) replaces the M0 disabled placeholders with the real wire truth,
matching the engine-free contract core in `../SBPR.QaHarness.T022/Contracts/`:

| File | Defined in | Contents (per ADR-0009) |
|------|-----------|--------------------------|
| `request.schema.json`  | M1 | The finite verb enum (kept identical to `VerbCatalog`) + typed argument-bound summary (§3.1). |
| `envelope.schema.json` | M1 | `{nonce, seq, expiry, hmac, role, worldUid, verb, requestId, args}`; the server binds the actual delivering peer at the M2 channel layer (§3.2). |
| `receipt.schema.json`  | M1 | Descriptive primitive facts only — **never a product PASS/FAIL verdict** (§6); the `reason` enum mirrors `RejectReason`. |

Spec⇄code drift is prevented mechanically: `qa/tests-core/SchemaSyncTests.cs` fails
the build if `request.schema.json`'s verb enum diverges from `VerbCatalog`, or if
`receipt.schema.json`'s reason enum diverges from `RejectReason`.

The helper still opens **no channel** and processes **no envelope at runtime** in M1
(the loopback/ZRpc dispatcher lands in M2); these schemas + the engine-free arming/
admission decision are the validated contract those channels will carry.

Only the external runner (`qa/runner/`) composes a verdict; the helper emits
primitive facts and nothing more.
