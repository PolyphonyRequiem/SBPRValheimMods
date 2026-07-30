"""Join-delivery preflight tests (#453).

Engine-free and pure: no process is started, no game is contacted, nothing is written.
Wrapper contents are strings; the filesystem seam is a dict.

The suite is built around what actually failed in production. `TestRealWrappers` runs
the checker against the TWO REAL DEPLOYED WRAPPERS on this host, so a drift in either
one fails here rather than ten minutes into a GPU boot.
"""
from __future__ import annotations

import os

import pytest

from runner_core.arrange_manifest import ArrangeManifest
from runner_core.arrange_static import P_JOIN_TARGET, StaticEnvironment, arrange_static
from runner_core.join_delivery import (
    CONNECT_ARGS_VAR,
    CONNECT_VAR,
    check_join_delivery,
    inspect_wrapper,
)

# The two real wrappers on this host. Skipped when absent (CI has no Valheim install).
#
# Resolved from the OWNING USER's home, not `~`: this suite may run under a profile
# whose $HOME is redirected (a Hermes agent profile, a sandbox), and `expanduser("~")`
# would then silently point somewhere with no wrappers — turning the most valuable
# tests in this file into permanent skips that look like passes.
def _real_home() -> str:
    import pwd

    for candidate in (os.environ.get("SUDO_USER"), "polyphonyrequiem"):
        if not candidate:
            continue
        try:
            return pwd.getpwnam(candidate).pw_dir
        except KeyError:
            continue
    return os.path.expanduser("~")


_HOME = _real_home()
REAL_WRAPPER_A = os.path.join(
    _HOME, ".local/share/Trailborne/Valheim-Modded/run-trailborne.sh"
)
REAL_WRAPPER_B = os.path.join(
    _HOME, "valheim/mcp-harness/dual-client/templates/run-valbot-bepinex.sh"
)

# A minimal GABS-launched wrapper: sources the sidecar, builds the fragment, execs the
# game binary directly. No Steam rotation, so fragment position is unconstrained.
GOOD_GABS_WRAPPER = """#!/usr/bin/env bash
set -euo pipefail
SBPR_QA_LAUNCH_ENV_FILE="${SBPR_QA_LAUNCH_ENV_FILE:-$HOME/.local/share/sbpr-qa/launch-env/${GABS_GAME_ID:-valheim}.env}"
if [[ -f "$SBPR_QA_LAUNCH_ENV_FILE" && ! -L "$SBPR_QA_LAUNCH_ENV_FILE" ]]; then
  set -a
  . "$SBPR_QA_LAUNCH_ENV_FILE"
  set +a
fi
SBPR_QA_CONNECT_ARGS=()
if [[ -n "${SBPR_QA_CONNECT:-}" ]]; then
  SBPR_QA_CONNECT_ARGS=(+connect "$SBPR_QA_CONNECT")
fi
exec "$HERE/valheim.x86_64" -console "${SBPR_QA_CONNECT_ARGS[@]}" "$@"
"""

# A Steam-launched wrapper: the fragment MUST be appended after "$@" to survive
# run_bepinex.sh's SteamLaunch argv rotation.
GOOD_STEAM_WRAPPER = """#!/usr/bin/env bash
set -euo pipefail
. "$SBPR_QA_LAUNCH_ENV_FILE"
SBPR_QA_CONNECT_ARGS=()
if [[ -n "${SBPR_QA_CONNECT:-}" ]]; then
  SBPR_QA_CONNECT_ARGS=(+connect "$SBPR_QA_CONNECT")
fi
exec /usr/bin/setsid --wait "$VSI_BEPINEX_RUNNER" "$@" "${SBPR_QA_CONNECT_ARGS[@]}"
"""

# The regression #449 warned about: same wrapper, fragment moved BEFORE "$@".
ROTATION_BROKEN_STEAM_WRAPPER = GOOD_STEAM_WRAPPER.replace(
    'exec /usr/bin/setsid --wait "$VSI_BEPINEX_RUNNER" "$@" "${SBPR_QA_CONNECT_ARGS[@]}"',
    'exec /usr/bin/setsid --wait "$VSI_BEPINEX_RUNNER" "${SBPR_QA_CONNECT_ARGS[@]}" "$@"',
)

