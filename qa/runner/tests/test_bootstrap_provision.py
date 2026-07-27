"""Unit coverage for descriptor-derived bootstrap-doc provisioning (M6-LAUNCHENV).

`t_2a954860` found the bootstrap docs were hand-authored operator inputs the runner
never wrote — so they went stale silently (helper hash `8436e740` pinned against a
deployed `135f6029`). These tests assert the provisioner now EMITS each doc from the
descriptor (nothing fabricated), writes it mode 0600 (it carries the HMAC secret +
operator token), and removes it on teardown.
"""
from __future__ import annotations

import json
import os
import stat

import pytest

from runner_core.bootstrap_provision import (
    BootstrapProvisioner,
    BootstrapProvisionError,
    build_bootstrap_doc,
)
from runner_core.manifest import REQUIRED_PARTS

_WIRE = {
    "nonce": "05f1f23e80076a93159f07028d7160fb",
    "expiry_unix_ms": 1785187338221,
    "operator_token": "87f16bf3560c508f4d6c5cfc5a9d478bd689c45131304a09",
    "hmac_secret": "4866e1b3f198fc64b01ebec42b7e40f494d3d0c61f428745fb17ffea73ae471a",
}
_LANE = {"world_uid": -898655635, "world_name": "homesteadt009l"}
_PINS = {p: (str(i) * 64)[:64] for i, p in enumerate(REQUIRED_PARTS, start=1)}


def _descriptor(tmp_path, **client_overrides):
    a = {
        "actor": "client_a",
        "role": "Client",
        "verbs": "Craft,UpgradeItem,ReadInventory,Ping,Cleanup,Disarm",
        "loopback_port": 48610,
        "bootstrap_path": str(tmp_path / "qa-artifacts" / "t022-bootstrap-client_a.json"),
    }
    a.update(client_overrides)
    b = {
        "actor": "client_b",
        "role": "Client",
        "verbs": "Craft,UpgradeItem,ReadInventory,Ping,Cleanup,Disarm",
        "loopback_port": 48611,
        "bootstrap_path": str(tmp_path / "qa-artifacts" / "t022-bootstrap-client_b.json"),
    }
    return {"wire": _WIRE, "pins": _PINS, "lane": _LANE, "clients": [a, b]}


# --------------------------------------------------------------------------- #
# build_bootstrap_doc copies descriptor values verbatim — nothing invented.
# --------------------------------------------------------------------------- #

def test_build_doc_copies_descriptor_fields_verbatim() -> None:
    doc = build_bootstrap_doc(
        role="Client", actor="client_a", wire=_WIRE, pins=_PINS, lane=_LANE,
        verbs="Craft,Ping", loopback_port=48610,
    )
    assert doc["enabled"] == 1
    assert doc["role"] == "Client"
    assert doc["actor"] == "client_a"
    assert doc["worldUid"] == _LANE["world_uid"]
    assert doc["worldName"] == _LANE["world_name"]
    assert doc["nonce"] == _WIRE["nonce"]
    assert doc["expiry"] == _WIRE["expiry_unix_ms"]
    assert doc["hmacSecret"] == _WIRE["hmac_secret"]
    assert doc["operatorToken"] == _WIRE["operator_token"]
    assert doc["loopbackPort"] == 48610
    assert doc["verbs"] == "Craft,Ping"
    # Exactly the six pinned parts, copied straight from the descriptor pins.
    assert doc["hashes"] == {p: _PINS[p] for p in REQUIRED_PARTS}


def test_build_doc_fails_closed_on_missing_wire_field() -> None:
    bad_wire = dict(_WIRE)
    del bad_wire["hmac_secret"]
    with pytest.raises(BootstrapProvisionError):
        build_bootstrap_doc(role="Client", actor="a", wire=bad_wire, pins=_PINS,
                            lane=_LANE, verbs="Ping", loopback_port=1)


def test_build_doc_fails_closed_on_missing_pin_part() -> None:
    bad_pins = {p: _PINS[p] for p in REQUIRED_PARTS if p != "helper"}
    with pytest.raises(BootstrapProvisionError):
        build_bootstrap_doc(role="Client", actor="a", wire=_WIRE, pins=bad_pins,
                            lane=_LANE, verbs="Ping", loopback_port=1)


def test_build_doc_fails_closed_on_empty_verbs() -> None:
    with pytest.raises(BootstrapProvisionError):
        build_bootstrap_doc(role="Client", actor="a", wire=_WIRE, pins=_PINS,
                            lane=_LANE, verbs="", loopback_port=1)


# --------------------------------------------------------------------------- #
# The provisioner writes 0600 secret-bearing docs and removes them on teardown.
# --------------------------------------------------------------------------- #

def test_provision_writes_a_0600_doc_per_client(tmp_path) -> None:
    descriptor = _descriptor(tmp_path)
    prov = BootstrapProvisioner()
    written = prov.provision_from_descriptor(descriptor)
    assert sorted(p.actor for p in written) == ["client_a", "client_b"]
    for client in descriptor["clients"]:
        path = client["bootstrap_path"]
        assert os.path.isfile(path)
        # 0600 — the doc carries the HMAC secret and operator token.
        assert stat.S_IMODE(os.stat(path).st_mode) == 0o600
        doc = json.load(open(path))
        assert doc["hmacSecret"] == _WIRE["hmac_secret"]
        assert doc["operatorToken"] == _WIRE["operator_token"]
        assert doc["hashes"] == {p: _PINS[p] for p in REQUIRED_PARTS}


def test_provisioned_doc_matches_the_wire_block_it_derives_from(tmp_path) -> None:
    # The stale-doc failure mode was a doc whose nonce/hashes no longer matched the
    # descriptor. An emitted doc cannot drift: assert byte-equality of the crypto fields.
    descriptor = _descriptor(tmp_path)
    BootstrapProvisioner().provision_from_descriptor(descriptor)
    doc = json.load(open(descriptor["clients"][0]["bootstrap_path"]))
    assert doc["nonce"] == descriptor["wire"]["nonce"]
    assert doc["expiry"] == descriptor["wire"]["expiry_unix_ms"]


def test_remove_all_clears_secret_bearing_docs(tmp_path) -> None:
    descriptor = _descriptor(tmp_path)
    prov = BootstrapProvisioner()
    prov.provision_from_descriptor(descriptor)
    prov.remove_all()
    for client in descriptor["clients"]:
        assert not os.path.exists(client["bootstrap_path"])
    assert prov.written == []


def test_provision_fails_closed_when_a_client_lacks_bootstrap_path(tmp_path) -> None:
    descriptor = _descriptor(tmp_path)
    del descriptor["clients"][0]["bootstrap_path"]
    with pytest.raises(BootstrapProvisionError):
        BootstrapProvisioner().provision_from_descriptor(descriptor)


def test_provision_refuses_relative_bootstrap_path(tmp_path) -> None:
    descriptor = _descriptor(tmp_path, bootstrap_path="relative/boot.json")
    with pytest.raises(BootstrapProvisionError):
        BootstrapProvisioner().provision_from_descriptor(descriptor)
