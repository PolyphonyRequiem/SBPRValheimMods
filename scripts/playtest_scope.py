#!/usr/bin/env python3
"""
playtest_scope.py — declarative, build-grounded project scope for playtest manifests.

Why this exists
---------------
The old `gen-playtest-guide.py` swept a single global `src/**/*.cs` universe. That
was fine when the repo held one mod. It now holds several independently-shipped
projects (Trailborne + its Core, HomesteadStones, resource-delivery, identity …).
A global sweep drags EVERY project's commits into Trailborne's playtest manifest,
so the ledger cross-check reported dozens of "unledgered" changes that are simply
a DIFFERENT mod's surface — noise that would either block a legitimate Trailborne
cut or train humans to ignore the guard.

This module makes scope declarative and grounds it in build reality:

  * A manifest names ONE ship project (the .csproj whose DLL/package ships).
  * The scope is that project PLUS its transitive `<ProjectReference>` closure —
    exactly the assemblies that end up in the shipped package (verified against
    scripts/pack-modpack.sh, which bundles SBPR.Trailborne.dll +
    SBPR.Trailborne.Core.dll and nothing else). We read the closure from the
    csproj graph, not from hand-typed path prefixes, so adding/removing a
    ProjectReference automatically moves the scope with the build.

  * Manifests are defined in scripts/playtest-manifests.yaml so a second manifest
    (Homestead, or a future mod) is a few declarative lines, never a fork of the
    tool.

Nothing here touches git history, tags, or the ledger. It only answers:
"which src files belong to manifest X's release closure?"
"""
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
MANIFESTS_FILE = REPO / "scripts/playtest-manifests.yaml"

# csproj <ProjectReference Include="..\Foo\Foo.csproj" /> — Windows-authored paths
# use backslashes; normalise to forward slashes before resolving.
_PROJREF_RE = re.compile(r'<ProjectReference\s+Include\s*=\s*"([^"]+)"', re.I)
_VERSION_RE = re.compile(r"<Version>\s*([0-9][0-9.]*)\s*</Version>")


def _load_yaml(path: Path):
    try:
        import yaml  # PyYAML is already a repo script dep (homestead extraction reqs)
    except Exception as e:  # pragma: no cover - environment guard
        sys.exit(f"playtest_scope: PyYAML required to read {path}: {e}")
    with open(path) as fh:
        return yaml.safe_load(fh) or {}


def csproj_project_refs(csproj_path: Path):
    """Direct <ProjectReference> targets of a csproj, as resolved repo-relative paths.

    Returns a list of Path objects pointing at the referenced .csproj files.
    Backslash-separated Windows paths are normalised. Paths are resolved relative
    to the referencing csproj's own directory (MSBuild semantics).
    """
    text = csproj_path.read_text()
    refs = []
    for raw in _PROJREF_RE.findall(text):
        rel = raw.replace("\\", "/")
        target = (csproj_path.parent / rel).resolve()
        refs.append(target)
    return refs


def dependency_closure(ship_csproj: Path):
    """Transitive ProjectReference closure of a ship project, including itself.

    Grounds "what ships" in the build graph: the ship DLL plus every project it
    (transitively) references — the exact set MSBuild copies next to the plugin
    DLL and that pack-modpack.sh bundles. Returns an ordered, de-duplicated list
    of resolved .csproj Paths (ship project first).
    """
    seen = {}
    order = []

    def visit(p: Path):
        key = str(p)
        if key in seen:
            return
        seen[key] = True
        order.append(p)
        for ref in csproj_project_refs(p):
            visit(ref)

    visit(ship_csproj.resolve())
    return order


def project_src_dir(csproj_path: Path) -> Path:
    """The directory that holds a project's source (the csproj's own directory)."""
    return csproj_path.parent


class ManifestScope:
    """Resolved scope for one playtest manifest.

    Attributes:
      name          manifest key (e.g. 'trailborne')
      ship_csproj   the .csproj whose package ships
      version       <Version> read from the ship csproj at the working tree
      projects      ordered list of .csproj Paths in the dependency closure
      pathspecs     git pathspecs (repo-relative, e.g. 'src/SBPR.Trailborne/**/*.cs')
                    covering every project's source in the closure
      ledger        Path to this manifest's playtest ledger
      guide_glob    template for the generated guide filename
      tag_prefix    e.g. 'v'  (playtest tags look like v0.2.41-playtest)
      tag_suffix    e.g. '-playtest'
    """

    def __init__(self, name, cfg):
        self.name = name
        self.ship_csproj = (REPO / cfg["ship_project"]).resolve()
        if not self.ship_csproj.exists():
            sys.exit(f"playtest_scope: manifest '{name}' ship_project not found: {self.ship_csproj}")
        self.projects = dependency_closure(self.ship_csproj)
        self.pathspecs = []
        for proj in self.projects:
            rel = project_src_dir(proj).relative_to(REPO).as_posix()
            # `:(glob)` forces git's glob magic so `**` matches across zero or
            # more directory levels — WITHOUT it, `dir/**/*.cs` misses files that
            # sit DIRECTLY under `dir` (git only expands `**/` between slashes for
            # non-magic pathspecs). That gap silently dropped top-level project
            # files from the scope; the glob prefix closes it.
            self.pathspecs.append(f":(glob){rel}/**/*.cs")
        self.ledger = REPO / cfg["ledger"]
        self.guide_template = cfg["guide_template"]  # e.g. docs/playtest/playtest-{n}-testers-guide.md
        self.tag_prefix = cfg.get("tag_prefix", "v")
        self.tag_suffix = cfg.get("tag_suffix", "-playtest")

    @property
    def version(self):
        m = _VERSION_RE.search(self.ship_csproj.read_text())
        return m.group(1) if m else None

    def project_names(self):
        return [p.parent.name for p in self.projects]

    def describe(self):
        lines = [f"manifest: {self.name}",
                 f"  ship project: {self.ship_csproj.relative_to(REPO).as_posix()}",
                 f"  version:      {self.version}",
                 f"  closure ({len(self.projects)} project(s)):"]
        for p in self.projects:
            lines.append(f"    - {p.relative_to(REPO).as_posix()}")
        lines.append("  pathspecs:")
        for ps in self.pathspecs:
            lines.append(f"    - {ps}")
        return "\n".join(lines)


def load_manifests(path: Path = MANIFESTS_FILE):
    cfg = _load_yaml(path)
    manifests = cfg.get("manifests", {})
    if not manifests:
        sys.exit(f"playtest_scope: no manifests defined in {path}")
    return {name: ManifestScope(name, mcfg) for name, mcfg in manifests.items()}


def get_manifest(name, path: Path = MANIFESTS_FILE):
    manifests = load_manifests(path)
    if name not in manifests:
        sys.exit(f"playtest_scope: unknown manifest '{name}'. Known: {', '.join(sorted(manifests))}")
    return manifests[name]


if __name__ == "__main__":
    # Introspection helper: `python3 scripts/playtest_scope.py [manifest]`
    which = sys.argv[1] if len(sys.argv) > 1 else None
    ms = load_manifests()
    for name, scope in ms.items():
        if which and name != which:
            continue
        print(scope.describe())
        print()
