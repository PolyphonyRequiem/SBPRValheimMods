"""Immutable 6-part artifact-pin manifest (ADR-0009 §5.1, §8).

The run pins an immutable manifest of six byte-state hashes —
**product / helper / game / BepInEx / Harmony / scenario** — and refuses to arm
if any is missing or drifts. This is the engine-free runner-side model of that
manifest. It:

  * enforces the exact six required parts are present (missing → RunManifestError),
  * rejects non-hex / wrong-length sha256 pins (malformed → RunManifestError),
  * detects drift against an observed hash set (drift → PinDriftError),
  * lowers into the FSM `Manifest.artifacts` map so the FSM's own
    `verify_pins()` / `ArtifactDriftError` machinery covers the same pins.

Everything is deterministic and pure — no file reads, no hashing of real bytes.
The caller supplies the pin hex strings (in dry-run, deterministic fixtures).
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Dict, Mapping

# The six manifest parts ADR-0009 §5.1 requires, in fixed order.
REQUIRED_PARTS = ("product", "helper", "game", "bepinex", "harmony", "scenario")

_SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


class RunManifestError(ValueError):
    """The pin manifest is structurally invalid (missing part or malformed hash)."""


class PinDriftError(RuntimeError):
    """An observed artifact hash diverged from its immutable pin."""


@dataclass(frozen=True)
class ArtifactPinManifest:
    """Immutable pin set. `pins` maps each of the six parts to a sha256 hex string."""

    pins: Mapping[str, str]

    def __post_init__(self) -> None:
        missing = [p for p in REQUIRED_PARTS if p not in self.pins]
        if missing:
            raise RunManifestError(
                f"manifest missing required pin part(s): {missing} "
                f"(need all of {list(REQUIRED_PARTS)})"
            )
        extra = [p for p in self.pins if p not in REQUIRED_PARTS]
        if extra:
            raise RunManifestError(
                f"manifest has unexpected pin part(s): {extra} "
                f"(allowed exactly {list(REQUIRED_PARTS)})"
            )
        for part, h in self.pins.items():
            if not isinstance(h, str) or not _SHA256_RE.match(h):
                raise RunManifestError(
                    f"pin {part!r} is not a lowercase 64-hex sha256: {h!r}"
                )

    def verify_no_drift(self, observed: Mapping[str, str]) -> None:
        """Fail closed if any observed part hash differs from its pin.

        Observed parts not present are treated as un-observed (no assertion), but
        any part that IS observed must match its pin exactly. An observed part that
        is not one of the six pinned parts is itself drift (unexpected artifact).
        """
        for part, got in observed.items():
            if part not in self.pins:
                raise PinDriftError(
                    f"observed unexpected artifact {part!r} not in the pin manifest"
                )
            want = self.pins[part]
            if got != want:
                raise PinDriftError(
                    f"artifact {part!r} drifted: observed {got} != pinned {want}"
                )

    def as_fsm_artifacts(self) -> Dict[str, str]:
        """Lower to the FSM `Manifest.artifacts` mapping (name -> sha256)."""
        return dict(self.pins)
