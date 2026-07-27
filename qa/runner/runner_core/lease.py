"""Exclusive QA-lane lease (ADR-0009 §5.3, §5.4).

Only one T022 run may drive the disposable QA lane at a time. The lease is an
engine-free, in-process, deterministic construct: it does NOT touch the filesystem
or any OS lock (this is dry-run-only). It models the *contract* the real M6 lane
lease will honor — acquire-once, non-reentrant, explicit release, and a sentinel
identity that the runner threads into the FSM `RunContext` so the FSM's own
competing-lease check (`CompetingLeaseError`) fires when the lease is not held.

The lease deliberately fails closed:

  * acquiring an already-held lease raises `LaneLeaseError` (no silent takeover),
  * a competing holder id makes `held_by_us` false, so the FSM refuses to act,
  * releasing an unheld lease is a no-op (idempotent teardown, cleanup-safe).
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional


class LaneLeaseError(RuntimeError):
    """Raised when a lane lease cannot be acquired (already held by someone)."""


@dataclass
class LaneLease:
    """A single exclusive claim on the QA lane.

    `lane_id` names the disposable lane; `our_id` is this run's sentinel identity.
    `current_holder` is whoever the shared lane records as holding it right now —
    in dry-run this is injected so tests can model a competing holder.
    """

    lane_id: str
    our_id: str
    current_holder: Optional[str] = None
    _acquired: bool = field(default=False, init=False, repr=False)

    def acquire(self) -> "LaneLease":
        """Take the lease. Fail closed if a *different* holder already owns it."""
        if self.current_holder is not None and self.current_holder != self.our_id:
            raise LaneLeaseError(
                f"lane {self.lane_id!r} already held by {self.current_holder!r}; "
                f"we are {self.our_id!r} — refusing to take over"
            )
        self.current_holder = self.our_id
        self._acquired = True
        return self

    @property
    def held_by_us(self) -> bool:
        return self._acquired and self.current_holder == self.our_id

    @property
    def effective_holder(self) -> str:
        """Who the FSM should see as the current holder.

        When we hold it, this is our id (FSM proceeds). When a competitor holds it
        (or nobody does and we never acquired), this is whatever the lane records —
        which will NOT equal our_id, so the FSM's CompetingLeaseError fires.
        """
        if self.current_holder is None:
            return "<unheld>"
        return self.current_holder

    def release(self) -> None:
        """Release the lease. Idempotent — safe to call from cleanup on any path."""
        if self.held_by_us:
            self.current_holder = None
        self._acquired = False
