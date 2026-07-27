"""Per-phase timeout budgets layered on the FSM's single global deadline.

ADR-0009 §3.2 requires per-request deadlines ("one primitive, one attempt, a hard
per-action deadline") in addition to the whole-run expiry the FSM already enforces.
The adopted FSM has ONE global deadline; this transport decorator adds a per-send
budget without touching the FSM: it wraps any `Transport`, charges the deterministic
tick cost of each `send` against that verb's budget, and raises `PhaseTimeoutError`
(an FSM `TransportError` subclass, so the FSM treats it as a fail-closed transport
failure — never a soft PASS) when a single primitive exceeds its budget.

Deterministic and engine-free: the "clock" is the wrapped transport's monotonic
tick count, so a test drives timeout purely by scripting tick costs.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, List, Mapping

# Import from the sibling engine-free fsm package. runner_core and fsm both live
# under qa/runner, which conftest / the CLI put on sys.path.
from fsm.errors import TransportError  # noqa: E402
from fsm.schema import ActionRequest  # noqa: E402
from fsm.transport import Transport  # noqa: E402


class PhaseTimeoutError(TransportError):
    """A single primitive exceeded its per-phase deadline budget."""


@dataclass
class PhaseBudget:
    """Per-verb tick budgets. `default` applies to any verb not explicitly listed."""

    default: int
    per_verb: Mapping[str, int] = field(default_factory=dict)

    def budget_for(self, verb: str) -> int:
        return self.per_verb.get(verb, self.default)


class PhaseTimeoutTransport:
    """Wrap a Transport, enforcing a per-send tick budget on each primitive.

    The wrapped transport is the source of truth for the clock (`now()`) and the
    actual send/cleanup behaviour. We measure the tick delta each `send` costs and
    fail closed if a single primitive burned more than its budget.
    """

    def __init__(self, inner: Transport, budget: PhaseBudget) -> None:
        self._inner = inner
        self._budget = budget
        self.charged: Dict[str, int] = {}

    def now(self) -> int:
        return self._inner.now()

    def send(self, request: ActionRequest) -> List[Any]:
        before = self._inner.now()
        result = self._inner.send(request)
        cost = self._inner.now() - before
        self.charged[request.request_id] = cost
        limit = self._budget.budget_for(request.verb)
        if cost > limit:
            raise PhaseTimeoutError(
                f"phase {request.verb!r} (request {request.request_id!r}) took "
                f"{cost} ticks, exceeding its per-phase budget of {limit}"
            )
        return result

    def cleanup(self) -> None:
        self._inner.cleanup()
