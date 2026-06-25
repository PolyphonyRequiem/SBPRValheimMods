// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone whitelist PARSER (engine-free)
// ----------------------------------------------------------------------------
//  Design   : docs/design/seers-stone.md + docs/design/seers-stone-pickable-whitelist.md
//
//  WHY THIS IS HAND-PARSED, NOT YamlDotNet (the dependency we DON'T take).
//  The whitelist schema is deliberately a FLAT, comment-friendly list under three
//  section keys — that's all the owner ever edits. Parsing it needs only: skip
//  comments/blanks, read "key:" section headers, read "  - value  # note" list
//  items. That is a dozen lines of string handling. Pulling in a full YAML
//  serializer (YamlDotNet) to read a flat allow-list would mean ILRepack
//  /internalize-ing a third-party assembly into the mod DLL purely to avoid the
//  version-collision footgun it itself introduces — cost with no benefit at this
//  schema complexity. Daniel's load-bearing concern was assembly-load version
//  collisions from shipped libs; the cleanest answer to "don't collide" is "don't
//  ship the lib." This parser is YAML-SUBSET-compatible (an owner CAN open it in a
//  YAML editor and it stays valid), so if the schema ever grows nested structure we
//  can swap to YamlDotNet without changing the file format — but until it needs
//  that, we carry zero serialization dependency.
//
//  Engine-free (no UnityEngine / Valheim / third-party refs) so
//  tests/SeersStoneWhitelistParseTests.cs gates the parse surface headless — the
//  comment-stripping, section routing, and (Clone)-normalization the runtime relies
//  on cannot silently regress.
//
//  Clean-side (ADR-0001): all SBPR-authored; references no vanilla or third-party type.
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// The parsed whitelist: three named lists (pickables / ore_surface / locations) plus the
    /// flattened, normalized <see cref="AllNames"/> union the eligibility core consumes. Parsing
    /// is a forgiving line scan of our flat schema — NOT a full YAML parse (see the file header for
    /// why we deliberately carry no serializer dependency). Unknown section keys are ignored (so a
    /// future-format file from a newer mod version degrades gracefully), and any "- item" lines
    /// before the first recognized section are dropped rather than crashing.
    /// </summary>
    public sealed class WhitelistDocument
    {
        public IReadOnlyList<string> Pickables { get; }
        public IReadOnlyList<string> OreSurface { get; }
        public IReadOnlyList<string> Locations { get; }

        /// <summary>Schema version from the "version:" scalar (1 if absent/unparseable).</summary>
        public int Version { get; }

        /// <summary>
        /// The flattened union of all three sections, each name normalized through the SAME
        /// <see cref="SeersStoneEligibility.Normalize"/> the runtime lookups use (one normalization,
        /// no author-time/match-time drift). Duplicates collapse. This is what
        /// <c>new SeersStoneEligibility(doc.AllNames)</c> is built from.
        /// </summary>
        public IReadOnlyList<string> AllNames { get; }

        private WhitelistDocument(int version, List<string> pickables, List<string> ore, List<string> locations)
        {
            Version = version;
            Pickables = pickables;
            OreSurface = ore;
            Locations = locations;

            var all = new List<string>(pickables.Count + ore.Count + locations.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var list in new[] { pickables, ore, locations })
            {
                foreach (var raw in list)
                {
                    var n = SeersStoneEligibility.Normalize(raw);
                    if (n.Length != 0 && seen.Add(n)) all.Add(n);
                }
            }
            AllNames = all;
        }

        // Recognized top-level section keys. Anything else ("version:", an unknown future key)
        // is not a list section, so list items don't route into it.
        private const string SecPickables = "pickables";
        private const string SecOre = "ore_surface";
        private const string SecLocations = "locations";

        /// <summary>
        /// Parse the flat whitelist text. Forgiving by design: '#'-comments (whole-line and
        /// trailing) and blank lines are skipped; "key:" lines switch the active section; "- value"
        /// lines append the value (raw, un-normalized — normalization happens once in the ctor) to
        /// the active section. Leading indentation is irrelevant. A null/empty input yields an
        /// all-empty document (⇒ no wisps, the fail-safe).
        /// </summary>
        public static WhitelistDocument Parse(string? text)
        {
            var pickables = new List<string>();
            var ore = new List<string>();
            var locations = new List<string>();
            int version = 1;
            List<string>? active = null;

            if (string.IsNullOrEmpty(text))
                return new WhitelistDocument(version, pickables, ore, locations);

            foreach (var rawLine in text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    // A list item: "- Value". Strip the dash + surrounding space.
                    var value = line.Substring(1).Trim();
                    if (value.Length != 0 && active != null)
                        active.Add(value);
                    continue;
                }

                // Otherwise it's a "key:" or "key: value" line.
                var colon = line.IndexOf(':');
                if (colon < 0)
                {
                    // Not a key line and not a list item — ignore (forgiving).
                    continue;
                }

                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var inlineValue = line.Substring(colon + 1).Trim();

                switch (key)
                {
                    case "version":
                        if (int.TryParse(inlineValue, out var v)) version = v;
                        active = null; // version is a scalar, not a list section
                        break;
                    case SecPickables: active = pickables; break;
                    case SecOre: active = ore; break;
                    case SecLocations: active = locations; break;
                    default:
                        // Unknown key — not a list section. Park active so stray items don't
                        // mis-route into the previous section.
                        active = null;
                        break;
                }
            }

            return new WhitelistDocument(version, pickables, ore, locations);
        }

        /// <summary>
        /// Strip a trailing '#'-comment from a line. Our schema has no quoted '#' literals (prefab
        /// names are bare identifiers), so a plain first-'#' cut is correct and avoids needing a
        /// quote-state machine. A whole-line comment ("# ...") becomes empty and is skipped by the
        /// caller's Trim()/length check.
        /// </summary>
        private static string StripComment(string line)
        {
            var hash = line.IndexOf('#');
            return hash < 0 ? line : line.Substring(0, hash);
        }
    }
}
