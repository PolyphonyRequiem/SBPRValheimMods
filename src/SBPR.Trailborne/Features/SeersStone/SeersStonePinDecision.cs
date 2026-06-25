// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone PIN-BY-LOOK decision (engine-free)
// ----------------------------------------------------------------------------
//  Design   : docs/design/seers-stone.md §pin-by-look — Daniel, 2026-06-25:
//    Look at a wisp, press Alt+E → the thing you're looking at goes onto your map.
//    • Pickable  → one pin for the whole PATCH (the "abundance pin"): the wisp is the
//      spawn-time group aggregate, so pinning the wisp pins the group as one pin.
//    • Location  → DiscoverLocation (the vanilla "shown on map" path).
//    • NO COUNT on the pin label (Daniel: "Just 'Blueberries' etc., the count's not
//      needed"). The pin is a frozen MEMORY ("Blueberries"); the wisp is the live eye.
//    • Pins default PRIVATE (the lens is a personal instrument; pin-sharing.md §2).
//    • Pin-merge: a same-name pin already within merge-radius ⇒ don't add a duplicate.
//
//  WHY ENGINE-FREE. The "what pin should this hit produce, and should it merge with an
//  existing one?" logic is pure: (hit name, kind, eligibility, existing pins) → a
//  PinPlan or "no pin". Keeping it free of UnityEngine/Minimap lets
//  tests/SeersStonePinDecisionTests.cs gate the rules headless — ignore-unlisted at the
//  pin site (defense in depth with the wisp gate), the no-count label, the private
//  default, and the merge-radius dedup. The MonoBehaviour does the raycast + the actual
//  Minimap.AddPin / DiscoverLocation; the DECISION is here.
//
//  Clean-side (ADR-0001): SBPR-authored; references no vanilla or third-party type.
//  PinKind mirrors the two raycast-hit categories (the wrapper maps a real Pickable/
//  Location component to these); PinType is OUR enum, mapped to vanilla PinType in the
//  wrapper (Icon3 for pickables, the DiscoverLocation default for locations).
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>What the raycast hit resolves to (the wrapper classifies the live component).</summary>
    public enum WispHitKind
    {
        /// <summary>A Pickable (or its vegetation group) — the "abundance pin" path → Minimap.AddPin.</summary>
        Pickable,
        /// <summary>A Location instance — the vanilla DiscoverLocation "shown on map" path.</summary>
        Location,
    }

    /// <summary>A resolved intent to place a pin (the wrapper turns this into the real Minimap call).</summary>
    public readonly struct PinPlan
    {
        /// <summary>Whether a pin should actually be placed (false ⇒ ignore: unlisted, or merged into an existing pin).</summary>
        public readonly bool ShouldPin;
        /// <summary>The pin label — NO count (Daniel): the friendly name only ("Blueberries").</summary>
        public readonly string Label;
        /// <summary>The hit kind, so the wrapper picks AddPin (Pickable) vs DiscoverLocation (Location).</summary>
        public readonly WispHitKind Kind;
        /// <summary>Pins default private (the lens is personal). The wrapper maps this to the pin's share state.</summary>
        public readonly bool Private;
        /// <summary>Why no pin, when ShouldPin is false (diagnostics / tests). Empty when ShouldPin is true.</summary>
        public readonly string Reason;

        private PinPlan(bool shouldPin, string label, WispHitKind kind, bool isPrivate, string reason)
        {
            ShouldPin = shouldPin;
            Label = label;
            Kind = kind;
            Private = isPrivate;
            Reason = reason;
        }

        public static PinPlan Pin(string label, WispHitKind kind)
            => new PinPlan(true, label, kind, isPrivate: true, reason: "");
        public static PinPlan NoPin(string reason)
            => new PinPlan(false, "", default, isPrivate: true, reason);
    }

    /// <summary>An existing pin, reduced to what the merge check needs (the wrapper adapts Minimap.PinData).</summary>
    public readonly struct ExistingPin
    {
        public readonly string Name;
        public readonly Vec3 Pos;
        public ExistingPin(string name, Vec3 pos) { Name = name; Pos = pos; }
    }

    /// <summary>
    /// Pure pin-by-look decision. Engine-free so the rules are CI-gated. The MonoBehaviour reads the
    /// raycast hit (prefab name, Pickable-or-Location, world pos), the loaded eligibility, and the
    /// current pins, then asks <see cref="Decide"/>; on ShouldPin it calls Minimap.AddPin (Pickable)
    /// or DiscoverLocation (Location) with the plan's Label.
    /// </summary>
    public static class SeersStonePinDecision
    {
        /// <summary>Default merge radius, metres — a same-name pin this close ⇒ don't double-pin (one R, Daniel).</summary>
        public const float DefaultMergeRadius = 15f;

        /// <summary>
        /// Decide whether (and how) to pin a raycast hit.
        ///
        /// Order of checks:
        ///   1. ELIGIBILITY (defense in depth): even though only wisps are pinnable and wisps only
        ///      spawn on eligible objects, re-check here so a stray raycast on an unlisted object can
        ///      never pin. IGNORE-UNLISTED (Daniel) holds at the pin site too.
        ///   2. LABEL: the friendly name, NO count (Daniel "just 'Blueberries'"). The wrapper passes
        ///      the object's hover/friendly name; we trim it and strip any vanilla " (x12)"-style
        ///      count the hover might carry, so the pin reads clean.
        ///   3. MERGE: if a same-(normalized)-name pin already sits within <paramref name="mergeRadius"/>,
        ///      return NoPin("merged") — the patch is already on the map.
        ///   4. Else Pin(label, kind), private by default.
        /// </summary>
        public static PinPlan Decide(
            string? hitPrefabName,
            string? friendlyName,
            WispHitKind kind,
            Vec3 hitPos,
            SeersStoneEligibility eligibility,
            IReadOnlyList<ExistingPin> existingPins,
            float mergeRadius = DefaultMergeRadius)
        {
            // 1. Eligibility gate (defense in depth with the wisp spawn gate).
            if (eligibility == null || !eligibility.IsEligible(hitPrefabName))
                return PinPlan.NoPin("ineligible");

            // 2. Clean label — friendly name, no count.
            var label = CleanLabel(friendlyName);
            if (label.Length == 0)
                return PinPlan.NoPin("empty-label");

            // 3. Merge: a same-name pin already within radius ⇒ the patch is already pinned.
            if (existingPins != null)
            {
                var key = label.ToLowerInvariant();
                float r2 = mergeRadius * mergeRadius;
                foreach (var pin in existingPins)
                {
                    if (pin.Name == null) continue;
                    if (CleanLabel(pin.Name).ToLowerInvariant() != key) continue;
                    float dx = pin.Pos.X - hitPos.X, dz = pin.Pos.Z - hitPos.Z;
                    if (dx * dx + dz * dz <= r2)
                        return PinPlan.NoPin("merged");
                }
            }

            // 4. Place it.
            return PinPlan.Pin(label, kind);
        }

        /// <summary>
        /// Reduce a hover/friendly name to the clean pin label: trim, strip a vanilla localization
        /// token prefix if one slipped through (names should already be resolved), and strip any
        /// trailing " x12" / " (12)" count the hover text may carry — the pin shows NO count (Daniel).
        /// Strips Valheim's "$item_..." only if literally present (defensive; the wrapper resolves it).
        /// </summary>
        public static string CleanLabel(string? friendlyName)
        {
            if (string.IsNullOrWhiteSpace(friendlyName)) return string.Empty;
            var s = friendlyName!.Trim();

            // Strip a trailing count the hover might carry: " x12", " ×12", " (12)".
            s = StripTrailingCount(s);

            return s.Trim();
        }

        private static string StripTrailingCount(string s)
        {
            // " (12)" form
            int open = s.LastIndexOf('(');
            if (open > 0 && s.EndsWith(")", StringComparison.Ordinal))
            {
                var inner = s.Substring(open + 1, s.Length - open - 2).Trim();
                if (IsAllDigits(inner)) return s.Substring(0, open).Trim();
            }
            // " x12" / " ×12" form
            int sp = s.LastIndexOf(' ');
            if (sp > 0 && sp < s.Length - 1)
            {
                var tail = s.Substring(sp + 1);
                if ((tail[0] == 'x' || tail[0] == 'X' || tail[0] == '×') && tail.Length > 1 && IsAllDigits(tail.Substring(1)))
                    return s.Substring(0, sp).Trim();
            }
            return s;
        }

        private static bool IsAllDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
