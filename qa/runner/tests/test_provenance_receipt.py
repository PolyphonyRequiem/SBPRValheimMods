"""M6-PROVENANCE — cross-uid harness provenance receipts.

THE WALL THIS CLOSES. The runner identifies the client it launched by reading that
process's `/proc/<pid>/environ` for a per-boot random marker. The kernel exposes
environ ONLY to the process owner, so this works for client_a (same uid as the
runner) and is STRUCTURALLY IMPOSSIBLE for client_b, which runs as valbot (uid 1001)
to hold a second Steam licence. The runner burned six boot attempts on a permission
error and refused to proceed without a tear-down-able instance, so the TRANSFER and
TAMPER acceptance tests could never execute at all.

The marker is not decoration: it is what the B1 kill guard uses to prove a
`valheim.x86_64` process is harness-owned rather than Daniel's own game before it
sends a signal. So the fix is NOT to drop the check. Instead the valbot controller —
which runs as valbot and knows the PID it just launched — attests a `{marker, pid}`
receipt into the primary-owned mode-0733 diagnostics directory, and the runner
re-derives every safety-relevant fact from the kernel.

These tests pin the SECURITY properties, not just the happy path.
"""

import json
import os
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from runner_core.live_composition import (  # noqa: E402
    PROVENANCE_RECEIPT_DIR,
    probe_pid_via_receipt,
    remove_provenance_receipts,
    resolve_via_receipt,
)
import runner_core.live_composition as lc  # noqa: E402


MARKER = "client_b:78def569d12744c691e3b898eb34bb03"
OTHER_MARKER = "client_b:0000000000000000000000000000dead"


def _write_receipt(directory, marker, pid, *, actor=None, extra=None):
    actor = actor if actor is not None else marker.split(":", 1)[0]
    doc = {"marker": marker, "pid": pid, "exe": "valheim.x86_64"}
    if extra:
        doc.update(extra)
    path = directory / f"harness-provenance-{actor}.json"
    path.write_text(json.dumps(doc))
    return path


@pytest.fixture()
def kernel(monkeypatch):
    """Stub the two world-readable /proc reads the receipt path re-verifies against.

    Stubbing here (rather than at `open`) keeps the tests about the DECISION logic.
    `exe[pid]` absent => unreadable/gone; `ticks[pid]` absent => process gone.
    """
    state = {"exe": {}, "ticks": {}}
    monkeypatch.setattr(lc, "_pid_exe_basename", lambda pid: state["exe"].get(pid))
    monkeypatch.setattr(lc, "_pid_start_ticks", lambda pid: state["ticks"].get(pid))
    return state


# --- The happy path this whole change exists to enable ---------------------------


def test_receipt_resolves_a_cross_uid_client(tmp_path, kernel):
    kernel["exe"][4242] = "valheim.x86_64"
    kernel["ticks"][4242] = 99887766
    _write_receipt(tmp_path, MARKER, 4242)

    inst = resolve_via_receipt(MARKER, str(tmp_path))

    assert inst is not None
    assert inst.pid == 4242
    assert inst.marker == MARKER
    assert inst.actor == "client_b"
    # Start-ticks come from the KERNEL, never from the receipt.
    assert inst.start_ticks == 99887766


def test_probe_by_pid_resolves_the_same_receipt(tmp_path, kernel):
    kernel["exe"][4242] = "valheim.x86_64"
    kernel["ticks"][4242] = 5
    _write_receipt(tmp_path, MARKER, 4242)

    inst = probe_pid_via_receipt(4242, str(tmp_path))

    assert inst is not None and inst.marker == MARKER


# --- Fail-closed properties (the ones that actually matter) ----------------------


def test_missing_receipt_fails_closed(tmp_path, kernel):
    kernel["exe"][4242] = "valheim.x86_64"
    kernel["ticks"][4242] = 5
    assert resolve_via_receipt(MARKER, str(tmp_path)) is None
    assert probe_pid_via_receipt(4242, str(tmp_path)) is None


def test_stale_receipt_from_a_previous_run_is_rejected(tmp_path, kernel):
    # A live, real Valheim process — but the receipt attests a DIFFERENT (previous)
    # boot's marker. Must not resolve for this boot.
    kernel["exe"][4242] = "valheim.x86_64"
    kernel["ticks"][4242] = 5
    _write_receipt(tmp_path, OTHER_MARKER, 4242)

    assert resolve_via_receipt(MARKER, str(tmp_path)) is None


