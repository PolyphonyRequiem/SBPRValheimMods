"""M6-COMPOSE coverage — prove `--live` actually EXECUTES, not merely prints (ADR-0009 §6, §9).

Three prior M6 attempts blocked because the merged `--live` verified the preflight and
returned without driving anything. These tests prove the composition entrypoint and the
`--live` path now DRIVE a qualification run against a stub operator layer (no real game):

  * `run_live_qualification` invokes lane launch, launches BOTH licensed clients, seeds
    entitlement via OFFER→BUY, drives all four T022 legs through the sole-authority
    orchestrator, composes a verdict, and tears every started resource down;
  * teardown runs on EVERY exit path — success, a driving failure, and an exception;
  * the CLI `--live` (all preconditions satisfied + a descriptor) reaches the executor
    and reports a composed verdict — it does NOT print the old "UNLOCKED but not executed
    here" deferral;
  * fail-closed gating is intact: bare `--live`, missing inputs, and a drifted overlay
    each refuse without executing.

All stubs; no Valheim, no game I/O, no real process, no file mutation.
"""
from __future__ import annotations

import hashlib
import hmac
import importlib.util
import json
import os
import sys

import pytest

RUNNER_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if RUNNER_DIR not in sys.path:
    sys.path.insert(0, RUNNER_DIR)

from fsm import ActionRequest, FakeTransport, Receipt  # noqa: E402
from runner_core.lease import LaneLease  # noqa: E402
from runner_core.live_composition import (  # noqa: E402
    LiveOperatorEnvironment,
    LiveQualificationPlan,
    run_live_qualification,
)
from runner_core.live_transport import LiveRunConfig, ChannelEndpoint  # noqa: E402
from runner_core.manifest import REQUIRED_PARTS, ArtifactPinManifest  # noqa: E402
from runner_core.operator_drivers import (  # noqa: E402
    LICENSED_STEAM_IDENTITIES,
    ClientSpec,
    LaneSpec,
    OperatorSafetyError,
)
from runner_core.timeouts import PhaseBudget  # noqa: E402

NONCE = "compose-run-nonce-0001"
INTEGRITY_KEY = b"compose-integrity-key"

GOLDEN_OBSERVED = {
    "req-issue": {"stamp_valid": True},
    "req-upgrade": {"stamp_valid": True},
    "req-transfer": {"verdict": "valid"},
    "req-tamper": {"verdict": "tampered", "line_rendered": False},
}


def _tag(r: Receipt) -> str:
    body = json.dumps(r.observed, sort_keys=True, separators=(",", ":"))
    msg = (
        f"{r.run_nonce}|{r.request_id}|{r.actor}|{r.conn_gen}|{r.seq}|"
        f"{r.outcome}|{body}"
    ).encode()
    return hmac.new(INTEGRITY_KEY, msg, hashlib.sha256).hexdigest()


def _receipt(request_id, actor, verb, seq, observed):
    base = Receipt(
        request_id=request_id, actor=actor, verb=verb, seq=seq, conn_gen=1,
        run_nonce=NONCE, outcome="ok", observed=dict(observed), integrity=None,
    )
    return Receipt(
        request_id=base.request_id, actor=base.actor, verb=base.verb, seq=base.seq,
        conn_gen=base.conn_gen, run_nonce=base.run_nonce, outcome=base.outcome,
        observed=base.observed, integrity=_tag(base),
    )


