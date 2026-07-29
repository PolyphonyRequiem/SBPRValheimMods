"""Credential files are readable by the identity that consumes them (#452)."""
from __future__ import annotations

import os
import stat

import pytest

from runner_core.credential_access import (
    CredentialReadError,
    assert_readable_as_consumer,
    prepare_credential_directory,
)


def test_prepare_directory_is_traversable_but_not_listable(tmp_path) -> None:
    directory = tmp_path / "credentials"
    prepare_credential_directory(str(directory))

    mode = stat.S_IMODE(os.stat(directory).st_mode)
    assert mode == 0o711
    assert mode & 0o111 == 0o111
    assert mode & 0o044 == 0


def test_readability_assertion_reads_as_declared_uid(tmp_path) -> None:
    path = tmp_path / "lane.secret"
    path.write_text("throwaway\n", encoding="utf-8")
    calls = []

    def read_as_uid(actual_path: str, uid: int) -> None:
        calls.append((actual_path, uid))
        with open(actual_path, "rb") as fh:
            fh.read(1)

    assert_readable_as_consumer(
        actor="client_b",
        path=str(path),
        consumer_uid=1001,
        read_as_uid=read_as_uid,
    )

    assert calls == [(str(path), 1001)]


def test_missing_and_unreadable_are_one_named_failure(tmp_path) -> None:
    path = tmp_path / "absent.secret"

    def unreadable(_path: str, _uid: int) -> None:
        raise PermissionError("denied")

    with pytest.raises(CredentialReadError) as exc_info:
        assert_readable_as_consumer(
            actor="client_b",
            path=str(path),
            consumer_uid=1001,
            read_as_uid=unreadable,
        )

    message = str(exc_info.value)
    assert "client_b" in message
    assert str(path) in message
    assert "uid 1001" in message
    assert "missing or unreadable" in message


def test_default_reader_really_reads_as_current_uid(tmp_path) -> None:
    path = tmp_path / "current.secret"
    path.write_text("throwaway\n", encoding="utf-8")

    assert_readable_as_consumer(
        actor="current",
        path=str(path),
        consumer_uid=os.geteuid(),
    )
