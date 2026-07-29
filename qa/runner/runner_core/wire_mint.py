"""Mint the QA arm-wire crypto envelope per run (M6-MINT).

## The defect this closes

`t022-run-descriptor.json` used to persist `wire.{nonce, expiry_unix_ms,
hmac_secret, operator_token}` — a live crypto envelope hand-provisioned into a
long-lived file. Nothing minted it, so the credential's lifetime was pinned to
whenever a human last hand-edited the descriptor. `live_composition.py` even
*claimed* "the runner mints the wire parameters", but the code copied them
verbatim. Live run `t_e8777cca` (2026-07-28) proved the failure mode: the
persisted `expiry` was 106 minutes in the past, the helper correctly refused to
arm, and zero AT legs were reachable.

## What this module does

`mint_wire_envelope(ttl_seconds)` produces ONE fresh envelope from a CSPRNG
(`secrets`), matching the encoding/length of the values it replaces
(`token_urlsafe(32)` == 43-char base64url), with `expiry_unix_ms = now + TTL`.
It is called ONCE per run, upstream of both consumers (the bootstrap-doc
provisioner and the live transport), so the docs and the transport authenticate
against one identical envelope and cannot diverge.

`assert_descriptor_carries_no_wire_secrets(wire)` fails closed if a descriptor
still ships a persisted secret-bearing wire field, naming the field — a stale
token must never be quietly accepted over the freshly minted one.

`resolve_ttl_seconds(descriptor)` derives a conservative default TTL from the
runner's own boot budgets so a legitimate full four-AT run cannot outlive its
own credential, while a leftover doc goes useless quickly.

Engine-free stdlib only. No Valheim/BepInEx/Unity import.
"""
from __future__ import annotations

import secrets
import time
from typing import Any, Dict, Mapping

# The four fields that are the run's SECRET crypto envelope. These are minted per
# run and MUST NOT be persisted in the descriptor. `wire.endpoints`,
# `wire.world_uid`, `wire.start_generation`, `wire.entitlement` are topology and
# stay in the descriptor.
SECRET_WIRE_FIELDS = ("nonce", "expiry_unix_ms", "hmac_secret", "operator_token")

# CSPRNG byte width for each token. `secrets.token_urlsafe(32)` yields a 43-char
# base64url (no padding) string — the exact encoding/length of the values these
# replace in the hand-provisioned descriptor.
_TOKEN_BYTES = 32

# Conservative floor for the minted credential's lifetime (seconds). Even a run
# whose descriptor names tiny boot budgets gets at least this long a credential.
_TTL_FLOOR_SECONDS = 3600  # 1 hour

# Headroom multiplier over the worst-case aggregate client-boot wall time. The
# boots are the dominant real-time cost of a run (each AT leg runs under a
# tick-budget, not a wall clock); 2x leaves room for lane spawn, entitlement
# seeding, the four legs, and teardown on top of the boots.
_TTL_BOOT_HEADROOM = 2.0


class WireSecretPersistedError(RuntimeError):
    """The descriptor still carries a persisted secret-bearing wire field.

    Raised at compose time so a stale hand-provisioned token can never be
    silently preferred over the freshly minted envelope. Fail closed.
    """


def mint_wire_envelope(ttl_seconds: float, *, now_ms: int | None = None) -> Dict[str, Any]:
    """Mint ONE fresh crypto envelope for this run.

    `nonce`, `hmac_secret`, `operator_token` come from a CSPRNG (`secrets`),
    each a 43-char base64url token (`token_urlsafe(32)`) matching the encoding
    of the values they replace. `expiry_unix_ms = now + ttl_seconds`, strictly
    in the future by construction (given a positive TTL).

    This is called ONCE per run and passed down to BOTH consumers, so the
    bootstrap docs and the live transport share one identical envelope.
    """
    if ttl_seconds <= 0:
        raise ValueError(f"ttl_seconds must be > 0 (got {ttl_seconds!r})")
    base_ms = int(time.time() * 1000) if now_ms is None else int(now_ms)
    return {
        "nonce": secrets.token_urlsafe(_TOKEN_BYTES),
        "hmac_secret": secrets.token_urlsafe(_TOKEN_BYTES),
        "operator_token": secrets.token_urlsafe(_TOKEN_BYTES),
        "expiry_unix_ms": base_ms + int(round(ttl_seconds * 1000)),
    }


def assert_descriptor_carries_no_wire_secrets(wire: Mapping[str, Any]) -> None:
    """Fail closed if any secret-bearing wire field is still persisted.

    The descriptor describes the lane, clients, endpoints, and pins — durable
    topology — not credentials. A descriptor that still ships a persisted
    `nonce`/`expiry_unix_ms`/`hmac_secret`/`operator_token` is refused by name
    rather than silently overridden, so a stale token can never re-arm a hook.
    """
    for field in SECRET_WIRE_FIELDS:
        if field in wire:
            raise WireSecretPersistedError(
                f"descriptor wire still carries persisted secret field "
                f"{field!r}: the crypto envelope is now minted per run and MUST "
                f"NOT be persisted. Remove {field!r} (and every other of "
                f"{list(SECRET_WIRE_FIELDS)}) from the run descriptor's `wire` "
                f"block. Keep only topology (endpoints, world_uid, "
                f"start_generation, entitlement)."
            )


def resolve_ttl_seconds(descriptor: Mapping[str, Any]) -> float:
    """Resolve this run's credential TTL (seconds).

    Precedence:
      1. An explicit `wire.ttl_seconds` in the descriptor (operator override).
      2. A conservative default derived from the runner's OWN worst-case boot
         budget: `max_attempts * readiness_timeout_s * num_clients`, times a
         headroom multiplier, floored at `_TTL_FLOOR_SECONDS`.

    The boots dominate a run's wall time (each AT leg runs under a tick-budget,
    not a wall clock). Sizing the default off the boot budget makes the TTL
    long enough that a legitimate full four-AT run cannot outlive its own
    credential, yet short enough that a leftover doc on disk is useless.
    """
    wire = descriptor.get("wire")
    if isinstance(wire, Mapping) and "ttl_seconds" in wire:
        ttl = float(wire["ttl_seconds"])
        if ttl <= 0:
            raise ValueError(f"wire.ttl_seconds must be > 0 (got {ttl!r})")
        return ttl

    server = descriptor.get("server")
    boot = server.get("boot_policy", {}) if isinstance(server, Mapping) else {}
    # Mirror BootRetryPolicy's defaults so the TTL tracks the actual boot budget.
    # KEEP IN SYNC with BootRetryPolicy (operator_drivers.py). If these drift apart the
    # TTL is sized for a boot budget the runner no longer uses, and a legitimate run can
    # outlive its own credential — reintroducing the expired-envelope wall this module
    # exists to close. Raised to 300 alongside the measured cold-boot time (~145s just
    # to reach the main menu, before any world load or join).
    max_attempts = int(boot.get("max_attempts", 6))
    readiness_timeout_s = float(boot.get("readiness_timeout_s", 300.0))

    clients = descriptor.get("clients")
    num_clients = len(clients) if isinstance(clients, (list, tuple)) and clients else 2

    worst_case_boot_s = max_attempts * readiness_timeout_s * num_clients
    derived = worst_case_boot_s * _TTL_BOOT_HEADROOM
    return max(float(_TTL_FLOOR_SECONDS), derived)
