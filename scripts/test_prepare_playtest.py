#!/usr/bin/env python3
"""
test_prepare_playtest.py — deterministic tests for the Prepare Playtest tooling.

Runs with stdlib unittest (no pip deps beyond PyYAML, already a repo script dep):

    python3 scripts/test_prepare_playtest.py

Builds a throwaway git repo in a temp dir with a two-project dependency closure
(Ship → Core) plus an unrelated sibling project (Other), tags a base "release",
then drives scripts/prepare-playtest.py against it with --no-fetch for hermetic,
network-free, deterministic runs. Covers: scope isolation, dependency closure,
exact-ref identity, unledgered rejection, exemptions, stale-version rejection,
stale-guide rejection, and no-side-effect dry-run.
"""
import json
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent
PREPARE = SCRIPTS / "prepare-playtest.py"
SCOPE = SCRIPTS / "playtest_scope.py"


def run(*args, cwd, env=None):
    e = dict(os.environ)
    if env:
        e.update(env)
    return subprocess.run(args, cwd=cwd, capture_output=True, text=True, env=e)


class Fixture:
    """A hermetic git repo mirroring the real scope shape."""

    def __init__(self, root: Path):
        self.root = root
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.email", "t@t")
        self.git("config", "user.name", "t")
        self.git("config", "commit.gpgsign", "false")
        # project layout
        self._proj("src/Ship/Ship.csproj", version="0.2.40",
                   refs=["..\\Core\\Core.csproj"])
        self._proj("src/Core/Core.csproj", version=None, refs=[])
        self._proj("src/Other/Other.csproj", version="1.0.0", refs=[])
        self.write("src/Ship/A.cs", "// ship\n")
        self.write("src/Core/C.cs", "// core\n")
        self.write("src/Other/O.cs", "// other\n")
        # manifest config pointing at Ship
        self.write("scripts/playtest-manifests.yaml", textwrap.dedent("""\
            manifests:
              test:
                ship_project: src/Ship/Ship.csproj
                ledger: docs/ledger.md
                guide_template: docs/guide-{n}.md
                tag_prefix: v
                tag_suffix: -playtest
        """))
        # copy the real tool + scope module into the fixture so REPO resolves here
        (self.root / "scripts").mkdir(parents=True, exist_ok=True)
        (self.root / "scripts/prepare-playtest.py").write_text(PREPARE.read_text())
        (self.root / "scripts/playtest_scope.py").write_text(SCOPE.read_text())
        self._ledger(pending="_(empty)_", counter=1, base_tag="v0.2.40-playtest")
        self.git("add", "-A")
        self.git("commit", "-qm", "base")
        self.git("tag", "v0.2.40-playtest")

    def git(self, *a):
        r = run("git", *a, cwd=self.root)
        if r.returncode != 0:
            raise RuntimeError(f"git {a} failed: {r.stderr}")
        return r.stdout.strip()

    def write(self, rel, content):
        p = self.root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(content)

    def _proj(self, rel, version, refs):
        refblock = "".join(
            f'    <ProjectReference Include="{r}" />\n' for r in refs)
        ver = f"    <Version>{version}</Version>\n" if version else ""
        self.write(rel, f"<Project>\n  <PropertyGroup>\n{ver}  </PropertyGroup>\n"
                        f"  <ItemGroup>\n{refblock}  </ItemGroup>\n</Project>\n")

    def _ledger(self, pending, counter, base_tag):
        self.write("docs/ledger.md", textwrap.dedent(f"""\
            ---
            title: "Ledger"
            playtest_counter: {counter}
            last_playtest_tag: {base_tag}
            ---

            # Ledger

            ## PENDING

            {pending}
        """))

    def set_ship_version(self, v):
        self.write("src/Ship/Ship.csproj",
                   f"<Project>\n  <PropertyGroup>\n    <Version>{v}</Version>\n"
                   f"  </PropertyGroup>\n  <ItemGroup>\n"
                   f'    <ProjectReference Include="..\\Core\\Core.csproj" />\n'
                   f"  </ItemGroup>\n</Project>\n")

    def commit(self, msg, files):
        for rel, content in files.items():
            self.write(rel, content)
        self.git("add", "-A")
        self.git("commit", "-qm", msg)

    def prepare(self, *extra):
        return run(sys.executable, "scripts/prepare-playtest.py",
                   "--manifest", "test", "--ref", "main", "--no-fetch", "--json",
                   *extra, cwd=self.root, env={"PYTHONDONTWRITEBYTECODE": "1"})

    def verdict(self, *extra):
        r = self.prepare(*extra)
        return json.loads(r.stdout), r.returncode


class PreparePlaytestTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.fx = Fixture(Path(self._tmp.name))

    def tearDown(self):
        self._tmp.cleanup()

    def test_scope_isolation_sibling_excluded(self):
        # a change to the unrelated Other project must NOT enter Ship's manifest.
        self.fx.commit("feat(other): unrelated", {"src/Other/O.cs": "// changed\n"})
        v, _ = self.fx.verdict()
        self.assertEqual(v["scoped_changes"], 0,
                         "sibling project change leaked into manifest scope")
        self.assertNotIn("Other", v["project_scope"])

    def test_dependency_closure_includes_core(self):
        v, _ = self.fx.verdict()
        self.assertIn("Ship", v["project_scope"])
        self.assertIn("Core", v["project_scope"])
        # a Core change IS in scope (it ships in Ship's package).
        self.fx.commit("feat(core): change", {"src/Core/C.cs": "// changed\n"})
        v, _ = self.fx.verdict()
        self.assertEqual(v["scoped_changes"], 1)

    def test_exact_ref_identity(self):
        head = self.fx.git("rev-parse", "HEAD")
        tree = self.fx.git("rev-parse", "HEAD^{tree}")
        v, _ = self.fx.verdict()
        self.assertEqual(v["commit"], head)
        self.assertEqual(v["tree"], tree)
        self.assertEqual(v["base_tag"], "v0.2.40-playtest")

    def test_unledgered_change_blocks(self):
        self.fx.set_ship_version("0.2.41")
        self.fx.commit("feat(ship): new thing t_11111111", {"src/Ship/A.cs": "// v2\n"})
        v, rc = self.fx.verdict()
        self.assertEqual(v["verdict"], "BLOCKED")
        self.assertEqual(rc, 2)
        self.assertEqual(len(v["unledgered"]), 1)

    def test_ledgered_change_passes(self):
        self.fx.set_ship_version("0.2.41")
        self.fx._ledger(pending="- t_11111111 new thing", counter=1,
                        base_tag="v0.2.40-playtest")
        self.fx.commit("feat(ship): new thing t_11111111", {"src/Ship/A.cs": "// v2\n"})
        v, rc = self.fx.verdict()
        self.assertEqual(v["unledgered"], [])
        self.assertEqual(v["verdict"], "READY", msg=str(v["blockers"]))
        self.assertEqual(rc, 0)
        self.assertEqual(v["proposed_tag"], "v0.2.41-playtest")

    def test_exempt_type_needs_no_ledger(self):
        self.fx.set_ship_version("0.2.41")
        self.fx.commit("chore(ship): tidy", {"src/Ship/A.cs": "// tidy\n"})
        v, rc = self.fx.verdict()
        self.assertEqual(v["unledgered"], [])
        self.assertEqual(len(v["exempt"]), 1)
        self.assertEqual(v["verdict"], "READY", msg=str(v["blockers"]))

    def test_no_card_no_pr_direct_push_blocks_without_sha(self):
        # a feat pushed straight to main (no t_ card, no trailing (#NNN)) is
        # unrepresentable by PR/card rescue — must block until its SHA is named.
        self.fx.set_ship_version("0.2.41")
        self.fx._ledger(pending="- some prose naming PR #294 but not the sha",
                        counter=1, base_tag="v0.2.40-playtest")
        self.fx.commit("feat(core): P0 direct push", {"src/Core/C.cs": "// v2\n"})
        v, rc = self.fx.verdict()
        self.assertEqual(v["verdict"], "BLOCKED")
        self.assertEqual(len(v["unledgered"]), 1)

    def test_no_card_no_pr_direct_push_sha_rescue_passes(self):
        # naming the commit's own SHA in PENDING ledgers a direct-to-main push.
        self.fx.set_ship_version("0.2.41")
        self.fx.commit("feat(core): P0 direct push", {"src/Core/C.cs": "// v2\n"})
        sha = self.fx.git("rev-parse", "--short", "HEAD")
        self.fx._ledger(pending=f"- P0 core seam (direct push, {sha})",
                        counter=1, base_tag="v0.2.40-playtest")
        self.fx.git("commit", "-aqm", "ledger names the sha")
        v, rc = self.fx.verdict()
        self.assertEqual(v["unledgered"], [], msg=str(v["unledgered"]))
        self.assertEqual(v["verdict"], "READY", msg=str(v["blockers"]))
        self.assertEqual(rc, 0)

    def test_sha_rescue_does_not_rescue_idcarrying_commit(self):
        # an id-carrying commit whose card id is ABSENT from PENDING stays flagged
        # even if some SHA is named — the rescue is strict to no-card-id commits.
        self.fx.set_ship_version("0.2.41")
        self.fx.commit("feat(ship): thing t_22222222", {"src/Ship/A.cs": "// v2\n"})
        sha = self.fx.git("rev-parse", "--short", "HEAD")
        self.fx._ledger(pending=f"- unrelated note mentioning {sha}",
                        counter=1, base_tag="v0.2.40-playtest")
        self.fx.git("commit", "-aqm", "ledger")
        v, rc = self.fx.verdict()
        self.assertEqual(v["verdict"], "BLOCKED")
        self.assertEqual(len(v["unledgered"]), 1)

    def test_stale_version_blocks(self):
        # ship version still equals released base → must block.
        self.fx.commit("feat(ship): thing t_11111111", {"src/Ship/A.cs": "// v2\n"})
        self.fx._ledger(pending="- t_11111111 thing", counter=1,
                        base_tag="v0.2.40-playtest")
        self.fx.git("commit", "-aqm", "ledger")
        v, rc = self.fx.verdict()
        self.assertEqual(v["verdict"], "BLOCKED")
        self.assertTrue(any("not greater than the released base" in b for b in v["blockers"]))

    def test_stale_guide_version_blocks(self):
        self.fx.set_ship_version("0.2.41")
        self.fx._ledger(pending="- t_11111111 thing", counter=1,
                        base_tag="v0.2.40-playtest")
        self.fx.write("docs/guide-1.md",
                      "**Build:** SBPR Trailborne 0.2.40 (current main)\n")
        self.fx.commit("feat(ship): thing t_11111111", {"src/Ship/A.cs": "// v2\n"})
        v, rc = self.fx.verdict()
        self.assertEqual(v["verdict"], "BLOCKED")
        self.assertTrue(any("labels Build 0.2.40" in b for b in v["blockers"]))

    def test_dry_run_no_side_effects(self):
        before = self.fx.git("status", "--porcelain")
        before_tags = self.fx.git("tag")
        before_ledger = (self.fx.root / "docs/ledger.md").read_text()
        self.fx.prepare()
        self.assertEqual(self.fx.git("status", "--porcelain"), before,
                         "dry run mutated the working tree")
        self.assertEqual(self.fx.git("tag"), before_tags, "dry run created a tag")
        self.assertEqual((self.fx.root / "docs/ledger.md").read_text(), before_ledger,
                         "dry run mutated the ledger")

    def test_write_guide_does_not_touch_ledger(self):
        self.fx.set_ship_version("0.2.41")
        self.fx._ledger(pending="- t_11111111 thing", counter=1,
                        base_tag="v0.2.40-playtest")
        self.fx.commit("feat(ship): thing t_11111111", {"src/Ship/A.cs": "// v2\n"})
        ledger_before = (self.fx.root / "docs/ledger.md").read_text()
        out = self.fx.root / "candidate-guide.md"
        r = run(sys.executable, "scripts/prepare-playtest.py", "--manifest", "test",
                "--ref", "main", "--no-fetch", "--write-guide", str(out),
                cwd=self.fx.root)
        self.assertTrue(out.exists(), "guide not written")
        gtext = out.read_text()
        self.assertIn("0.2.41", gtext)
        self.assertNotIn("**Build:** SBPR Trailborne 0.2.40", gtext,
                         "candidate guide reused the released base version")
        self.assertEqual((self.fx.root / "docs/ledger.md").read_text(), ledger_before,
                         "--write-guide mutated the living ledger")


if __name__ == "__main__":
    unittest.main(verbosity=2)
