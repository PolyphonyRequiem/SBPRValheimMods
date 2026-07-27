"""Compact, deterministic run result + JSON serialization."""
from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from typing import Any, Dict, List, Mapping


@dataclass(frozen=True)
class RunResult:
    """The single object the runner emits. `verdict` is "PASS" only when every
    required leg asserted AND cleanup confirmed. Everything else is "FAIL".

    This is compact by design — it is the artifact the human/architect turns
    into a QA evidence doc.
    """

    verdict: str                                   # "PASS" | "FAIL"
    run_nonce: str
    phases: List[str] = field(default_factory=list)          # phases reached
    legs: Mapping[str, str] = field(default_factory=dict)     # AT -> "pass"/"fail"/"skipped"
    cleanup_confirmed: bool = False
    failure_reason: str | None = None              # first fatal reason, if FAIL
    failure_kind: str | None = None                # exception class name, if FAIL
    evidence_preserved: bool = False               # true when a failure was preserved
    receipts_correlated: int = 0

    @property
    def passed(self) -> bool:
        return self.verdict == "PASS"

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)

    def to_json(self) -> str:
        # sort_keys => byte-stable output for hashing / golden comparison.
        return json.dumps(self.to_dict(), sort_keys=True, separators=(",", ":"))