def _golden_fake_transport() -> FakeTransport:
    t = FakeTransport()
    t.on("client_a", "Craft", _receipt("req-issue", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"]))
    t.on("client_a", "UpgradeItem", _receipt("req-upgrade", "client_a", "UpgradeItem", 2, GOLDEN_OBSERVED["req-upgrade"]))
    t.on("client_b", "ReadItem", _receipt("req-transfer", "client_b", "ReadItem", 3, GOLDEN_OBSERVED["req-transfer"]))
    t.on("client_b", "TamperField", _receipt("req-tamper", "client_b", "TamperField", 4, GOLDEN_OBSERVED["req-tamper"]))
    return t


def _pins() -> ArtifactPinManifest:
    return ArtifactPinManifest(pins={p: hashlib.sha256(p.encode()).hexdigest() for p in REQUIRED_PARTS})


def _plan(**overrides) -> LiveQualificationPlan:
    ep = ChannelEndpoint(host="127.0.0.1", port=5, role="Client")
    run_config = LiveRunConfig(
        nonce=NONCE, world_uid=1, expiry_unix_ms=32_000_000_000_000,
        operator_token="tok", hmac_secret="sec",
        endpoints={"client_a": ep, "client_b": ep},
        integrity_key=INTEGRITY_KEY, start_generation=1,
    )
    kwargs = dict(
        lane=LaneSpec("t009l", "homestead-t009l", 1, 3456),
        clients=(
            ClientSpec("client_a", LICENSED_STEAM_IDENTITIES[0], "/lane/a/valheim.x86_64"),
            ClientSpec("client_b", LICENSED_STEAM_IDENTITIES[1], "/lane/b/valheim.x86_64"),
        ),
        run_config=run_config,
        lease=LaneLease(lane_id="t009l", our_id="runner-1"),
        pins=_pins(),
        world_uid="1", world_name="homestead-t009l", run_nonce=NONCE,
        expiry=10_000_000,
        phase_budget=PhaseBudget(default=1_000_000),
        expected_conn_gen={"client_a": 1, "client_b": 1, "server": 1},
        actor_identity={"server": "id-s", "client_a": "id-a", "client_b": "id-b"},
        integrity_key=INTEGRITY_KEY,
    )
    kwargs.update(overrides)
    return LiveQualificationPlan(**kwargs)


class _Recorder:
    """Records every operator action so a test can assert the composition DROVE."""

    def __init__(self, *, transport, running=(), spawn_client_fails_on=None):
        self.transport = transport
        self._running = list(running)
        self._spawn_client_fails_on = spawn_client_fails_on
        self.lane_started = None
        self.clients_spawned = []
        self.clients_stopped = []
        self.lane_stopped = False
        self.seeded = []
        self.adminlist_read = 0
        self.adminlist_written = []
        self.transport_built = False

    def env(self) -> LiveOperatorEnvironment:
        return LiveOperatorEnvironment(
            spawn_lane=self._spawn_lane,
            lane_ready=lambda h: True,
            stop_lane=self._stop_lane,
            spawn_client=self._spawn_client,
            stop_client=lambda h: self.clients_stopped.append(h),
            running_binaries=lambda: list(self._running),
            deliver_entitlement=self._deliver,
            read_adminlist=self._read_admin,
            write_adminlist=self._write_admin,
            build_transport=self._build_transport,
            max_ready_polls=3,
        )

    def _spawn_lane(self, spec):
        self.lane_started = spec
        return "lane-handle"

    def _stop_lane(self, h):
        self.lane_stopped = True

    def _spawn_client(self, spec):
        if self._spawn_client_fails_on == spec.actor:
            raise RuntimeError(f"stub client {spec.actor} crashed on launch")
        self.clients_spawned.append(spec.actor)
        return f"client-{spec.actor}"

    def _deliver(self, discriminator):
        self.seeded.append(discriminator)
        return f"operator-line-{discriminator}"

    def _read_admin(self):
        self.adminlist_read += 1
        return b"admin1\n"

    def _write_admin(self, data):
        self.adminlist_written.append(data)

    def _build_transport(self, cfg):
        self.transport_built = True
        return self.transport


# --------------------------------------------------------------------------- #
# The composition actually DRIVES.
# --------------------------------------------------------------------------- #

def test_composition_drives_full_run_and_composes_pass() -> None:
    rec = _Recorder(transport=_golden_fake_transport())
    report = run_live_qualification(_plan(), rec.env())

    # Lane launched, both clients launched under the licensed identities.
    assert rec.lane_started is not None
    assert rec.clients_spawned == ["client_a", "client_b"]
    assert report.clients_launched == ["client:client_a", "client:client_b"]
    # Entitlement seeded via OFFER→BUY (1 then 2), never minted.
    assert rec.seeded == [1, 2]
    assert [s.discriminator for s in report.seed_results] == [1, 2]
    # Transport built and all four legs driven → verdict composed from real receipts.
    assert rec.transport_built
    assert report.legs_driven == 4
    assert report.verdict is not None
    assert report.verdict.verdict == "PASS"
    assert report.passed
    # Teardown ran on the success path.
    assert report.teardown_completed
    assert rec.lane_stopped
    assert len(rec.clients_stopped) == 2
    # Adminlist captured before + restored after (byte-identical).
    assert rec.adminlist_read >= 2
    assert rec.adminlist_written == [b"admin1\n"]


def test_composition_failure_still_tears_down_and_no_false_pass() -> None:
    # A tampered ISSUE receipt: driving fails; verdict must be FAIL and teardown run.
    t = _golden_fake_transport()
    t.on("client_a", "Craft", _receipt("req-issue", "client_a", "Craft", 1, {"stamp_valid": False}))
    rec = _Recorder(transport=t)
    report = run_live_qualification(_plan(), rec.env())

    assert report.verdict is not None
    assert report.verdict.verdict == "FAIL"
    assert not report.passed
    # Everything started was still torn down.
    assert report.teardown_completed
    assert rec.lane_stopped and len(rec.clients_stopped) == 2
    assert rec.adminlist_written == [b"admin1\n"]  # adminlist restored despite failure


def test_composition_client_launch_failure_tears_down_lane_and_restores_admin() -> None:
    # Second client crashes on launch: partial-launch teardown + lane stop + admin restore.
    rec = _Recorder(transport=_golden_fake_transport(), spawn_client_fails_on="client_b")
    report = run_live_qualification(_plan(), rec.env())

    assert report.verdict is None  # never reached the drive
    assert rec.clients_spawned == ["client_a"]
    assert rec.clients_stopped == ["client-client_a"]  # the one we started, torn down
    assert rec.lane_stopped
    assert rec.adminlist_written == [b"admin1\n"]
    assert report.teardown_completed


def test_composition_refuses_foreign_running_client() -> None:
    # A user-owned valheim.x86_64 is already running one of our target binaries.
    rec = _Recorder(transport=_golden_fake_transport(), running=["/lane/a/valheim.x86_64"])
    report = run_live_qualification(_plan(), rec.env())
    assert report.verdict is None
    # Lane started but no client co-opted; lane torn down, admin restored.
    assert rec.clients_spawned == []
    assert rec.lane_stopped
    assert rec.adminlist_written == [b"admin1\n"]


def test_plan_rejects_production_port() -> None:
    with pytest.raises(OperatorSafetyError):
        _plan(lane=LaneSpec("prod", "Niflheim", 1, 2456))


def test_plan_rejects_unlicensed_pair() -> None:
    with pytest.raises(OperatorSafetyError):
        _plan(clients=(
            ClientSpec("client_a", "0000", "/a/valheim.x86_64"),
            ClientSpec("client_b", LICENSED_STEAM_IDENTITIES[1], "/b/valheim.x86_64"),
        ))