# Never reads the sidecar at all — the original twelve-day state of client_b.
NO_SIDECAR_WRAPPER = """#!/usr/bin/env bash
set -euo pipefail
exec /usr/bin/setsid --wait "$VSI_BEPINEX_RUNNER" "$@"
"""

# Reads the value but never turns it into an argument. Env alone does not populate
# vanilla's m_queuedJoinServer.
READS_BUT_NO_ARGV_WRAPPER = """#!/usr/bin/env bash
set -euo pipefail
. "$SBPR_QA_LAUNCH_ENV_FILE"
echo "joining ${SBPR_QA_CONNECT:-}"
exec /usr/bin/setsid --wait "$VSI_BEPINEX_RUNNER" "$@"
"""


def manifest_with(*, a_wrapper=None, b_wrapper=None, b_kind="steam_applaunch"):
    """Two asymmetric clients; wrapper_path present only when a wrapper is supplied."""
    a_launcher = {"kind": "gabs", "endpoint": "http://x/mcp", "game_id": "g"}
    if a_wrapper:
        a_launcher["wrapper_path"] = a_wrapper
    b_launcher = (
        {"kind": "steam_applaunch", "app_id": "892970"}
        if b_kind == "steam_applaunch"
        else {"kind": b_kind}
    )
    if b_wrapper:
        b_launcher["wrapper_path"] = b_wrapper

    def client(actor, uid, user, acct, root, launcher, port, prof):
        return {
            "actor": actor, "uid": uid, "user": user, "steam_account": acct,
            "game_root": root, "binary_path": root + "/valheim.x86_64",
            "plugins_dir": root + "/BepInEx/plugins", "launcher": launcher,
            "ports": {
                "loopback_control": port,
                "valbridge_gabp": port + 3000,
                "unity_script_host": port + 4000,
            }, "qa_profile": prof,
            "join": {"host": "127.0.0.1", "port": 2476, "delivery": "connect_argv"},
            "artifacts": [], "credentials": {},
        }

    return {
        "kind": "sbpr-qa-arrange-manifest", "version": 2,
        "lane": {"lane_id": "l", "world_name": "w", "host": "127.0.0.1",
                 "port": 2476, "requires_password": False},
        "artifacts": [],
        "clients": [
            client("client_a", 1000, "poly", "76561197965627562",
                   "/home/poly/game", a_launcher, 48610, "qa_a"),
            client("client_b", 1001, "valbot", "76561198671522196",
                   "/home/valbot/game", b_launcher, 48611, "qa_b"),
        ],
    }


def run(manifest_dict, files):
    m = ArrangeManifest.parse(manifest_dict)
    return check_join_delivery(m, lambda p: files.get(p))


def for_client(failures, actor):
    return [f for f in failures if f.client == actor]


class TestInspectWrapper:
    def test_good_gabs_wrapper(self):
        seam = inspect_wrapper(GOOD_GABS_WRAPPER)
        assert seam.sources_sidecar and seam.builds_connect_args
        assert seam.fragment_after_passthrough is False  # before "$@", fine for GABS

    def test_good_steam_wrapper_has_fragment_after_passthrough(self):
        seam = inspect_wrapper(GOOD_STEAM_WRAPPER)
        assert seam.sources_sidecar and seam.builds_connect_args
        assert seam.fragment_after_passthrough is True

    def test_rotation_broken_wrapper_detected(self):
        assert inspect_wrapper(ROTATION_BROKEN_STEAM_WRAPPER).fragment_after_passthrough is False

    def test_no_sidecar_wrapper(self):
        seam = inspect_wrapper(NO_SIDECAR_WRAPPER)
        assert not seam.sources_sidecar and not seam.builds_connect_args

    def test_reads_but_no_argv(self):
        seam = inspect_wrapper(READS_BUT_NO_ARGV_WRAPPER)
        assert seam.sources_sidecar and not seam.builds_connect_args

    def test_exec_line_is_captured_for_quoting_back(self):
        assert "setsid" in (inspect_wrapper(GOOD_STEAM_WRAPPER).exec_line or "")


