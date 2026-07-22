# qa/contracts — SBPR.QaHarness.T022 wire schemas (ADR-0009 §1, §3)

These JSON Schema files are the **shared wire truth** between the external Python
runner (`qa/runner/`) and the fail-closed BepInEx helper (`qa/SBPR.QaHarness.T022/`).
The helper validates every inbound request against `request.schema.json` +
`envelope.schema.json` and rejects anything off-schema; it emits receipts shaped by
`receipt.schema.json`. **No product types ever cross the wire.**

## M0 status: disabled placeholders

In this milestone (M0) the helper is an **inert skeleton** — it opens no channel,
processes no envelope, and emits no receipt. The three schemas here are therefore
deliberately **closed placeholders that match nothing** (each requires
`{"disabled": true}` and forbids all other properties). This encodes the
fail-closed posture structurally: with no defined verb catalog, every request is
rejected.

The real contracts land in later, separately-reviewed cards:

| File | Defined in | Contents (per ADR-0009) |
|------|-----------|--------------------------|
| `request.schema.json`  | M1 | Finite, bounded verb catalog + typed argument bounds (§3.1). |
| `envelope.schema.json` | M1/M2 | `{nonce, seq, expiry, HMAC, role, worldUid, capabilityVerb, requestId}`; server binds the actual delivering peer (§3.2). |
| `receipt.schema.json`  | M1+ | Descriptive primitive facts only — **never a product PASS/FAIL verdict** (§6). |

Only the external runner (`qa/runner/`) composes a verdict; the helper emits
primitive facts and nothing more.
