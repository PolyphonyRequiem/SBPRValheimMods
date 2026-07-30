"""Per-run lane-password minting (#452)."""
from __future__ import annotations

import pytest

from runner_core.lane_password_mint import (
    LanePasswordPersistedError,
    assert_descriptor_carries_no_lane_password,
    compose_server_args_with_lane_password,
    mint_lane_password,
)


def test_lane_password_is_fresh_per_run() -> None:
    first = mint_lane_password()
    second = mint_lane_password()
    assert first != second
    assert len(first) >= 20
    assert len(second) >= 20


def test_persisted_lane_password_is_refused() -> None:
    with pytest.raises(LanePasswordPersistedError) as exc_info:
        assert_descriptor_carries_no_lane_password({"lane_password": "old-secret"})
    assert "lane_password" in str(exc_info.value)


def test_topology_only_descriptor_is_allowed() -> None:
    assert_descriptor_carries_no_lane_password({"lane": {"requires_password": True}})


def test_server_args_receive_the_same_minted_password() -> None:
    args = compose_server_args_with_lane_password(
        ("-name", "t022", "-port", "2476"), "fresh-secret"
    )
    assert args[-2:] == ("-password", "fresh-secret")


def test_existing_server_password_is_replaced_not_duplicated() -> None:
    args = compose_server_args_with_lane_password(
        ("-name", "t022", "-password", "persisted", "-port", "2476"),
        "fresh-secret",
    )
    assert args.count("-password") == 1
    assert args[args.index("-password") + 1] == "fresh-secret"
    assert "persisted" not in args