class TestCheckJoinDelivery:
    def test_both_wrappers_good_passes(self):
        failures = run(
            manifest_with(a_wrapper="/a.sh", b_wrapper="/b.sh"),
            {"/a.sh": GOOD_GABS_WRAPPER, "/b.sh": GOOD_STEAM_WRAPPER},
        )
        assert failures == []

    def test_no_wrapper_declared_is_skipped(self):
        """A launcher that execs the binary directly has no wrapper to check."""
        assert run(manifest_with(), {}) == []

    def test_missing_wrapper_file_is_named(self):
        failures = run(manifest_with(b_wrapper="/gone.sh"), {})
        f = for_client(failures, "client_b")[0]
        assert "missing or unreadable" in f.detail
        assert "/gone.sh" in f.expected

    def test_wrapper_that_never_reads_the_var(self):
        failures = run(
            manifest_with(b_wrapper="/b.sh"), {"/b.sh": NO_SIDECAR_WRAPPER}
        )
        f = for_client(failures, "client_b")[0]
        assert CONNECT_VAR in f.detail

    def test_wrapper_that_reads_but_builds_no_argument(self):
        failures = run(
            manifest_with(b_wrapper="/b.sh"), {"/b.sh": READS_BUT_NO_ARGV_WRAPPER}
        )
        f = for_client(failures, "client_b")[0]
        assert "no `+connect` argument" in f.detail
        assert "m_queuedJoinServer" in f.remedy

    def test_rotation_regression_is_caught_for_steam(self):
        """THE regression #449 warned about: move the fragment, silently break the join."""
        failures = run(
            manifest_with(b_wrapper="/b.sh"),
            {"/b.sh": ROTATION_BROKEN_STEAM_WRAPPER},
        )
        f = for_client(failures, "client_b")[0]
        assert "PREPENDED" in f.detail
        assert "rotat" in f.remedy.lower()
        assert CONNECT_ARGS_VAR in f.expected

    def test_rotation_rule_does_NOT_apply_to_gabs(self):
        """client_a execs the game binary itself — no Steam wrapper chain, no rotation.
        Its fragment sits before "$@" and that is correct, so applying the Steam rule
        here would refuse the working production wrapper."""
        failures = run(
            manifest_with(a_wrapper="/a.sh"), {"/a.sh": GOOD_GABS_WRAPPER}
        )
        assert for_client(failures, "client_a") == []

    def test_undeterminable_position_is_refused_for_steam(self):
        restructured = """#!/usr/bin/env bash
. "$SBPR_QA_LAUNCH_ENV_FILE"
SBPR_QA_CONNECT_ARGS=(+connect "$SBPR_QA_CONNECT")
ARGS=("$@")
exec "$RUNNER" "${ARGS[@]}"
"""
        failures = run(manifest_with(b_wrapper="/b.sh"), {"/b.sh": restructured})
        f = for_client(failures, "client_b")[0]
        assert "cannot determine" in f.detail

    def test_failures_are_specific(self):
        failures = run(
            manifest_with(a_wrapper="/a.sh", b_wrapper="/b.sh"),
            {"/a.sh": NO_SIDECAR_WRAPPER, "/b.sh": ROTATION_BROKEN_STEAM_WRAPPER},
        )
        assert len(failures) == 2
        for f in failures:
            assert f.precondition == P_JOIN_TARGET
            assert f.client in ("client_a", "client_b")
            assert f.detail and f.expected and f.actual and f.remedy

    def test_each_client_judged_against_its_own_launcher(self):
        """No symmetry: the same wrapper text is a failure under Steam and fine under
        GABS, because only one of them passes through the rotation."""
        failures = run(
            manifest_with(a_wrapper="/w.sh", b_wrapper="/w.sh"),
            {"/w.sh": GOOD_GABS_WRAPPER},
        )
        assert for_client(failures, "client_a") == []
        assert for_client(failures, "client_b")  # same bytes, different verdict

    def test_no_join_target_is_not_double_reported(self):
        """S8 already reports a missing join target; this check stays quiet."""
        m = manifest_with(b_wrapper="/b.sh")
        del m["clients"][1]["join"]
        failures = run(m, {"/b.sh": NO_SIDECAR_WRAPPER})
        assert for_client(failures, "client_b") == []

    def test_third_client_needs_no_code_change(self):
        m = manifest_with(a_wrapper="/a.sh", b_wrapper="/b.sh")
        third = dict(m["clients"][0])
        third.update(
            actor="client_c", uid=1002, user="v2", steam_account="76561198000000003",
            game_root="/srv/c", binary_path="/srv/c/valheim.x86_64",
            plugins_dir="/srv/c/BepInEx/plugins", qa_profile="qa_c",
            ports={
                "loopback_control": 48612,
                "valbridge_gabp": 51612,
                "unity_script_host": 52612,
            },
            launcher={"kind": "direct_exec", "wrapper_path": "/c.sh"},
        )
        m["clients"].append(third)
        failures = run(
            m,
            {"/a.sh": GOOD_GABS_WRAPPER, "/b.sh": GOOD_STEAM_WRAPPER,
             "/c.sh": NO_SIDECAR_WRAPPER},
        )
        assert for_client(failures, "client_c")


