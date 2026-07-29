"""M6-JOIN3 / B2 — the lane-password file has a real PRODUCER and a TEARDOWN unlink.

The branch shipped a consumer (the QA FejdStartup hook reads a credential file named by
SBPR_QA_SERVER_PASSWORD_FILE) with no producer and no teardown. These tests prove the
producer now exists with the same discipline as BootstrapProvisioner — atomic,
descriptor-derived — and that the credential is unlinked on EVERY teardown path, proven
by command output (the file is gone), not merely asserted. They also prove the value is
never surfaced in an error message.
"""
from __future__ import annotations

import os
import stat

import pytest

import runner_core.lane_password_provision as lane_password_module
from runner_core.lane_password_provision import (
    LanePasswordProvisioner,
    LanePasswordProvisionError,
    ProvisionedLanePassword,
)


def _descriptor(tmp_path, *, password="t009lproof", with_file=True, uid=None):
    pwfile = str(tmp_path / "lane" / "lane-password.txt")
    client = {
        "actor": "client_a",
        "uid": os.geteuid() if uid is None else uid,
        "steam_id": "76561197965627562",
    }
    if with_file:
        client["server_password_file"] = pwfile
    d = {"clients": [client]}
    if password is not None:
        d["lane_password"] = password
    return d, pwfile


def test_producer_writes_password_file_mode_0644(tmp_path):
    d, pwfile = _descriptor(tmp_path)
    prov = LanePasswordProvisioner()
    written = prov.provision_from_descriptor(d)

    assert [w.actor for w in written] == ["client_a"]
    assert os.path.exists(pwfile)
    # Exactly mode 0644 — readable by the cross-uid client that consumes it.
    # 0644, not 0600: the QA client that consumes this file runs as a DIFFERENT
    # uid (valbot, 1001) than the runner that writes it (1000), so owner-only permissions
    # made it structurally unreadable — the client joined with no password and stalled
    # forever on vanilla's password prompt. These are per-run throwaway credentials for a
    # disposable loopback lane (the wire envelope is minted per run with a short TTL and
    # swept on teardown), so local readability is the right trade against blocking the
    # test entirely. The containing directory is 0711: traversable to a known path,
    # not listable.
    mode = stat.S_IMODE(os.stat(pwfile).st_mode)
    assert mode == 0o644, f"expected 0644, got {oct(mode)}"
    # The value is the descriptor's password, single line (consumer trims whitespace).
    with open(pwfile, encoding="utf-8") as fh:
        assert fh.read().strip() == "t009lproof"


def test_provision_asserts_readability_as_each_consuming_uid(tmp_path):
    d, pwfile = _descriptor(tmp_path, uid=1001)
    reads = []
    prov = LanePasswordProvisioner(
        read_as_uid=lambda path, uid: reads.append((path, uid))
    )

    prov.provision_from_descriptor(d)

    assert reads == [(pwfile, 1001)]


def test_unreadable_failure_names_client_path_and_consuming_uid(tmp_path):
    d, pwfile = _descriptor(tmp_path, uid=1001)

    def unreadable(_path, _uid):
        raise PermissionError("denied")

    with pytest.raises(LanePasswordProvisionError) as exc_info:
        LanePasswordProvisioner(read_as_uid=unreadable).provision_from_descriptor(d)

    message = str(exc_info.value)
    assert "client_a" in message
    assert pwfile in message
    assert "uid 1001" in message
    assert "missing or unreadable" in message


def test_provision_repairs_existing_directory_to_0711(tmp_path):
    d, pwfile = _descriptor(tmp_path)
    directory = os.path.dirname(pwfile)
    os.makedirs(directory, mode=0o700)
    os.chmod(directory, 0o700)

    LanePasswordProvisioner().provision_from_descriptor(d)

    assert stat.S_IMODE(os.stat(directory).st_mode) == 0o711


def test_producer_is_atomic_no_tmp_left_behind(tmp_path):
    d, pwfile = _descriptor(tmp_path)
    LanePasswordProvisioner().provision_from_descriptor(d)
    # No temp artifact lingers next to the final file.
    directory = os.path.dirname(pwfile)
    leftovers = [f for f in os.listdir(directory) if ".tmp." in f]
    assert leftovers == [], f"atomic write left temp files: {leftovers}"