def test_receipt_naming_a_foreign_binary_is_refused(tmp_path, kernel):
    # THE SAFETY CASE: a receipt must never be able to point the kill guard at an
    # arbitrary process. The binary at that PID is re-checked against the kernel.
    kernel["exe"][4242] = "firefox"
    kernel["ticks"][4242] = 5
    _write_receipt(tmp_path, MARKER, 4242)

    assert resolve_via_receipt(MARKER, str(tmp_path)) is None
    assert probe_pid_via_receipt(4242, str(tmp_path)) is None


def test_receipt_naming_a_dead_pid_is_refused(tmp_path, kernel):
    # Process gone => no start-ticks => cannot pin identity => refuse.
    kernel["exe"][4242] = "valheim.x86_64"  # exe link may linger briefly
    _write_receipt(tmp_path, MARKER, 4242)

    assert resolve_via_receipt(MARKER, str(tmp_path)) is None


def test_daniels_own_game_has_no_receipt_and_never_resolves(tmp_path, kernel):
    # Daniel's client is a real valheim.x86_64 with a real start time. The ONLY thing
    # that distinguishes it is the absence of an attested receipt. That must be enough.
    kernel["exe"][31337] = "valheim.x86_64"
    kernel["ticks"][31337] = 4242
    _write_receipt(tmp_path, MARKER, 4242)  # a receipt, but for a DIFFERENT pid
    kernel["exe"][4242] = "valheim.x86_64"
    kernel["ticks"][4242] = 1

    assert probe_pid_via_receipt(31337, str(tmp_path)) is None


def test_malformed_receipts_are_refused(tmp_path, kernel):
    kernel["exe"][4242] = "valheim.x86_64"
    kernel["ticks"][4242] = 5
    p = tmp_path / "harness-provenance-client_b.json"

    p.write_text("{not json")
    assert resolve_via_receipt(MARKER, str(tmp_path)) is None

    p.write_text(json.dumps(["not", "a", "dict"]))
    assert resolve_via_receipt(MARKER, str(tmp_path)) is None

    p.write_text(json.dumps({"marker": MARKER}))  # no pid
    assert resolve_via_receipt(MARKER, str(tmp_path)) is None

    p.write_text(json.dumps({"marker": MARKER, "pid": "not-an-int"}))
    assert resolve_via_receipt(MARKER, str(tmp_path)) is None


def test_receipt_cannot_smuggle_in_start_ticks(tmp_path, kernel):
    # Even if a receipt carries a start_ticks field, it must be IGNORED — the value
    # is always re-derived from the kernel, so PID reuse stays defeated.
    kernel["exe"][4242] = "valheim.x86_64"
    kernel["ticks"][4242] = 777
    _write_receipt(tmp_path, MARKER, 4242, extra={"start_ticks": 999999})

    inst = resolve_via_receipt(MARKER, str(tmp_path))
    assert inst is not None and inst.start_ticks == 777


def test_unreadable_receipt_dir_fails_closed(tmp_path, kernel):
    missing = tmp_path / "does-not-exist"
    assert resolve_via_receipt(MARKER, str(missing)) is None
    assert probe_pid_via_receipt(4242, str(missing)) is None


# --- Teardown hygiene ------------------------------------------------------------


def test_teardown_sweeps_receipts_and_leaves_other_files(tmp_path):
    _write_receipt(tmp_path, MARKER, 1, actor="client_a")
    _write_receipt(tmp_path, MARKER, 2, actor="client_b")
    keep = tmp_path / "valbot-launch.log"
    keep.write_text("diagnostics we must not delete")

    remove_provenance_receipts(str(tmp_path))

    assert not list(tmp_path.glob("harness-provenance-*.json"))
    assert keep.exists(), "teardown must not touch the controller's launch log"


def test_teardown_on_missing_dir_is_a_noop():
    remove_provenance_receipts("/nonexistent/path/for/test")  # must not raise


def test_receipt_dir_is_the_established_cross_user_seam():
    # Must reuse the primary-owned diagnostics directory the valbot controller already
    # writes to — not introduce a new, unreviewed trust path.
    assert PROVENANCE_RECEIPT_DIR.endswith("dual-client/runtime-diagnostics")
