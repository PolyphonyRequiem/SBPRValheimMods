# docs/design/research

Feasibility **spikes and investigations** that serve a design idea but are not themselves a spec.
These are point-in-time records (`status: historical`) — they capture what a throwaway experiment
proved about a *proposed* feature, before any version-scope or build decision has been made.

This sits under `design/` (not under a `v<semver>/`) on purpose: the ideas these notes serve are
pre-greenlight and not yet attached to a release. Once an idea is greenlit into a version, its
research note can be `git mv`'d into that version's `research/` tree (e.g. `docs/v7/research/`)
alongside the spec that absorbs it.

See [`index.md`](index.md) for the file manifest.

## What belongs here

- Throwaway-spike findings for a `design/`-tier idea (the experiment is disposable; the writeup
  records the verdict).
- Feasibility investigations that right-size a feature before it earns a version slot.

## What does NOT belong here

- Implementation investigations for an in-flight release → `docs/v<semver>/research/`.
- Living design intent → `docs/design/*.md` (the idea doc itself).
- Anything that ships code/recipes → that's a build, not research.
