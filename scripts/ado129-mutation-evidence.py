#!/usr/bin/env python3
"""ADO #129 mutation evidence: prove each torn-frame test actually BITES.

A test that passes is worthless unless it can fail. This script temporarily mutates
the two production guards the new tests assert against, one at a time, per handler
file, and records whether NiflheimJournalCorruptionAllHandlersTests goes RED.

  Mutation A — FRAME layer: delete the `Crc32(payload) != crc` term from ReadDurable,
               so a CRC-invalid frame is accepted instead of truncating the read.
  Mutation B — RECORD layer: delete the field-count + record-tag guard from
               ParseRecord, so a well-framed garbage record is no longer rejected.

Every mutation is reverted before the next one runs (git checkout of the file).
Run from the repo root with the net8 toolchain (NOT ~/.dotnet-p5).
"""
import re
import subprocess
import sys
import pathlib

CMD = "Application/Commands"
FILES = [
    "PurchaseCommands.cs",
    "DevelopmentCommands.cs",
    "FacetCommands.cs",
    "ActivityCommands.cs",
    "LocalPolicyCommands.cs",
    "RelationshipCommands.cs",
]
ROOT = pathlib.Path("src/SBPR.Niflheim.HomesteadStones") / CMD
FILTER = "FullyQualifiedName~NiflheimJournalCorruptionAllHandlersTests"

CRC_TERM = " || Crc32(payload) != crc"
GUARD_RE = re.compile(r'^(\s*)if \(\(?parts\.Length[^\n]*?\) return null;\n', re.M)


def run_tests():
    p = subprocess.run(
        ["dotnet", "test", "tests/SBPR.Trailborne.Tests.csproj", "-c", "Release",
         "--filter", FILTER, "--nologo"],
        capture_output=True, text=True)
    out = p.stdout + p.stderr
    m = re.search(r"Failed:\s+(\d+), Passed:\s+(\d+)", out)
    if not m:
        return ("BUILD-ERROR", out[-600:])
    return (("RED" if int(m.group(1)) else "GREEN"), m.group(0))


def revert(path):
    subprocess.run(["git", "checkout", "--", str(path)], check=True)


def mutate_crc(path):
    t = path.read_text()
    if CRC_TERM not in t:
        return 0
    n = t.count(CRC_TERM)
    path.write_text(t.replace(CRC_TERM, ""))
    return n


def mutate_guard(path):
    t = path.read_text()
    new, n = GUARD_RE.subn(lambda m: "", t)
    if n:
        path.write_text(new)
    return n


def main():
    print("baseline:", run_tests())
    for f in FILES:
        path = ROOT / f
        for label, fn in (("A frame/CRC", mutate_crc), ("B record/field-count", mutate_guard)):
            n = fn(path)
            if n == 0:
                print(f"{f:26s} {label:22s} NO-SITE")
                continue
            verdict, detail = run_tests()
            print(f"{f:26s} {label:22s} sites={n} -> {verdict}   {detail}")
            revert(path)
    print("final (reverted):", run_tests())


if __name__ == "__main__":
    sys.exit(main())
