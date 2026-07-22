#!/usr/bin/env python3
"""Read-only metadata drift probe for qa/decomp-map/VANILLA-BINDINGS.md.

Asserts that the vanilla method/field seams the SBPR.QaHarness.T022 helper binds
(ADR-0009 M2-M4) still exist with their pinned signatures in a given
assembly_valheim.dll. TOOLING ONLY: launches no game, deploys nothing, mutates
nothing, is not wired into the product build. It shells `ilspycmd` when present;
if a pre-produced decompile directory is supplied via --decomp it greps that
instead so it can run in CI without the decompiler.

Exit 0 = all pinned bindings present. Non-zero = drift; the relevant M-card must
re-pin against VANILLA-BINDINGS.md before relying on the map.

Usage:
  python3 qa/decomp-map/probe_vanilla_bindings.py --assembly /path/to/assembly_valheim.dll
  python3 qa/decomp-map/probe_vanilla_bindings.py --decomp /tmp/decomp   # grep a decompile dir
"""
from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import subprocess
import sys

# (type, distinctive signature substring that must appear in the type's decompile)
# Substrings are chosen to be resilient to whitespace but specific to the member.
BINDINGS: list[tuple[str, str, str]] = [
    ("ZNet", "public long GetWorldUID()", "world UID arming (§3.1)"),
    ("ZNet", "public string GetWorldName()", "world name arming (§3.1)"),
    ("ZNet", "public bool IsServer()", "role/server check (§3.1)"),
    ("ZNet", "private void OnNewConnection(ZNetPeer peer)", "per-peer RPC hook (§3.4)"),
    ("ZNet", "public ZNetPeer GetServerPeer()", "client->server peer (§3.4)"),
    ("ZNet", "public List<ZNetPeer> GetPeers()", "peer enumeration (§3.4)"),
    ("ZNetPeer", "public ZRpc m_rpc", "per-peer rpc field (§3.4)"),
    ("ZNetPeer", "public long m_uid", "peer uid field (§3.4)"),
    ("ZRpc", "public ISocket GetSocket()", "delivering-peer socket (§3.4)"),
    ("ZRpc", "public void Invoke(string method, params object[] parameters)", "rpc invoke (§3.4)"),
    ("ZNetScene", "public GameObject GetPrefab(int hash)", "blueprint read (§3.5)"),
    ("ZNetScene", "public void Destroy(GameObject go)", "network despawn/cleanup (§3.5)"),
    ("ObjectDB", "public GameObject GetItemPrefab(string name)", "item prefab lookup (§3.5)"),
    ("ItemDrop", "public static ItemDrop DropItem(ItemData item, int amount, Vector3 position, Quaternion rotation)", "world item spawn/drop (§3.5/§3.7)"),
    ("CraftingStation", "public static CraftingStation FindClosestStationInRange(string name, Vector3 point, float range)", "station discovery (§3.5)"),
    ("InventoryGui", "private void DoCrafting(Player player)", "craft/upgrade issuance seam (§3.6)"),
    ("InventoryGui", "private void OnCraftPressed()", "craft trigger (§3.6)"),
    ("InventoryGui", "public static InventoryGui instance", "gui singleton (§3.6)"),
    ("Player", "public CraftingStation GetCurrentCraftingStation()", "current station (§3.6)"),
    ("Humanoid", "public Inventory GetInventory()", "inventory access (§3.10; declared on Humanoid, inherited by Player)"),
    ("Humanoid", "public bool DropItem(Inventory inventory, ItemDrop.ItemData item, int amount)", "drop continuity (§3.7)"),
    ("Humanoid", "public bool Pickup(GameObject go", "pickup continuity (§3.7)"),
    ("ItemDrop", "public string GetTooltip(int stackOverride", "tooltip observation (§3.8)"),
    ("ItemDrop", "public Dictionary<string, string> m_customData", "tamper surface (§3.9)"),
    ("Terminal", "public void TryRunCommand(string text", "do-not-bind lock marker (§3.3)"),
    ("Inventory", "public ItemDrop.ItemData GetItem(int index)", "item read (§3.10)"),
]

# Version constants pinned in VANILLA-BINDINGS.md §1.
PINNED_VERSION = "new GameVersion(0, 221, 12)"
PINNED_NETWORK = "m_networkVersion = 36u"

