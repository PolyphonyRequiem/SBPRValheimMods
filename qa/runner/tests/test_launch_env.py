"""Unit coverage for the launch-env sidecar (M6-LAUNCHENV).

These exercise the pure rendering + safe file placement of the sidecar the launch
wrapper sources to carry the three arming vars across the GABS daemon fork. The
end-to-end proof that a REAL forked child inherits them lives in
`test_launch_env_sidecar_delivery.py` (locally-gated, non-stubbed).
"""
from __future__ import annotations

import os
import stat

import pytest

from runner_core.launch_env import (
    ALLOWED_SIDECAR_KEYS,
    LaunchEnvError,
    SidecarWriter,
    render_sidecar,
    sidecar_path,
    sidecar_relpath,
)

_GOOD_ENV = {
    "SBPR_QA_T022_BOOTSTRAP": "/home/qa/valheim/qa-artifacts/t022-bootstrap-client_a.json",
    "SBPR_QA_HARNESS_INSTANCE": "client_a:deadbeefcafef00d",
    "SBPR_QA_STEAM_ID": "76561197965627562",
}


# --------------------------------------------------------------------------- #
# Path derivation is a pure function of game_id (the only thing the wrapper knows).
# --------------------------------------------------------------------------- #

def test_sidecar_relpath_is_pure_function_of_game_id() -> None:
    assert sidecar_relpath("valheim") == ".local/share/sbpr-qa/launch-env/valheim.env"
    assert sidecar_path("/home/qa", "valheim") == "/home/qa/.local/share/sbpr-qa/launch-env/valheim.env"


@pytest.mark.parametrize("bad", ["../evil", "a/b", ".", "..", "", "he re"])
def test_sidecar_relpath_rejects_traversal_and_separators(bad) -> None:
    with pytest.raises(LaunchEnvError):
        sidecar_relpath(bad)


# --------------------------------------------------------------------------- #
# Rendering: shell-sourceable, allowlisted keys only, no secret smuggling.
# --------------------------------------------------------------------------- #

def test_render_emits_sorted_keyvalue_lines() -> None:
    out = render_sidecar(_GOOD_ENV)
    assert out == (
        "SBPR_QA_HARNESS_INSTANCE=client_a:deadbeefcafef00d\n"
        "SBPR_QA_STEAM_ID=76561197965627562\n"
        "SBPR_QA_T022_BOOTSTRAP=/home/qa/valheim/qa-artifacts/t022-bootstrap-client_a.json\n"
    )


def test_render_refuses_a_non_allowlisted_key() -> None:
    # The whole point: a secret (HMAC/operator token) must never be writable to the
    # 0644 sidecar. Only the three non-secret arming vars are allowed.
    with pytest.raises(LaunchEnvError):
        render_sidecar({**_GOOD_ENV, "SBPR_QA_HMAC_SECRET": "deadbeef"})


def test_allowlist_is_exactly_the_three_arming_vars() -> None:
    assert ALLOWED_SIDECAR_KEYS == frozenset(
        {"SBPR_QA_T022_BOOTSTRAP", "SBPR_QA_HARNESS_INSTANCE", "SBPR_QA_STEAM_ID"}
    )


def test_render_refuses_newline_or_shell_hostile_values() -> None:
    with pytest.raises(LaunchEnvError):
        render_sidecar({**_GOOD_ENV, "SBPR_QA_STEAM_ID": "7656\n119"})
    with pytest.raises(LaunchEnvError):
        render_sidecar({**_GOOD_ENV, "SBPR_QA_STEAM_ID": "76561; rm -rf /"})
    with pytest.raises(LaunchEnvError):
        render_sidecar({**_GOOD_ENV, "SBPR_QA_STEAM_ID": "$(evil)"})


# --------------------------------------------------------------------------- #
# Writer: atomic 0644 placement + tracked, idempotent removal.
# --------------------------------------------------------------------------- #

def test_write_places_0644_file_the_wrapper_can_source(tmp_path) -> None:
    path = str(tmp_path / "home" / ".local" / "share" / "sbpr-qa" / "launch-env" / "valheim.env")
    writer = SidecarWriter()
    sidecar = writer.write(path, _GOOD_ENV)
    assert sidecar.path == path
    assert os.path.isfile(path)
    mode = stat.S_IMODE(os.stat(path).st_mode)
    assert mode == 0o644  # non-secret; group/other-readable is fine and intended
    with open(path, "r", encoding="utf-8") as fh:
        body = fh.read()
    assert "SBPR_QA_T022_BOOTSTRAP=" in body
    # It is tracked for teardown.
    assert [s.path for s in writer.written] == [path]


def test_write_requires_absolute_path(tmp_path) -> None:
    with pytest.raises(LaunchEnvError):
        SidecarWriter().write("relative/valheim.env", _GOOD_ENV)


def test_write_refuses_symlink_target(tmp_path) -> None:
    real = tmp_path / "real.env"
    real.write_text("x")
    link = tmp_path / "link.env"
    os.symlink(real, link)
    with pytest.raises(LaunchEnvError):
        SidecarWriter().write(str(link), _GOOD_ENV)


def test_remove_is_idempotent_and_clears_the_file(tmp_path) -> None:
    path = str(tmp_path / "valheim.env")
    writer = SidecarWriter()
    writer.write(path, _GOOD_ENV)
    assert os.path.exists(path)
    writer.remove(path)
    assert not os.path.exists(path)
    # Second remove is a no-op (teardown safety), never raises.
    writer.remove(path)
    assert writer.written == []


def test_remove_all_clears_every_written_sidecar(tmp_path) -> None:
    writer = SidecarWriter()
    p1 = str(tmp_path / "a" / "valheim.env")
    p2 = str(tmp_path / "b" / "valheim.env")
    writer.write(p1, _GOOD_ENV)
    writer.write(p2, _GOOD_ENV)
    writer.remove_all()
    assert not os.path.exists(p1)
    assert not os.path.exists(p2)
    assert writer.written == []
