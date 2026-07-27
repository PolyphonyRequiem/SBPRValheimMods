"""Transport interface + deterministic fake.

The FSM talks to helpers only through `Transport`. It is intentionally minimal:
send one request, get zero-or-more raw receipt payloads back, plus a monotonic
`now()` clock and a cleanup hook. The real M1 transport (loopback TCP/JSON +
per-peer ZRpc) implements the same three methods; the FSM never learns the
difference.

`FakeTransport` is fully deterministic and scriptable so tests can inject every
adversarial receipt pattern (missing / reordered / duplicate / stale / tampered)
and every failure (timeout / crash / cleanup failure) without any real I/O.
"""
from __future__ import annotations

from typing import Any, Callable, Dict, List, Optional, Protocol, runtime_checkable

from .errors import CleanupError, TransportError
from .schema import ActionRequest


@runtime_checkable
class Transport(Protocol):
    """Minimal transport seam. No network/file/game types cross this boundary."""

    def now(self) -> int:
        """Monotonic tick clock (deadline arithmetic only, no wall time)."""
        ...

    def send(self, request: ActionRequest) -> List[Any]:
        """Deliver one request; return zero or more raw receipt payloads.

        Raise TransportError to model a peer crash / dropped connection.
        """
        ...

    def cleanup(self) -> None:
        """Tear down fixtures. Raise CleanupError if cleanup did not confirm."""
        ...


class FakeTransport:
    """Scriptable, deterministic Transport for FSM tests.

    Behaviours are registered per `(actor, verb)`:
      * a receipt payload (or list of payloads) to return, or
      * a callable taking the ActionRequest and returning payload(s), or
      * an Exception instance/class to raise (models crash/timeout).

    The clock advances by `tick_per_send` on every send unless a handler
    overrides it, letting tests drive deadline expiry deterministically.
    """

    def __init__(
        self,
        *,
        start_tick: int = 0,
        tick_per_send: int = 1,
        cleanup_raises: Optional[BaseException] = None,
    ) -> None:
        self._tick = start_tick
        self._tick_per_send = tick_per_send
        self._handlers: Dict[tuple[str, str], Any] = {}
        self._cleanup_raises = cleanup_raises
        self.sent: List[ActionRequest] = []
        self.cleanup_called = False

    # -- scripting API -----------------------------------------------------
    def on(self, actor: str, verb: str, behaviour: Any) -> "FakeTransport":
        """Register the response for a given (actor, verb). Chainable."""
        self._handlers[(actor, verb)] = behaviour
        return self

    def advance(self, ticks: int) -> None:
        self._tick += ticks

    # -- Transport protocol ------------------------------------------------
    def now(self) -> int:
        return self._tick

    def send(self, request: ActionRequest) -> List[Any]:
        self.sent.append(request)
        self._tick += self._tick_per_send
        behaviour = self._handlers.get((request.actor, request.verb))
        if behaviour is None:
            raise TransportError(
                f"no scripted behaviour for actor={request.actor!r} verb={request.verb!r}"
            )
        return self._resolve(behaviour, request)

    def cleanup(self) -> None:
        self.cleanup_called = True
        if self._cleanup_raises is not None:
            exc = self._cleanup_raises
            if isinstance(exc, type):
                raise exc("cleanup failed to confirm")
            raise exc

    # -- internals ---------------------------------------------------------
    @staticmethod
    def _resolve(behaviour: Any, request: ActionRequest) -> List[Any]:
        if isinstance(behaviour, BaseException):
            raise behaviour
        if isinstance(behaviour, type) and issubclass(behaviour, BaseException):
            raise behaviour(f"scripted failure on {request.verb}")
        if callable(behaviour):
            result = behaviour(request)
        else:
            result = behaviour
        if result is None:
            return []
        if isinstance(result, list):
            return result
        return [result]
