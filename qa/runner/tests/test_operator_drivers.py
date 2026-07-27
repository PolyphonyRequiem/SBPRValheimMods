"""Operator launch/seed driver guard coverage (ADR-0009 §5, §9) — M6-EXEC.

Proves every operator guard is a HARD fail-closed check: production-port refusal,
explicit-readiness (no blind sleep), refusal to touch a foreign valheim binary,
deterministic teardown on every path, the OFFER→BUY discriminators (never mint),
and byte-identical adminlist restore with a loud mismatch. Injected spawn/deliver/IO
callables mean NOTHING here launches a game or mutates a real file.
"""
from __future__ import annotations

import pytest

from runner_core.operator_drivers import (
    CMD_BUY,
    CMD_OFFER,
    LICENSED_STEAM_IDENTITIES,
    PRODUCTION_PORTS,
    AdminlistGuard,
    ClientSpec,
    DualClientLauncher,
    EntitlementSeeder,
    LaneLauncher,
    LaneSpec,
    OperatorSafetyError,
)


# --------------------------------------------------------------------------- #
# LaneLauncher.
# --------------------------------------------------------------------------- #

def test_lane_refuses_production_ports() -> None:
    for port in PRODUCTION_PORTS:
        spec = LaneSpec(lane_id="t009l", world_name="w", world_uid=1, port=port)
        with pytest.raises(OperatorSafetyError):
            LaneLauncher(lambda s: object(), lambda h: True, lambda h: None).start(spec)


def test_lane_starts_when_ready_signalled() -> None:
    stopped = []
    launcher = LaneLauncher(
        spawn=lambda s: "handle",
        is_ready=lambda h: True,
        stop=lambda h: stopped.append(h),
    )
    proc = launcher.start(LaneSpec("t009l", "w", 1, 3456))
    assert launcher.running and proc.name == "lane:t009l"
    launcher.stop()
    assert stopped == ["handle"] and not launcher.running


def test_lane_never_ready_tears_down_and_fails_closed() -> None:
    stopped = []
    launcher = LaneLauncher(
        spawn=lambda s: "h",
        is_ready=lambda h: False,  # never ready — no blind sleep, bounded polls
        stop=lambda h: stopped.append(h),
        max_ready_polls=3,
    )
    with pytest.raises(OperatorSafetyError):
        launcher.start(LaneSpec("t009l", "w", 1, 3456))
    assert stopped == ["h"]  # torn down on the failure path


def test_lane_double_start_refused() -> None:
    launcher = LaneLauncher(lambda s: "h", lambda h: True, lambda h: None)
    launcher.start(LaneSpec("t009l", "w", 1, 3456))
    with pytest.raises(OperatorSafetyError):
        launcher.start(LaneSpec("t009l", "w", 1, 3457))


# --------------------------------------------------------------------------- #
# DualClientLauncher.
# --------------------------------------------------------------------------- #

def _pair(bin_a="/lane/a/valheim.x86_64", bin_b="/lane/b/valheim.x86_64"):
    return [
        ClientSpec("client_a", LICENSED_STEAM_IDENTITIES[0], bin_a),
        ClientSpec("client_b", LICENSED_STEAM_IDENTITIES[1], bin_b),
    ]


def test_dual_client_launches_licensed_pair() -> None:
    spawned = []
    stopped = []
    launcher = DualClientLauncher(
        spawn=lambda s: spawned.append(s.actor) or f"h-{s.actor}",
        stop=lambda h: stopped.append(h),
        running_binaries=lambda: [],
    )
    procs = launcher.launch(_pair())
    assert len(procs) == 2 and spawned == ["client_a", "client_b"]
    launcher.teardown()
    assert set(stopped) == {"h-client_a", "h-client_b"}


def test_dual_client_wrong_identity_refused() -> None:
    launcher = DualClientLauncher(lambda s: "h", lambda h: None, lambda: [])
    bad = [
        ClientSpec("client_a", "0000", "/lane/a/valheim.x86_64"),
        ClientSpec("client_b", LICENSED_STEAM_IDENTITIES[1], "/lane/b/valheim.x86_64"),
    ]
    with pytest.raises(OperatorSafetyError):
        launcher.launch(bad)


