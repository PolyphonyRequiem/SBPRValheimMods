# index — docs/v2/investigations

Spike findings and de-risking investigations for the v2 (Black Forest)
cartography tier. These are point-in-time proofs, not living specs — each
carries a `verdict` in its frontmatter that feeds the impl-card planning.

| file | verdict | purpose |
|------|---------|---------|
| 2026-06-10-bounded-map-ui-fork-spike.md | GO-WITH-CAVEATS | Can we render a bounded, fixed-zoom 1000 m map view from our OWN windowed fog array (not vanilla's full 256²)? Retires the tier's biggest unknown. (card t_e8bbbe48) |
