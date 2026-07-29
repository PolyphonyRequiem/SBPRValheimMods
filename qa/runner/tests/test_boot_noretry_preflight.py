"""M6-NORETRY — the boot loop pre-flights PERMANENT preconditions and refuses to
re-roll structurally-unrecoverable failures.

## The defect this suite pins (run t_e8777cca, 2026-07-28)

The client-boot retry policy (`GabsClientBooter.boot`) re-rolled on EVERY failure,
including failures that no retry could ever recover: an already-EXPIRED bootstrap
credential. The C# arm gate refuses an at/past `expiry` on every attempt
(`RejectReason.Expired`, ArmingGate.cs:110-112), so the helper never binds its
loopback control port — which at the Python layer is indistinguishable from a
transient ValBridge wedge. So the loop booted a ~90-second game client, watched it
join, reached `Connected`, saw the loopback port refuse, and re-rolled — for the full
`max_attempts`, with no attempt able to differ from the last. A full budget consumed
with no verdict.

## What this suite proves

- RED-against-main: given a bootstrap doc with a PAST `expiry`, the boot makes ZERO
  launch attempts and raises a named permanent-failure error. (On pre-fix code this
  fails: the loop consumes all `max_attempts`.)
- A genuinely transient failure still re-rolls the FULL `max_attempts` — a flake was
  not turned into a hard stop.
- An unclassifiable failure (unreadable / unparseable / expiry-less doc) defaults to
  RECOVERABLE — fail-safe on uncertainty, not fail-fast.
- A future-expiry doc pre-flights clean and the boot proceeds normally.

Injected seams only: NO real GABS, NO real socket, NO game, NO sleep, NO real clock/file.
"""
from __future__ import annotations

import json

import pytest

from runner_core.operator_drivers import (
    BootRetryPolicy,
    ClientLaunchError,
    ClientLaunchRequest,
    GabsClientBooter,
    PermanentBootPreconditionError,
    assert_bootstrap_credential_not_expired,
)

from tests.test_gabs_client_boot import _Recorder, _spec, BOOTSTRAP_PATH

NOW_MS = 1_785_000_000_000  # a fixed "now" in unix-ms for deterministic expiry tests


def _bootstrap_doc(expiry_ms):
    """A minimal bootstrap doc carrying just the field the pre-flight inspects."""
    return json.dumps({"enabled": 1, "role": "Client", "expiry": expiry_ms})


def _booter_with_bootstrap(
    rec: _Recorder,
    *,
    doc_text,
    now_ms=NOW_MS,
    policy=None,
):
    """Build a booter wired with the M6-NORETRY pre-flight seams over an in-memory doc.

    `doc_text` may be a string (the doc's bytes), a callable raising to model an
    unreadable doc, or None to model a spec with no readable doc.
    """

    def _read_text(path):
        if callable(doc_text):
            return doc_text(path)
        if doc_text is None:
            raise FileNotFoundError(path)
        return doc_text

    return GabsClientBooter(
        apply_env=rec.apply_env,
        gabs_start=rec.gabs_start,
        control_ready=rec.control_ready,
        resolve_launched=rec.resolve_launched,
        probe_pid=rec._probe_pid_with_hook,
        terminate=rec.terminate,
        sleep=rec.sleep,
        policy=policy or BootRetryPolicy(max_attempts=6, readiness_timeout_s=30.0, poll_interval_s=10.0),
        read_bootstrap_text=_read_text,
        now_unix_ms=lambda: now_ms,
    )


# --------------------------------------------------------------------------- #
# PERMANENT: an expired credential aborts BEFORE any launch (zero attempts).
# THE RED-AGAINST-MAIN REGRESSION.
# --------------------------------------------------------------------------- #

def test_boot_aborts_with_zero_attempts_on_expired_credential() -> None:
    # The doc's expiry is 106 minutes in the past — the exact observed shape.
    past = NOW_MS - 106 * 60 * 1000
    rec = _Recorder(ready_after_calls=9999)  # would never arm; the loop must NEVER run
    booter = _booter_with_bootstrap(rec, doc_text=_bootstrap_doc(past))

    with pytest.raises(PermanentBootPreconditionError) as ei:
        booter.boot(_spec())

    msg = str(ei.value)
    # Named, actionable diagnostic — names the expiry, that it is in the past, and the gate.
    assert "EXPIRED" in msg
    assert BOOTSTRAP_PATH in msg
    assert "RejectReason.Expired" in msg or "past" in msg
    # THE ASSERTION THAT MATTERS: ZERO launch attempts consumed. Nothing was ever started,
    # no env published, no process terminated, no poll slept. The pre-flight ran before
    # the loop, so the whole re-roll budget was preserved (the defect burned all of it).
    assert rec.started == []
    assert rec.env_applied == []
    assert rec.terminated == []
    assert rec.slept == []


def test_boot_aborts_on_expiry_exactly_now() -> None:
    # The gate requires expiry STRICTLY in the future (ExpiryUnixMs <= now => Reject).
    # An expiry equal to now is therefore already dead — permanent, zero attempts.
    rec = _Recorder(ready_after_calls=9999)
    booter = _booter_with_bootstrap(rec, doc_text=_bootstrap_doc(NOW_MS))
    with pytest.raises(PermanentBootPreconditionError):
        booter.boot(_spec())
    assert rec.started == []


# --------------------------------------------------------------------------- #
# RECOVERABLE: a genuinely transient failure still re-rolls the FULL budget.
# Proves the pre-flight did not turn a flake into a hard stop.
# --------------------------------------------------------------------------- #

