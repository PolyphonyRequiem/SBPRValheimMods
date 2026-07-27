"""Final evidence-document composition (ADR-0009 §6).

The runner correlates the FSM `RunResult` plus the run's operational envelope
(lease identity, artifact pins, phase timing) into ONE deterministic evidence
document — the single artifact a human/architect turns into a QA evidence doc.

This is descriptive only. It does NOT decide the verdict on its own; the
orchestrator composes the verdict and stamps it here. The document is byte-stable
(sorted keys) so it can be hashed / golden-compared, and it carries an explicit
`maturity` banner asserting the run was DRY-RUN / SIMULATED — never live.
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass, field
from typing import Any, Dict, List, Mapping

# ADR-0009 §10: the four named T022 acceptance-test legs.
REQUIRED_LEGS = ("ISSUE", "UPGRADE", "TRANSFER", "TAMPER")

DRY_RUN_MATURITY = (
    "DRY-RUN / SIMULATED — no game launch, no network, no file mutation, nothing "
    "executed in-world. This is NOT a live qualification; the four T022 acceptance "
    "tests have NOT been observed in-world (that is the separate operator M6 card)."
)


@dataclass(frozen=True)
class EvidenceDocument:
    """The single correlated evidence artifact the runner emits."""

    verdict: str                          # "PASS" | "FAIL" — stamped by the orchestrator
    run_nonce: str
    lane_id: str
    lease_holder: str
    phases: List[str] = field(default_factory=list)
    legs: Mapping[str, str] = field(default_factory=dict)
    cleanup_confirmed: bool = False
    lease_held: bool = False
    pins_verified: bool = False
    receipts_correlated: int = 0
    phase_costs: Mapping[str, int] = field(default_factory=dict)
    failure_reason: str | None = None
    failure_kind: str | None = None
    evidence_preserved: bool = False
    maturity: str = DRY_RUN_MATURITY

    def to_dict(self) -> Dict[str, Any]:
        return {
            "verdict": self.verdict,
            "run_nonce": self.run_nonce,
            "lane_id": self.lane_id,
            "lease_holder": self.lease_holder,
            "phases": list(self.phases),
            "legs": dict(self.legs),
            "cleanup_confirmed": self.cleanup_confirmed,
            "lease_held": self.lease_held,
            "pins_verified": self.pins_verified,
            "receipts_correlated": self.receipts_correlated,
            "phase_costs": dict(self.phase_costs),
            "failure_reason": self.failure_reason,
            "failure_kind": self.failure_kind,
            "evidence_preserved": self.evidence_preserved,
            "maturity": self.maturity,
        }

    def to_json(self) -> str:
        # sort_keys => byte-stable output for hashing / golden comparison.
        return json.dumps(self.to_dict(), sort_keys=True, separators=(",", ":"))

    def digest(self) -> str:
        return hashlib.sha256(self.to_json().encode()).hexdigest()

    @property
    def passed(self) -> bool:
        return self.verdict == "PASS"