@pytest.mark.parametrize("failure_point", ["replace", "chmod"])
def test_install_failure_leaves_no_final_or_temp_credential(
    tmp_path, monkeypatch, failure_point
):
    d, pwfile = _descriptor(tmp_path, uid=1001)
    real_replace = lane_password_module.os.replace
    real_chmod = lane_password_module.os.chmod

    def replace(src, dst):
        if failure_point == "replace" and dst == pwfile:
            raise OSError("replace failed")
        return real_replace(src, dst)

    def chmod(path, mode):
        if failure_point == "chmod" and path == pwfile:
            raise OSError("chmod failed")
        return real_chmod(path, mode)

    monkeypatch.setattr(lane_password_module.os, "replace", replace)
    monkeypatch.setattr(lane_password_module.os, "chmod", chmod)

    with pytest.raises(LanePasswordProvisionError) as exc_info:
        LanePasswordProvisioner(read_as_uid=lambda _path, _uid: None).provision_from_descriptor(d)

    message = str(exc_info.value)
    assert "client_a" in message
    assert pwfile in message
    assert "uid 1001" in message
    assert not os.path.exists(pwfile)
    assert list((tmp_path / "lane").glob("*.tmp.*")) == []


def test_teardown_unlinks_on_success(tmp_path):
    d, pwfile = _descriptor(tmp_path)
    prov = LanePasswordProvisioner()
    prov.provision_from_descriptor(d)
    assert os.path.exists(pwfile)
    prov.remove_all()
    # Proven by the filesystem, not an assertion on an internal flag: the file is GONE.
    assert not os.path.exists(pwfile)


def test_teardown_unlink_is_idempotent_even_after_external_removal(tmp_path):
    # Teardown must not raise if the file was already removed (a failure/abort path may
    # have unlinked it, or it was never written). remove_all stays silent.
    d, pwfile = _descriptor(tmp_path)
    prov = LanePasswordProvisioner()
    prov.provision_from_descriptor(d)
    os.unlink(pwfile)  # simulate an out-of-band removal on a failure path
    prov.remove_all()  # must not raise
    assert not os.path.exists(pwfile)


def test_remove_single_path_clears_tracking_and_file(tmp_path):
    d, pwfile = _descriptor(tmp_path)
    prov = LanePasswordProvisioner()
    prov.provision_from_descriptor(d)
    prov.remove(pwfile)
    assert not os.path.exists(pwfile)
    assert prov.written == []


def test_open_lane_no_file_no_password_required(tmp_path):
    # No client names a server_password_file => nothing produced, and lane_password is
    # NOT required. This is the legitimate open/no-password lane path.
    d, _ = _descriptor(tmp_path, password=None, with_file=False)
    prov = LanePasswordProvisioner()
    assert prov.provision_from_descriptor(d) == []
    assert prov.written == []


def test_fails_closed_when_file_named_but_no_password(tmp_path):
    # A client names a password file but the descriptor carries no lane_password: refuse to
    # write an empty credential the handshake would silently reject.
    d, _ = _descriptor(tmp_path, password=None, with_file=True)
    prov = LanePasswordProvisioner()
    with pytest.raises(LanePasswordProvisionError):
        prov.provision_from_descriptor(d)


def test_fails_closed_when_password_consumer_uid_is_undeclared(tmp_path):
    d, _ = _descriptor(tmp_path)
    del d["clients"][0]["uid"]
    with pytest.raises(LanePasswordProvisionError) as exc_info:
        LanePasswordProvisioner().provision_from_descriptor(d)
    assert "client_a" in str(exc_info.value)
    assert "uid" in str(exc_info.value)


def test_fails_closed_on_empty_password(tmp_path):
    d, _ = _descriptor(tmp_path, password="", with_file=True)
    prov = LanePasswordProvisioner()
    with pytest.raises(LanePasswordProvisionError):
        prov.provision_from_descriptor(d)


def test_password_value_never_appears_in_error_message(tmp_path):
    # Point the file path at a location whose parent is a FILE, forcing makedirs/open to
    # fail, and assert the raised error does not embed the secret.
    secret = "SUPER-SECRET-LANE-PW"
    blocker = tmp_path / "not-a-dir"
    blocker.write_text("x")
    pwfile = str(blocker / "sub" / "lane.txt")  # parent is a file → write fails
    d = {"clients": [{"actor": "client_a", "server_password_file": pwfile}], "lane_password": secret}
    prov = LanePasswordProvisioner()
    with pytest.raises(Exception) as ei:
        prov.provision_from_descriptor(d)
    assert secret not in str(ei.value)


