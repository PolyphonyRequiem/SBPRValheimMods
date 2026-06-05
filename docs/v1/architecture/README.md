# v1 / architecture

Structural plans for the v1 codebase — how the mod's source is organized and why.

The current plan is the **vertical-slice `Features/`** layout: each gameplay
feature (Trailhead, Trailblazing, Signs, Pigments, Cairns) owns a folder with its
prefab registration, patches, and runtime behavior together, rather than splitting
by technical layer.

See [`index.md`](index.md) for the file manifest.