# Known-good SHA-256 pins from §1 (informational; drift here is a warning, not fatal,
# because a re-download can differ while signatures still match).
KNOWN_SHA = {
    "ae98afc3a65ccb2e6c744397bb692287cf2c1527877d002e90307a33f3d917ee": "client 0.221.12 (Trailborne-Modded GUI)",
    "f26465c6c5b8d1883deac13a1d001054a5f5aedd84fb54644d3fbb36550564ba": "server 0.221.12 (niflheim dedicated dl)",
}


def norm(s: str) -> str:
    return " ".join(s.split())


def decompile_type(assembly: str, typ: str, env: dict) -> str:
    out = subprocess.run(
        ["ilspycmd", "-t", typ, assembly],
        capture_output=True, text=True, env=env,
    )
    return out.stdout


def load_from_dir(decomp_dir: str, typ: str) -> str:
    for candidate in (f"{typ}.cs", f"srv_{typ}.cs", f"{typ.replace('.', '_')}.cs"):
        p = os.path.join(decomp_dir, candidate)
        if os.path.exists(p):
            with open(p, encoding="utf-8") as fh:
                return fh.read()
    return ""


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--assembly", help="path to assembly_valheim.dll (uses ilspycmd)")
    ap.add_argument("--decomp", help="path to a directory of pre-produced *.cs decompiles")
    args = ap.parse_args()

    if not args.assembly and not args.decomp:
        ap.error("provide --assembly (needs ilspycmd) or --decomp <dir>")

    env = dict(os.environ)
    env.setdefault("DOTNET_ROLL_FORWARD", "Major")
    env.setdefault("DOTNET_CLI_TELEMETRY_OPTOUT", "1")

    if args.assembly:
        if not os.path.exists(args.assembly):
            print(f"FATAL: assembly not found: {args.assembly}", file=sys.stderr)
            return 2
        if not shutil.which("ilspycmd"):
            print("FATAL: ilspycmd not on PATH. Install: dotnet tool install --global ilspycmd "
                  "(then export PATH=$PATH:~/.dotnet/tools), or pass --decomp.", file=sys.stderr)
            return 2
        sha = hashlib.sha256(open(args.assembly, "rb").read()).hexdigest()
        label = KNOWN_SHA.get(sha)
        if label:
            print(f"[sha] {sha[:16]}… matches pinned {label}")
        else:
            print(f"[sha] WARNING: {sha[:16]}… is not a pinned build "
                  f"(re-verify version constants below before trusting).")

    # Cache decompiles per type to avoid re-running ilspycmd.
    cache: dict[str, str] = {}

    def source_for(typ: str) -> str:
        if typ in cache:
            return cache[typ]
        text = (load_from_dir(args.decomp, typ) if args.decomp
                else decompile_type(args.assembly, typ, env))
        cache[typ] = text
        return text

    failures: list[str] = []
    for typ, sig, why in BINDINGS:
        src = source_for(typ)
        if not src:
            failures.append(f"[missing-type] {typ} — could not decompile ({why})")
            continue
        if norm(sig) in norm(src):
            print(f"[ok] {typ}: {sig}")
        else:
            failures.append(f"[drift] {typ}: expected `{sig}`  ({why})")

    # Version constant check via the Version type.
    ver = source_for("Version")
    if ver:
        if norm(PINNED_VERSION) in norm(ver):
            print(f"[ok] Version: {PINNED_VERSION}")
        else:
            failures.append(f"[drift] Version: expected `{PINNED_VERSION}` (game version moved — re-pin §1)")
        if norm(PINNED_NETWORK) in norm(ver):
            print(f"[ok] Version: {PINNED_NETWORK}")
        else:
            failures.append(f"[drift] Version: expected `{PINNED_NETWORK}`")

    print()
    if failures:
        print(f"DRIFT DETECTED ({len(failures)}):")
        for f in failures:
            print("  " + f)
        print("\nRe-pin qa/decomp-map/VANILLA-BINDINGS.md against the new build "
              "before any M2-M4 card relies on these seams.")
        return 1
    print(f"All {len(BINDINGS)} bindings + version constants present. No drift.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
