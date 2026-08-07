"""M6-MINT coverage — the crypto envelope is minted per run and NEVER persisted.

The live defect (`t_e8777cca`, 2026-07-28): `wire.expiry_unix_ms` was a persisted,
hand-provisioned value 106 minutes in the past, so the helper correctly refused to
arm and zero AT legs were reachable. These tests prove the runner now MINTS the
envelope per run (fresh CSPRNG secrets, `now + TTL` expiry), passes ONE identical
envelope to both consumers (bootstrap doc + live transport), and REFUSES a descriptor
that still carries a persisted secret-bearing wire field.

Engine-free; no game, no socket, no file mutation beyond a tmp bootstrap doc.
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import time

import pytest

RUNNER_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if RUNNER_DIR not in sys.path:
    sys.path.insert(0, RUNNER_DIR)

from runner_core.live_composition import build_live_run  # noqa: E402
from runner_core.lane_password_mint import LanePasswordPersistedError  # noqa: E402
from runner_core.manifest import REQUIRED_PARTS  # noqa: E402
from runner_core.operator_drivers import LICENSED_STEAM_IDENTITIES  # noqa: E402
from runner_core.wire_mint import (  # noqa: E402
    SECRET_WIRE_FIELDS,
    WireSecretPersistedError,
    assert_descriptor_carries_no_wire_secrets,
    mint_wire_envelope,
    resolve_ttl_seconds,
)


# --------------------------------------------------------------------------- #
# mint_wire_envelope: fresh CSPRNG secrets, future expiry.
# --------------------------------------------------------------------------- #

def test_mint_produces_fresh_future_envelope() -> None:
    now_ms = int(time.time() * 1000)
    env = mint_wire_envelope(3600, now_ms=now_ms)
    assert set(env) == set(SECRET_WIRE_FIELDS)
    # Expiry strictly in the future at mint time.
    assert env["expiry_unix_ms"] > now_ms
    assert env["expiry_unix_ms"] == now_ms + 3600 * 1000
    # Tokens match the token_urlsafe(32) encoding/length (43-char base64url).
    for field in ("nonce", "hmac_secret", "operator_token"):
        assert len(env[field]) == 43
        assert all(c.isalnum() or c in "-_" for c in env[field])


def test_mint_rerolls_secrets_each_call() -> None:
    a = mint_wire_envelope(3600)
    b = mint_wire_envelope(3600)
    for field in ("nonce", "hmac_secret", "operator_token"):
        assert a[field] != b[field], f"{field} must be re-rolled per run"


def test_mint_rejects_nonpositive_ttl() -> None:
    with pytest.raises(ValueError):
        mint_wire_envelope(0)
    with pytest.raises(ValueError):
        mint_wire_envelope(-5)


# --------------------------------------------------------------------------- #
# assert_descriptor_carries_no_wire_secrets: fail closed, naming the field.
# --------------------------------------------------------------------------- #

@pytest.mark.parametrize("field", SECRET_WIRE_FIELDS)
def test_persisted_secret_field_is_refused_by_name(field) -> None:
    wire = {"world_uid": 1, "endpoints": {}, field: "leftover"}
    with pytest.raises(WireSecretPersistedError) as ei:
        assert_descriptor_carries_no_wire_secrets(wire)
    assert field in str(ei.value)


def test_topology_only_wire_passes() -> None:
    wire = {"world_uid": 1, "endpoints": {}, "start_generation": 1, "ttl_seconds": 900}
    # Must not raise — topology-only is the post-fix descriptor shape.
    assert_descriptor_carries_no_wire_secrets(wire)


# --------------------------------------------------------------------------- #
# resolve_ttl_seconds: derived from the runner's own boot budget, floored.
# --------------------------------------------------------------------------- #

def test_ttl_defaults_from_boot_budget_and_floor() -> None:
    # 6 attempts * 150s * 2 clients * 2.0 headroom = 3600 -> equals the floor.
    d = {"clients": [1, 2], "server": {}}
    assert resolve_ttl_seconds(d) == pytest.approx(3600.0)


def test_ttl_scales_up_with_larger_boot_budget() -> None:
    d = {"clients": [1, 2], "server": {"boot_policy": {"max_attempts": 6, "readiness_timeout_s": 300}}}
    # 6 * 300 * 2 * 2.0 = 7200 > floor.
    assert resolve_ttl_seconds(d) == pytest.approx(7200.0)


def test_ttl_explicit_override_wins() -> None:
    d = {"wire": {"ttl_seconds": 42}, "clients": [1, 2], "server": {}}
    assert resolve_ttl_seconds(d) == 42.0


# --------------------------------------------------------------------------- #
# build_live_run integration: mint once, ONE envelope to both consumers.
# --------------------------------------------------------------------------- #

def _live_descriptor(tmp_path, *, wire_extra=None):
    boot_a = str(tmp_path / "boot-a.json")
    boot_b = str(tmp_path / "boot-b.json")
    wire = {
        "world_uid": 424242,
        "endpoints": {
            "client_a": {"host": "127.0.0.1", "port": 5, "role": "Client"},
            "client_b": {"host": "127.0.0.1", "port": 6, "role": "Client"},
        },
        "entitlement": {"host": "127.0.0.1", "port": 7, "role": "Client"},
    }
    if wire_extra:
        wire.update(wire_extra)
    return {
        "integrity_key": "mint-integrity",
        "world_uid": "424242",
        "world_name": "homestead-mint",
        "expiry": 10_000_000,
        "lane": {"lane_id": "mintl", "world_name": "homestead-mint", "world_uid": 424242, "port": 2476},
        "clients": [
            {
                "actor": "client_a", "steam_id": LICENSED_STEAM_IDENTITIES[0],
                "uid": os.geteuid(),
                "binary_path": "/lane/a/valheim.x86_64",
                "gabs_endpoint": "http://localhost:8080/mcp", "game_id": "valheim",
                "bootstrap_path": boot_a,
                "connect_host": "127.0.0.1", "connect_port": 2476, "loopback_port": 48610,
                "role": "Client", "verbs": "Craft,Ping,Cleanup,Disarm",
                "launch_env_path": str(tmp_path / "a.env"),
                "qa_profile": "sbpr_qa_join",
            },
            {
                "actor": "client_b", "steam_id": LICENSED_STEAM_IDENTITIES[1],
                "uid": os.geteuid(),
                "binary_path": "/lane/b/valheim.x86_64",
                "gabs_endpoint": "http://localhost:8081/mcp", "game_id": "valheim",
                "bootstrap_path": boot_b,
                "connect_host": "127.0.0.1", "connect_port": 2476, "loopback_port": 48611,
                "role": "Client", "verbs": "Craft,Ping,Cleanup,Disarm",
                "launch_env_path": str(tmp_path / "b.env"),
                "qa_profile": "sbpr_qa_join",
            },
        ],
        "wire": wire,
        "lease": {"lane_id": "mintl", "our_id": "runner-1"},
        "pins": {p: hashlib.sha256(p.encode()).hexdigest() for p in REQUIRED_PARTS},
        "expected_conn_gen": {"client_a": 1, "client_b": 1, "server": 1},
        "actor_identity": {"server": "id-s", "client_a": "id-a", "client_b": "id-b"},
        "server": {
            "server_binary": "/lane/valheim_server.x86_64", "server_args": [],
            "server_ready_log": "/lane/server.log", "server_ready_marker": "Game server connected",
            "client_binary": "/lane/valheim.x86_64", "adminlist_path": "/lane/adminlist.txt",
        },
    }


def test_build_live_run_mints_expiry_in_the_future_and_shares_one_envelope(tmp_path) -> None:
    """The minted expiry is strictly in the future at arm time, and the bootstrap
    doc and the LiveRunConfig receive the SAME envelope. Divergence fails here."""
    descriptor = _live_descriptor(tmp_path)
    before_ms = int(time.time() * 1000)
    plan, env = build_live_run(descriptor)

    rc = plan.run_config
    # Minted expiry strictly in the future at compose (arm) time.
    assert rc.expiry_unix_ms > before_ms

    # Provision the bootstrap docs from the SAME (minted) descriptor the env carries,
    # then assert byte-equality of the crypto envelope between the doc and run_config.
    env.provision_bootstraps()
    try:
        doc = json.load(open(descriptor["clients"][0]["bootstrap_path"]))
    finally:
        env.cleanup_bootstraps()

    assert doc["nonce"] == rc.nonce, "bootstrap nonce must equal the transport nonce"
    assert doc["hmacSecret"] == rc.hmac_secret, "hmac_secret must not diverge"
    assert doc["operatorToken"] == rc.operator_token, "operator_token must not diverge"
    assert doc["expiry"] == rc.expiry_unix_ms, "expiry must not diverge"
    # And the minted values are genuinely fresh (43-char CSPRNG tokens), not "tok"/"sec".
    assert len(rc.operator_token) == 43 and len(rc.hmac_secret) == 43 and len(rc.nonce) == 43


def test_build_live_run_reproduces_the_live_defect_now_fixed(tmp_path) -> None:
    """Regression for `t_e8777cca`: a descriptor whose wire carried a PAST
    `expiry_unix_ms` produced a bootstrap doc still expired. Against origin/main this
    FAILS (the past expiry is copied verbatim). Post-fix, the descriptor is refused
    for carrying a persisted secret before it can ever emit a stale doc — AND when
    presented topology-only, the emitted expiry is in the future."""
    past_ms = int(time.time() * 1000) - 106 * 60 * 1000  # 106 min in the past, as observed
    bad = _live_descriptor(tmp_path, wire_extra={"expiry_unix_ms": past_ms})

    # Post-fix: a persisted secret (here expiry_unix_ms) is refused at compose.
    with pytest.raises(WireSecretPersistedError):
        build_live_run(bad)

    # And the topology-only descriptor mints a FUTURE expiry (the defect cannot recur).
    good = _live_descriptor(tmp_path)
    plan, _env = build_live_run(good)
    assert plan.run_config.expiry_unix_ms > int(time.time() * 1000)


def test_build_live_run_refuses_persisted_secret_descriptor(tmp_path) -> None:
    for field, value in (
        ("nonce", "leftover-nonce"),
        ("hmac_secret", "leftover-secret"),
        ("operator_token", "leftover-token"),
        ("expiry_unix_ms", 32_500_000_000_000),
    ):
        d = _live_descriptor(tmp_path, wire_extra={field: value})
        with pytest.raises(WireSecretPersistedError) as ei:
            build_live_run(d)
        assert field in str(ei.value)


def test_build_live_run_refuses_persisted_lane_password(tmp_path) -> None:
    descriptor = _live_descriptor(tmp_path)
    descriptor["lane_password"] = "persisted-secret"

    with pytest.raises(Exception) as exc_info:
        build_live_run(descriptor)

    assert "lane_password" in str(exc_info.value)


def test_build_live_run_mints_one_lane_password_for_server_and_clients(
    tmp_path, monkeypatch
) -> None:
    from runner_core import live_composition

    descriptor = _live_descriptor(tmp_path)
    password_paths = []
    for index, client in enumerate(descriptor["clients"]):
        client["uid"] = os.geteuid()
        client["server_password_file"] = str(tmp_path / f"lane-{index}.secret")
        password_paths.append(client["server_password_file"])

    monkeypatch.setattr(live_composition, "mint_lane_password", lambda: "fresh-per-run")
    descriptor["server"]["server_binary"] = "/bin/true"

    plan, env = build_live_run(descriptor)
    process = env.spawn_lane(plan.lane)
    env.provision_bootstraps()
    try:
        assert process.args[-2:] == ["-password", "fresh-per-run"]
        for path in password_paths:
            assert open(path, encoding="utf-8").read().strip() == "fresh-per-run"
    finally:
        env.cleanup_bootstraps()


def test_build_live_run_does_not_mint_for_open_lane(tmp_path, monkeypatch) -> None:
    from runner_core import live_composition

    descriptor = _live_descriptor(tmp_path)

    def unexpected_mint():
        raise AssertionError("open lane must not mint a password")

    monkeypatch.setattr(live_composition, "mint_lane_password", unexpected_mint)

    build_live_run(descriptor)


# --------------------------------------------------------------------------- #
# Externally-managed lane + the minted-expiry regression (2026-07-30 live run).
# --------------------------------------------------------------------------- #

def test_plan_expiry_is_the_minted_wire_expiry_not_a_persisted_field(tmp_path) -> None:
    """`plan.expiry` MUST come from the minted wire envelope.

    Regression from the mint refactor: the plan still read a top-level
    `descriptor["expiry"]` after that field stopped being persisted, so a real live
    run died with `KeyError: 'expiry'`. The subtler danger is worse than the crash —
    had a stale value been present, it would have DISAGREED with the expiry the arm
    docs and transport authenticate against, which is exactly the divergence that
    produced a 106-minute-past expiry and a helper that refused to arm (t_e8777cca).

    A deliberately absurd persisted value is planted here: if the plan ever reads it
    again instead of the minted one, this fails loudly.
    """
    descriptor = _live_descriptor(tmp_path)
    descriptor["expiry"] = 1  # long past; must be IGNORED

    plan, _env = build_live_run(descriptor)

    assert plan.expiry == plan.run_config.expiry_unix_ms
    assert plan.expiry > int(time.time() * 1000), "plan expiry must be in the future"
    assert plan.expiry != 1, "plan must not read the stale persisted descriptor field"


def test_externally_managed_lane_uses_env_password_and_never_mints(
    tmp_path, monkeypatch
) -> None:
    """A lane the runner does not launch takes its password from the environment.

    t009l is a long-lived docker container whose SERVER_PASS was fixed when it
    started, and whose descriptor sets `server_binary` to /bin/true because the
    runner must NOT launch it. Minting there would hand the CLIENTS a fresh password
    the SERVER has never heard of, and every join would fail with a misleading
    wrong-password error. Opting in via `server.externally_managed` must therefore
    suppress the mint entirely and use the operator-supplied value.
    """
    from runner_core import live_composition

    descriptor = _live_descriptor(tmp_path)
    password_paths = []
    for index, client in enumerate(descriptor["clients"]):
        client["uid"] = os.geteuid()
        client["server_password_file"] = str(tmp_path / f"ext-{index}.secret")
        password_paths.append(client["server_password_file"])

    descriptor["server"]["server_binary"] = "/bin/true"
    descriptor["server"]["externally_managed"] = True

    def unexpected_mint():
        raise AssertionError("externally-managed lane must NOT mint a password")

    monkeypatch.setattr(live_composition, "mint_lane_password", unexpected_mint)
    monkeypatch.setenv("SBPR_QA_LANE_PASSWORD", "preexisting-lane-pw")

    _plan, env = build_live_run(descriptor)
    env.provision_bootstraps()
    try:
        for path in password_paths:
            assert open(path, encoding="utf-8").read().strip() == "preexisting-lane-pw"
    finally:
        env.cleanup_bootstraps()


def test_externally_managed_lane_refuses_when_env_password_missing(
    tmp_path, monkeypatch
) -> None:
    """Fail closed: no silent fallback to a minted password the server would reject."""
    descriptor = _live_descriptor(tmp_path)
    for client in descriptor["clients"]:
        client["uid"] = os.geteuid()
        client["server_password_file"] = str(tmp_path / "x.secret")
    descriptor["server"]["server_binary"] = "/bin/true"
    descriptor["server"]["externally_managed"] = True
    monkeypatch.delenv("SBPR_QA_LANE_PASSWORD", raising=False)

    with pytest.raises(ValueError, match="SBPR_QA_LANE_PASSWORD"):
        build_live_run(descriptor)


def test_externally_managed_lane_still_refuses_a_persisted_secret(
    tmp_path, monkeypatch
) -> None:
    """The #452 no-persisted-secret guard is NOT weakened by the env path."""
    descriptor = _live_descriptor(tmp_path)
    descriptor["server"]["externally_managed"] = True
    descriptor["lane_password"] = "persisted-is-always-refused"
    monkeypatch.setenv("SBPR_QA_LANE_PASSWORD", "preexisting-lane-pw")

    with pytest.raises(LanePasswordPersistedError):
        build_live_run(descriptor)
