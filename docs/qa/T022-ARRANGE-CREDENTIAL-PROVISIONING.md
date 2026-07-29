---
title: T022 ARRANGE — credential provisioning and consumer readability
status: current
last_updated: 2026-07-29
---

# T022 ARRANGE — credential provisioning and consumer readability

Implementation notes for issue #452 and invariant I4 in the parent T022 ARRANGE
specification.

## Scope and phase boundary

This change owns the credential part of **PROVISION** and its immediate read-back
assertion. It does not change STATIC's responsibility:

- STATIC (`S4-LANE-PASSWORD-POLICY`) checks that every password-gated client declares a
  credential and that the declared consumer uid equals that client's own uid. It does
  not inspect a file that PROVISION has not written yet.
- PROVISION mints the disposable-lane password, writes each declared lane-password file
  and bootstrap doc, and prepares their containing directory.
- The provisioner then opens each written file **as its declared consuming uid**. This
  is the readable-in-fact assertion required by I4. A future standalone VERIFY phase
  may report the same fact in its aggregate readiness report; STATIC remains pure.

Existence and readability are one precondition. A missing file and a permission-denied
file both leave the headless client without credentials and therefore produce the same
fail-closed error.

## Per-run credentials, never descriptor secrets

`build_live_run()` refuses a descriptor containing `lane_password`. It mints one fresh
CSPRNG value per run and composes the same value into:

1. the disposable dedicated server's `-password` argument; and
2. every client's `server_password_file`.

The descriptor retains only paths, client identities, and other topology. The password
value is not persisted there. Bootstrap HMAC/operator credentials continue to come from
the existing once-per-run short-TTL wire-envelope mint. Teardown unlinks both credential
classes on every existing cleanup path.

## Cross-uid filesystem policy

The permanent dual-user rig has a uid-1000 runner and a uid-1001 client. Both credential
classes therefore use the threat-model-approved local-read policy:

- containing directory: `0711` — a consumer that knows the exact path may traverse it,
  but cannot list neighbouring names;
- credential file: `0644` — readable by the consuming uid;
- values: per-run throwaways with short lifetime, swept on teardown;
- launch sidecars contain only paths, never credential values.

`prepare_credential_directory()` repairs an already-existing `0700` directory to `0711`;
`os.makedirs(..., mode=0711)` alone would not change an existing directory and would
preserve the original cross-uid lock.

## Consumer-identity assertion

`credential_access.assert_readable_as_consumer()` executes a one-byte open/read probe
under the declared uid. For a foreign uid it uses the rig's passwordless operator seam:

```text
sudo -n -u #<uid> -- <python> -c <read probe> <credential path>
```

A failure names all three actionable coordinates required by the issue:

- client actor;
- credential path;
- consuming uid.

Provisioning fails before any client launch and removes files already written during the
failed batch. No graphical client is needed to exercise this contract.

## Verified versus not claimed

Automated tests cover minting, descriptor-secret refusal, directory modes, per-client uid
selection, read failures, cleanup, and server/client password equality. The implementation
was also exercised on the real host by reading a freshly provisioned file as uid 1001.

This proves filesystem provisioning and cross-uid readability. It does **not** claim a GPU
client was launched, joined the lane, or completed a playable T022 acceptance run.
