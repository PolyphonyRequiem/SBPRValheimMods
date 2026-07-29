"""Mint the disposable-lane password per run (#452).

The descriptor is durable topology and must never persist this credential. One fresh
value is composed into both the dedicated-server argv and each client's password file,
then the existing teardown path sweeps the files.
"""
from __future__ import annotations

import secrets
from typing import Any, Mapping, Sequence, Tuple


class LanePasswordPersistedError(RuntimeError):
    """The durable descriptor still contains a lane-password value."""


def assert_descriptor_carries_no_lane_password(descriptor: Mapping[str, Any]) -> None:
    """Fail closed if the descriptor persists the per-run lane password."""
    if "lane_password" in descriptor:
        raise LanePasswordPersistedError(
            "descriptor still carries persisted secret field 'lane_password': the "
            "disposable-lane password is minted per run and MUST NOT be persisted"
        )


def mint_lane_password() -> str:
    """Return one fresh CSPRNG password suitable for Valheim's `-password` argument."""
    return secrets.token_urlsafe(24)


def compose_server_args_with_lane_password(
    server_args: Sequence[str], password: str
) -> Tuple[str, ...]:
    """Return server argv with exactly one `-password <minted>` pair."""
    if not password:
        raise ValueError("minted lane password must be non-empty")

    result = []
    index = 0
    while index < len(server_args):
        arg = str(server_args[index])
        if arg == "-password":
            if index + 1 >= len(server_args):
                raise ValueError("server_args has '-password' without a value")
            index += 2
            continue
        result.append(arg)
        index += 1
    result.extend(("-password", password))
    return tuple(result)
