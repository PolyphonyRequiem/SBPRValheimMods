"""Typed FSM errors. Every failure vector that must NEVER yield a PASS maps to a
distinct exception so tests can assert the exact fail-closed reason.
"""
from __future__ import annotations


class FsmError(Exception):
    """Base class for all runner state-machine failures."""


class TransportError(FsmError):
    """The transport layer failed to deliver a request or a peer crashed."""


class ReceiptCorrelationError(FsmError):
    """A receipt could not be correlated to its request by the four-part key
    (run nonce / requestId / actor / connection generation): missing,
    reordered, duplicated, stale, or tampered.
    """


class ArtifactPinError(FsmError):
    """The run manifest is missing a required artifact pin, so identity cannot
    be established. Fail-closed before any phase runs.
    """


class ArtifactDriftError(FsmError):
    """An observed artifact hash diverged from the pinned manifest value."""


class IdentityCollisionError(FsmError):
    """Two actors resolved to the same bound principal / connection identity,
    so receipts cannot be attributed unambiguously.
    """


class CompetingLeaseError(FsmError):
    """Another holder owns the exclusive QA lane lease; this run must not act."""


class CleanupError(FsmError):
    """Cleanup did not confirm. Because cleanup is a PASS precondition, this is
    always terminal for the verdict even when the scenario legs passed.
    """