def test_dual_client_wrong_count_refused() -> None:
    launcher = DualClientLauncher(lambda s: "h", lambda h: None, lambda: [])
    with pytest.raises(OperatorSafetyError):
        launcher.launch(_pair()[:1])


def test_dual_client_refuses_foreign_running_binary() -> None:
    # A valheim binary this launcher did NOT start is already running -> refuse.
    launcher = DualClientLauncher(
        spawn=lambda s: "h",
        stop=lambda h: None,
        running_binaries=lambda: ["/lane/a/valheim.x86_64"],
    )
    with pytest.raises(OperatorSafetyError):
        launcher.launch(_pair())


def test_dual_client_partial_launch_tears_down() -> None:
    stopped = []
    calls = {"n": 0}

    def spawn(spec):
        calls["n"] += 1
        if calls["n"] == 2:
            raise RuntimeError("second client crashed on launch")
        return f"h-{spec.actor}"

    launcher = DualClientLauncher(
        spawn=spawn, stop=lambda h: stopped.append(h), running_binaries=lambda: []
    )
    with pytest.raises(RuntimeError):
        launcher.launch(_pair())
    # The first (successfully-started) client was torn down on the failure path.
    assert stopped == ["h-client_a"]


# --------------------------------------------------------------------------- #
# EntitlementSeeder — OFFER->BUY, never mint.
# --------------------------------------------------------------------------- #

def test_seeder_uses_correct_discriminators_in_order() -> None:
    delivered = []
    seeder = EntitlementSeeder(deliver=lambda d: delivered.append(d) or f"line-{d}")
    results = seeder.seed()
    assert [r.discriminator for r in results] == [CMD_OFFER, CMD_BUY]
    assert delivered == [1, 2]  # NOT the retired driver's 0/1
    assert results[0].command == "offer" and results[1].command == "buy"


def test_seeder_offer_and_buy_report_product_line_verbatim() -> None:
    seeder = EntitlementSeeder(deliver=lambda d: f"product-operator-line-{d}")
    assert seeder.offer().operator_line == "product-operator-line-1"
    assert seeder.buy().operator_line == "product-operator-line-2"


def test_discriminators_match_product_constants() -> None:
    # Guards against the QaT022Driver off-by-one (offer=0/buy=1) recurring.
    assert (CMD_OFFER, CMD_BUY) == (1, 2)


# --------------------------------------------------------------------------- #
# AdminlistGuard — byte-identical restore, loud on mismatch.
# --------------------------------------------------------------------------- #

class _FakeFile:
    def __init__(self, data: bytes) -> None:
        self.data = data

    def read(self) -> bytes:
        return self.data

    def write(self, data: bytes) -> None:
        self.data = data


def test_adminlist_capture_and_byte_identical_restore() -> None:
    f = _FakeFile(b"admin1\nadmin2\n")
    guard = AdminlistGuard(read_bytes=f.read, write_bytes=f.write)
    sha = guard.arm()
    f.write(b"admin1\nadmin2\nqa-temp-admin\n")  # a change during the run
    restored = guard.restore()
    assert restored == sha
    assert f.data == b"admin1\nadmin2\n"


def test_adminlist_restore_mismatch_is_loud() -> None:
    f = _FakeFile(b"orig\n")
    guard = AdminlistGuard(read_bytes=f.read, write_bytes=lambda d: None)  # write is a no-op
    guard.arm()
    f.data = b"corrupted\n"  # simulate a failed restore
    with pytest.raises(OperatorSafetyError):
        guard.restore()


def test_adminlist_restore_before_arm_refused() -> None:
    f = _FakeFile(b"x")
    guard = AdminlistGuard(read_bytes=f.read, write_bytes=f.write)
    with pytest.raises(OperatorSafetyError):
        guard.restore()


def test_adminlist_double_arm_refused() -> None:
    f = _FakeFile(b"x")
    guard = AdminlistGuard(read_bytes=f.read, write_bytes=f.write)
    guard.arm()
    with pytest.raises(OperatorSafetyError):
        guard.arm()
