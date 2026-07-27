"""Fail-closed live-mode preflight for the T022 runner (ADR-0009 §5, §7, §8) — M6-EXEC.

Enabling live execution replaces the M5 blanket refusal with a real path that is
STILL fail-closed and explicitly opt-in. `--dry-run` remains the default and fully
working; `--live` runs ONLY when every one of these holds:

  1. `--live` was passed explicitly (never inferred).
  2. A disposable-lane sentinel is supplied and validates: it is the overlay's
     `lane_sentinel.json` (`kind == "sbpr-qa-overlay-lane-sentinel"`,
     `lane == "disposable"`), and it carries the hard production deny list
     (Niflheim 2456 / Heistan 2466). A sentinel that omits the deny list, targets a
     production port, or is not a disposable sentinel is REFUSED.
  3. The overlay pins verify: the supplied overlay manifest's recomputed part
     hashes match its recorded ones and the folded `overlay_digest` — i.e. the
     bundle has not drifted since it was packed.

Absent ANY of the three, the runner refuses live execution and says exactly why.
This module performs the DECISION only; it launches nothing, contacts no game, and
mutates no file. Composing it with the operator drivers + the live transport is the
runner's job under an explicit operator authorization — importing or evaluating this
gate never starts a run.

Engine-free: stdlib only, no product/game import.
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from typing import Any, Dict, Mapping, Optional

# Hard production deny list (ADR-0009 §5.1). The sentinel MUST enumerate both, and a
# live run may never target either. Mirrors operator_drivers.PRODUCTION_PORTS.
PRODUCTION_PORTS = frozenset({2456, 2466})

SENTINEL_KIND = "sbpr-qa-overlay-lane-sentinel"
MANIFEST_KIND = "sbpr-qa-overlay-manifest"
OVERLAY_PARTS = ("helper", "runner", "contracts", "profile", "scenario", "lane_sentinel")


class LiveModeRefused(Exception):
    """Live execution failed a fail-closed precondition. Carries the exact reason."""


@dataclass(frozen=True)
class LivePreflightResult:
    """The outcome of the live-mode gate. `ok` is True only when EVERY check passed."""

    ok: bool
    reason: Optional[str] = None
    sentinel_lane: Optional[str] = None
    overlay_digest: Optional[str] = None


def _extract_deny_ports(deny: Any) -> set[int]:
    """Pull integer ports out of a production_deny block shaped like the packer's.

    The packer writes {"worlds": ["Niflheim:2456", "Heistan:2466"], ...}; accept
    that shape (and a plain list of "name:port" / ints) and return the port set.
    """
    ports: set[int] = set()
    if isinstance(deny, Mapping):
        worlds = deny.get("worlds", [])
    else:
        worlds = deny
    if not isinstance(worlds, (list, tuple)):
        return ports
    for entry in worlds:
        if isinstance(entry, int):
            ports.add(entry)
        elif isinstance(entry, str) and ":" in entry:
            tail = entry.rsplit(":", 1)[-1].strip()
            if tail.isdigit():
                ports.add(int(tail))
    return ports


def validate_sentinel(sentinel: Mapping[str, Any]) -> str:
    """Validate a disposable-lane sentinel. Returns the lane label or raises.

    Fail-closed: wrong kind, non-disposable lane, or a missing/insufficient hard
    production deny list all refuse.
    """
    if not isinstance(sentinel, Mapping):
        raise LiveModeRefused("lane sentinel is not a JSON object")
    if sentinel.get("kind") != SENTINEL_KIND:
        raise LiveModeRefused(
            f"lane sentinel kind {sentinel.get('kind')!r} != {SENTINEL_KIND!r}"
        )
    lane = sentinel.get("lane")
    if lane != "disposable":
        raise LiveModeRefused(
            f"lane sentinel lane {lane!r} is not 'disposable' — refusing live run"
        )
    deny_ports = _extract_deny_ports(sentinel.get("production_deny"))
    missing = PRODUCTION_PORTS - deny_ports
    if missing:
        raise LiveModeRefused(
            f"lane sentinel is missing hard production deny ports {sorted(missing)} "
            "(both Niflheim 2456 and Heistan 2466 must be denied)"
        )
    return str(lane)


def _fold_digest(parts: Mapping[str, str]) -> str:
    """Reproduce pack-qa-overlay's overlay_digest fold over the six parts."""
    ordered = {p: parts[p] for p in OVERLAY_PARTS}
    return hashlib.sha256(json.dumps(ordered, sort_keys=True).encode()).hexdigest()


def verify_overlay_pins(
    manifest: Mapping[str, Any],
    observed_part_hashes: Mapping[str, str],
) -> str:
    """Verify observed part hashes match the manifest + its folded digest.

    Returns the verified overlay_digest or raises LiveModeRefused on ANY drift:
    a missing part, a per-part hash mismatch, or a folded-digest mismatch.
    """
    if not isinstance(manifest, Mapping) or manifest.get("kind") != MANIFEST_KIND:
        raise LiveModeRefused(
            f"overlay manifest kind {manifest.get('kind')!r} != {MANIFEST_KIND!r}"
        )
    recorded_parts = manifest.get("parts")
    if not isinstance(recorded_parts, Mapping):
        raise LiveModeRefused("overlay manifest has no 'parts' map")

    for part in OVERLAY_PARTS:
        want = recorded_parts.get(part)
        got = observed_part_hashes.get(part)
        if want is None:
            raise LiveModeRefused(f"overlay manifest missing pin for part {part!r}")
        if got is None:
            raise LiveModeRefused(f"no observed hash for overlay part {part!r}")
        if want != got:
            raise LiveModeRefused(
                f"overlay part {part!r} drifted: observed {got} != pinned {want}"
            )

    recomputed = _fold_digest({p: str(recorded_parts[p]) for p in OVERLAY_PARTS})
    recorded_digest = manifest.get("overlay_digest")
    if recorded_digest != recomputed:
        raise LiveModeRefused(
            f"overlay_digest mismatch: manifest {recorded_digest} != recomputed {recomputed}"
        )
    return recomputed


def evaluate_live_preflight(
    *,
    live_requested: bool,
    sentinel: Optional[Mapping[str, Any]],
    manifest: Optional[Mapping[str, Any]],
    observed_part_hashes: Optional[Mapping[str, str]],
) -> LivePreflightResult:
    """The whole fail-closed live gate. Returns ok=False (never raises) with a reason.

    ALL of: live explicitly requested, a valid disposable sentinel, verified overlay
    pins. Any missing input or failed check yields ok=False and the precise reason,
    so the caller refuses live execution with an actionable message.
    """
    if not live_requested:
        return LivePreflightResult(ok=False, reason="live mode not requested (--live absent)")
    if sentinel is None:
        return LivePreflightResult(
            ok=False, reason="live mode requires a disposable-lane sentinel (none supplied)"
        )
    if manifest is None or observed_part_hashes is None:
        return LivePreflightResult(
            ok=False,
            reason="live mode requires a verified overlay manifest + observed pins (missing)",
        )
    try:
        lane = validate_sentinel(sentinel)
        digest = verify_overlay_pins(manifest, observed_part_hashes)
    except LiveModeRefused as exc:
        return LivePreflightResult(ok=False, reason=str(exc))
    return LivePreflightResult(ok=True, sentinel_lane=lane, overlay_digest=digest)