class TestWiredIntoStaticPhase:
    def test_arrange_static_runs_the_join_delivery_check(self):
        env = StaticEnvironment(
            path_exists=lambda p: False,
            hash_file=lambda p: None,
            find_named_files=lambda _root, _name: (),
            read_text=lambda p: ROTATION_BROKEN_STEAM_WRAPPER,
        )
        report = arrange_static(manifest_with(b_wrapper="/b.sh"), env)
        assert not report.ok
        assert any("PREPENDED" in f.detail for f in report.failures)

    def test_static_environment_without_read_text_still_works(self):
        """`read_text` stays optional: omitting it reports 'unreadable', never crashes.

        This is the deliberate asymmetry with `find_named_files` (#467). An absent
        wrapper text is an honest, self-describing S8 result, so a focused test that
        exercises unrelated preconditions may omit the seam. Proving a component is
        ABSENT cannot be defaulted the same way — see the mandatory-seam tests.
        """
        env = StaticEnvironment(
            path_exists=lambda p: False,
            hash_file=lambda p: None,
            find_named_files=lambda _root, _name: (),
        )
        report = arrange_static(manifest_with(b_wrapper="/b.sh"), env)
        assert any("missing or unreadable" in f.detail for f in report.failures)


class TestRealWrappers:
    """Run the checker against the ACTUAL deployed wrappers on this host.

    This is the test that earns its keep: it turns "the join seam is intact" from a
    claim in a spike comment into a fact re-asserted on every suite run. If someone
    tidies either wrapper and moves the fragment, this fails in seconds instead of
    ten minutes into a GPU boot with no error logged.
    """

    @pytest.mark.skipif(
        not os.path.isfile(REAL_WRAPPER_A), reason="client_a wrapper not on this host"
    )
    def test_real_client_a_wrapper_delivers(self):
        text = open(REAL_WRAPPER_A, encoding="utf-8", errors="replace").read()
        seam = inspect_wrapper(text)
        assert seam.sources_sidecar, f"{REAL_WRAPPER_A} no longer reads {CONNECT_VAR}"
        assert seam.builds_connect_args, f"{REAL_WRAPPER_A} builds no +connect fragment"

    @pytest.mark.skipif(
        not os.path.isfile(REAL_WRAPPER_B), reason="client_b wrapper not on this host"
    )
    def test_real_client_b_wrapper_delivers_and_appends(self):
        text = open(REAL_WRAPPER_B, encoding="utf-8", errors="replace").read()
        seam = inspect_wrapper(text)
        assert seam.sources_sidecar, f"{REAL_WRAPPER_B} no longer reads {CONNECT_VAR}"
        assert seam.builds_connect_args, f"{REAL_WRAPPER_B} builds no +connect fragment"
        assert seam.fragment_after_passthrough is True, (
            f"{REAL_WRAPPER_B} no longer APPENDS the +connect fragment after \"$@\". "
            "Steam's run_bepinex.sh rotates argv; a prepended fragment is swallowed and "
            "the client parks at the server list with nothing logged. Exec line seen: "
            f"{seam.exec_line}"
        )