def test_transient_wedge_still_rerolls_full_budget_with_valid_credential() -> None:
    # A FUTURE-expiry credential pre-flights clean, so the boot proceeds; the loopback
    # port never accepts (a genuine ValBridge wedge) => it must re-roll the FULL budget
    # and fail closed with the ordinary readiness diagnostic, NOT a permanent abort.
    future = NOW_MS + 60 * 60 * 1000  # 1h in the future
    policy = BootRetryPolicy(max_attempts=4, readiness_timeout_s=10.0, poll_interval_s=10.0)
    rec = _Recorder(ready_after_calls=9999)  # never arms => wedge
    booter = _booter_with_bootstrap(rec, doc_text=_bootstrap_doc(future), policy=policy)

    with pytest.raises(ClientLaunchError) as ei:
        booter.boot(_spec())
    # It is the RECOVERABLE diagnostic, not the permanent one.
    assert not isinstance(ei.value, PermanentBootPreconditionError)
    assert "never reached armed readiness" in str(ei.value)
    # Re-rolled the FULL attempt budget — the transient path is untouched by the pre-flight.
    assert len(rec.started) == policy.max_attempts


# --------------------------------------------------------------------------- #
# UNCLASSIFIABLE => RECOVERABLE (fail-safe on uncertainty, not fail-fast).
# --------------------------------------------------------------------------- #

def test_unreadable_doc_defaults_to_recoverable() -> None:
    # The doc cannot be read (FileNotFoundError) => the credential's validity is UNKNOWN.
    # The pre-flight must NOT abort; the normal boot path runs (and here arms on attempt 1).
    rec = _Recorder(ready_after_calls=1)
    booter = _booter_with_bootstrap(rec, doc_text=None)  # read raises
    request = booter.boot(_spec())
    assert isinstance(request, ClientLaunchRequest)
    assert len(rec.started) == 1  # it proceeded normally, did not hard-stop


def test_unparseable_doc_defaults_to_recoverable() -> None:
    # The doc is not JSON => unknown => fail-safe: proceed with the normal boot.
    rec = _Recorder(ready_after_calls=1)
    booter = _booter_with_bootstrap(rec, doc_text="}{ not json")
    request = booter.boot(_spec())
    assert isinstance(request, ClientLaunchRequest)
    assert len(rec.started) == 1


def test_doc_without_expiry_defaults_to_recoverable() -> None:
    # A JSON doc that carries no `expiry` field => unknown => fail-safe.
    rec = _Recorder(ready_after_calls=1)
    booter = _booter_with_bootstrap(rec, doc_text=json.dumps({"enabled": 1, "role": "Client"}))
    request = booter.boot(_spec())
    assert isinstance(request, ClientLaunchRequest)
    assert len(rec.started) == 1


def test_doc_with_noninteger_expiry_defaults_to_recoverable() -> None:
    # A non-integer `expiry` cannot be compared => unknown => fail-safe.
    rec = _Recorder(ready_after_calls=1)
    booter = _booter_with_bootstrap(rec, doc_text=json.dumps({"expiry": "not-a-number"}))
    request = booter.boot(_spec())
    assert isinstance(request, ClientLaunchRequest)
    assert len(rec.started) == 1


def test_future_expiry_proceeds_normally() -> None:
    # The happy path: a valid future-expiry credential pre-flights clean and the boot
    # arms on the first attempt exactly as before.
    future = NOW_MS + 30 * 60 * 1000
    rec = _Recorder(ready_after_calls=1)
    booter = _booter_with_bootstrap(rec, doc_text=_bootstrap_doc(future))
    request = booter.boot(_spec())
    assert isinstance(request, ClientLaunchRequest)
    assert len(rec.started) == 1


# --------------------------------------------------------------------------- #
# Legacy/unit callers: the pre-flight is a no-op when its seams are not wired,
# so every existing GabsClientBooter test keeps its behaviour unchanged.
# --------------------------------------------------------------------------- #

def test_preflight_is_noop_when_seams_absent() -> None:
    # A booter constructed WITHOUT the pre-flight seams (the legacy shape) never touches
    # the credential — even a past-expiry doc on disk is irrelevant because nothing reads
    # it. This is what keeps the whole existing suite green.
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()  # no read_bootstrap_text / now_unix_ms
    request = booter.boot(_spec())
    assert isinstance(request, ClientLaunchRequest)
    assert len(rec.started) == 1


# --------------------------------------------------------------------------- #
# The pre-flight helper is independently unit-tested (pure, no booter).
# --------------------------------------------------------------------------- #

def test_assert_helper_raises_on_past_expiry() -> None:
    with pytest.raises(PermanentBootPreconditionError):
        assert_bootstrap_credential_not_expired(
            "/run/sbpr-qa/boot.json",
            read_text=lambda p: _bootstrap_doc(NOW_MS - 1),
            now_unix_ms=lambda: NOW_MS,
        )


def test_assert_helper_silent_on_future_expiry() -> None:
    # Returns None (no raise) for a valid future credential.
    assert (
        assert_bootstrap_credential_not_expired(
            "/run/sbpr-qa/boot.json",
            read_text=lambda p: _bootstrap_doc(NOW_MS + 1),
            now_unix_ms=lambda: NOW_MS,
        )
        is None
    )


def test_assert_helper_fail_safe_on_read_error() -> None:
    # Unreadable => no raise (fail-safe). The C# gate remains the authoritative check.
    def _boom(path):
        raise OSError("cannot read")

    assert (
        assert_bootstrap_credential_not_expired(
            "/run/sbpr-qa/boot.json",
            read_text=_boom,
            now_unix_ms=lambda: NOW_MS,
        )
        is None
    )
