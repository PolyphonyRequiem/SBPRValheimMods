"""Fail-closed live-mode preflight coverage (ADR-0009 §5, §7, §8) — M6-EXEC.

Proves the runner refuses live execution unless EVERY precondition holds: explicit
opt-in, a valid disposable-lane sentinel carrying the hard production deny list, and
verified overlay pins. No game/network/file side effects; pure decision logic.
"""
from __future__ import annotations

import pytest

from runner_core.live_preflight import (
    LiveModeRefused,
    PRODUCTION_PORTS,
    evaluate_live_preflight,
    validate_sentinel,
    verify_overlay_pins,
    _fold_digest,
)


def good_sentinel() -> dict:
    return {
        "kind": "sbpr-qa-overlay-lane-sentinel",
        "lane": "disposable",
        "production_deny": {"worlds": ["Niflheim:2456", "Heistan:2466"]},
    }


def good_manifest() -> dict:
    parts = {
        "helper": "a" * 64,
        "runner": "b" * 64,
        "contracts": "c" * 64,
        "profile": "d" * 64,
        "scenario": "e" * 64,
        "lane_sentinel": "f" * 64,
    }
    return {
        "kind": "sbpr-qa-overlay-manifest",
        "parts": parts,
        "overlay_digest": _fold_digest(parts),
    }


def observed_from(manifest: dict) -> dict:
    return dict(manifest["parts"])


# --------------------------------------------------------------------------- #
# Sentinel validation.
# --------------------------------------------------------------------------- #

def test_valid_disposable_sentinel_accepts() -> None:
    assert validate_sentinel(good_sentinel()) == "disposable"


def test_sentinel_wrong_kind_refused() -> None:
    s = good_sentinel()
    s["kind"] = "something-else"
    with pytest.raises(LiveModeRefused):
        validate_sentinel(s)


def test_sentinel_non_disposable_refused() -> None:
    s = good_sentinel()
    s["lane"] = "production"
    with pytest.raises(LiveModeRefused):
        validate_sentinel(s)


def test_sentinel_missing_production_deny_refused() -> None:
    s = good_sentinel()
    s["production_deny"] = {"worlds": ["Niflheim:2456"]}  # Heistan missing
    with pytest.raises(LiveModeRefused):
        validate_sentinel(s)


def test_production_ports_are_both_denied_constants() -> None:
    assert PRODUCTION_PORTS == frozenset({2456, 2466})


# --------------------------------------------------------------------------- #
# Overlay pin verification.
# --------------------------------------------------------------------------- #

def test_overlay_pins_verify_when_consistent() -> None:
    m = good_manifest()
    assert verify_overlay_pins(m, observed_from(m)) == m["overlay_digest"]


def test_overlay_pin_drift_refused() -> None:
    m = good_manifest()
    obs = observed_from(m)
    obs["runner"] = "9" * 64  # drift one part
    with pytest.raises(LiveModeRefused):
        verify_overlay_pins(m, obs)


def test_overlay_missing_part_refused() -> None:
    m = good_manifest()
    obs = observed_from(m)
    del obs["scenario"]
    with pytest.raises(LiveModeRefused):
        verify_overlay_pins(m, obs)


def test_overlay_folded_digest_mismatch_refused() -> None:
    m = good_manifest()
    m["overlay_digest"] = "0" * 64  # tampered fold
    with pytest.raises(LiveModeRefused):
        verify_overlay_pins(m, observed_from(m))


def test_overlay_wrong_kind_refused() -> None:
    m = good_manifest()
    m["kind"] = "not-a-manifest"
    with pytest.raises(LiveModeRefused):
        verify_overlay_pins(m, observed_from(m))


# --------------------------------------------------------------------------- #
# The whole gate — ALL of opt-in + sentinel + pins.
# --------------------------------------------------------------------------- #

def test_gate_passes_when_everything_holds() -> None:
    m = good_manifest()
    r = evaluate_live_preflight(
        live_requested=True, sentinel=good_sentinel(),
        manifest=m, observed_part_hashes=observed_from(m),
    )
    assert r.ok and r.sentinel_lane == "disposable"


def test_gate_refuses_without_live_flag() -> None:
    m = good_manifest()
    r = evaluate_live_preflight(
        live_requested=False, sentinel=good_sentinel(),
        manifest=m, observed_part_hashes=observed_from(m),
    )
    assert not r.ok and "not requested" in r.reason


def test_gate_refuses_without_sentinel() -> None:
    m = good_manifest()
    r = evaluate_live_preflight(
        live_requested=True, sentinel=None,
        manifest=m, observed_part_hashes=observed_from(m),
    )
    assert not r.ok and "sentinel" in r.reason