def test_provisioned_record_repr_does_not_leak_password(tmp_path):
    # The tracking record stores only the path + actor, never the value.
    rec = ProvisionedLanePassword(path="/x/lane.txt", actor="client_a")
    assert "lane.txt" in repr(rec)
    # (No password field exists on the record at all.)
    assert not hasattr(rec, "password")


def test_refuses_relative_path(tmp_path):
    d = {"clients": [{"actor": "client_a", "server_password_file": "relative/lane.txt"}], "lane_password": "pw"}
    prov = LanePasswordProvisioner()
    with pytest.raises(LanePasswordProvisionError):
        prov.provision_from_descriptor(d)


def test_refuses_to_write_over_a_symlink(tmp_path):
    target = tmp_path / "real.txt"
    target.write_text("orig")
    link = tmp_path / "link.txt"
    os.symlink(str(target), str(link))
    d = {"clients": [{"actor": "client_a", "server_password_file": str(link)}], "lane_password": "pw"}
    prov = LanePasswordProvisioner()
    with pytest.raises(LanePasswordProvisionError):
        prov.provision_from_descriptor(d)
    # The symlink target is untouched.
    assert target.read_text() == "orig"


# --------------------------------------------------------------------------- #
# Composition wiring: real_operator_environment produces the file before launch
# and unlinks it on teardown (the producer+teardown wired into the run).
# --------------------------------------------------------------------------- #

def test_real_environment_provisions_then_cleans_up_password_file(tmp_path):
    import hashlib
    from runner_core.live_composition import real_operator_environment, RealOperatorConfig
    from runner_core.live_transport import ChannelEndpoint, EntitlementDeliveryConfig
    from runner_core.manifest import REQUIRED_PARTS

    pwfile = str(tmp_path / "lane" / "lane-password.txt")
    bootstrap_a = str(tmp_path / "boot" / "client_a.json")
    # A complete descriptor (wire+pins+lane) so the bootstrap provisioner runs too — this
    # proves the lane-password producer is wired ALONGSIDE the existing bootstrap producer,
    # and that BOTH are cleaned up together on teardown.
    descriptor = {
        "wire": {
            "nonce": "n", "expiry_unix_ms": 10_000_000, "hmac_secret": "s", "operator_token": "t",
        },
        "pins": {p: hashlib.sha256(p.encode()).hexdigest() for p in REQUIRED_PARTS},
        "lane": {"world_uid": 1, "world_name": "w"},
        "lane_password": "t009lproof",
        "clients": [
            {
                "actor": "client_a",
                "uid": os.geteuid(),
                "steam_id": "76561197965627562",
                "verbs": "Ping",
                "loopback_port": 48610,
                "bootstrap_path": bootstrap_a,
                "server_password_file": pwfile,
            }
        ],
    }
    config = RealOperatorConfig(
        server_binary="/bin/true",
        server_args=(),
        server_ready_log="/tmp/ready.log",
        server_ready_marker="Game server connected",
        client_binary="/lane/valheim.x86_64",
        adminlist_path="/tmp/adminlist.txt",
        entitlement_delivery=EntitlementDeliveryConfig(
            endpoint=ChannelEndpoint(host="127.0.0.1", port=1, role="Server"),
            operator_token="t",
            hmac_secret="s",
            nonce="n",
            world_uid=1,
            expiry_unix_ms=1,
        ),
    )
    env = real_operator_environment(config, descriptor=descriptor)

    # Producer: before launch the file exists at 0644 with the descriptor's password.
    # 0644 because the consuming client runs as a different uid (valbot) than the
    # runner that writes it; see test_producer_writes_password_file_mode_0644.
    env.provision_bootstraps()
    assert os.path.exists(pwfile)
    assert stat.S_IMODE(os.stat(pwfile).st_mode) == 0o644
    with open(pwfile, encoding="utf-8") as fh:
        assert fh.read().strip() == "t009lproof"

    # Teardown: cleanup_bootstraps unlinks the credential (proven by the filesystem).
    env.cleanup_bootstraps()
    assert not os.path.exists(pwfile)