def test_gate_refuses_without_manifest() -> None:
    r = evaluate_live_preflight(
        live_requested=True, sentinel=good_sentinel(),
        manifest=None, observed_part_hashes=None,
    )
    assert not r.ok


def test_gate_refuses_on_bad_sentinel_even_with_good_pins() -> None:
    m = good_manifest()
    bad = good_sentinel()
    bad["lane"] = "production"
    r = evaluate_live_preflight(
        live_requested=True, sentinel=bad,
        manifest=m, observed_part_hashes=observed_from(m),
    )
    assert not r.ok


# ---------------------------------------------------------------------------
# M6-LANEPW — lane password-policy consistency.
#
# The wall this closes: t009l is password-gated (SERVER_PASS) but the deployed
# descriptor named no lane_password and no client named a server_password_file.
# LanePasswordProvisioner correctly no-opped as an "open lane", the client joined
# with no password, vanilla's needPassword=true branch waited on a prompt no
# headless client answers, Player.OnSpawned never fired, and TryArm was never
# reached. Descriptor-only invariant; no Docker coupling.
# ---------------------------------------------------------------------------

from runner_core.live_preflight import validate_lane_password_consistency  # noqa: E402


def _descriptor(*, requires, pw_files, lane_password=None):
    d = {
        "lane": {"lane_id": "t009l", "port": 2476},
        "clients": [
            {"actor": "client_a"},
            {"actor": "client_b"},
        ],
    }
    if requires is not ...:
        d["lane"]["requires_password"] = requires
    if lane_password is not None:
        d["lane_password"] = lane_password
    for actor, path in pw_files.items():
        for c in d["clients"]:
            if c["actor"] == actor:
                c["server_password_file"] = path
    return d


def test_gated_lane_with_password_and_all_files_passes():
    d = _descriptor(
        requires=True,
        pw_files={"client_a": "/tmp/a.secret", "client_b": "/tmp/b.secret"},
        lane_password="sekrit",
    )
    assert validate_lane_password_consistency(d) is True


def test_open_lane_with_no_files_passes():
    assert validate_lane_password_consistency(_descriptor(requires=False, pw_files={})) is False


def test_undeclared_policy_is_refused():
    # Fail closed: the policy is never inferred from the client entries.
    with pytest.raises(LiveModeRefused) as exc:
        validate_lane_password_consistency(_descriptor(requires=..., pw_files={}))
    assert "requires_password" in str(exc.value)


def test_gated_lane_missing_a_client_password_file_is_refused():
    # THE REGRESSION: exactly the deployed-descriptor shape that burned the run.
    d = _descriptor(requires=True, pw_files={"client_a": "/tmp/a.secret"}, lane_password="sekrit")
    with pytest.raises(LiveModeRefused) as exc:
        validate_lane_password_consistency(d)
    assert "client_b" in str(exc.value)


def test_gated_lane_with_no_files_at_all_is_refused():
    d = _descriptor(requires=True, pw_files={}, lane_password="sekrit")
    with pytest.raises(LiveModeRefused) as exc:
        validate_lane_password_consistency(d)
    assert "client_a" in str(exc.value) and "client_b" in str(exc.value)


def test_gated_lane_without_lane_password_is_refused():
    d = _descriptor(
        requires=True, pw_files={"client_a": "/tmp/a.secret", "client_b": "/tmp/b.secret"}
    )
    with pytest.raises(LiveModeRefused) as exc:
        validate_lane_password_consistency(d)
    assert "lane_password" in str(exc.value)


def test_gated_lane_with_empty_lane_password_is_refused():
    d = _descriptor(
        requires=True,
        pw_files={"client_a": "/tmp/a.secret", "client_b": "/tmp/b.secret"},
        lane_password="",
    )
    with pytest.raises(LiveModeRefused):
        validate_lane_password_consistency(d)


def test_open_lane_naming_a_password_file_is_refused():
    # The other direction: an open lane needs no credential.
    d = _descriptor(requires=False, pw_files={"client_a": "/tmp/a.secret"})
    with pytest.raises(LiveModeRefused) as exc:
        validate_lane_password_consistency(d)
    assert "client_a" in str(exc.value)


def test_non_boolean_policy_is_refused():
    with pytest.raises(LiveModeRefused):
        validate_lane_password_consistency(_descriptor(requires="yes", pw_files={}))


def test_missing_lane_object_is_refused():
    with pytest.raises(LiveModeRefused):
        validate_lane_password_consistency({"clients": [{"actor": "a"}]})


def test_empty_client_list_is_refused():
    with pytest.raises(LiveModeRefused):
        validate_lane_password_consistency({"lane": {"requires_password": False}, "clients": []})
