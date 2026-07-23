// The finite, manifest-compiled verb catalog (ADR-0009 §3.1). Every command the
// harness could ever expose is a NAMED, BOUNDED verb with a fixed role and a typed
// argument schema. There is no arbitrary eval, no reflection, no prefab/type/method/
// file/network/shell surface — a verb not in this static table simply does not exist.
//
// M1 defines the catalog + argument bounds + the parse/validate logic ONLY. The verbs
// are inert descriptors here: nothing in this assembly touches the game. Actual
// execution of a fixture/action/observation lands in later, separately-reviewed cards
// (M2+) behind the arming gate this card ships.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>Which control channel a verb is delivered over (ADR-0009 §3.1).</summary>
    public enum VerbChannel
    {
        /// <summary>Server role, authenticated per-peer ZRpc (no host listener).</summary>
        ServerRpc = 1,
        /// <summary>Client role, owner-local loopback TCP/JSON.</summary>
        ClientLoopback = 2,
        /// <summary>Either role (read-only observation / lifecycle).</summary>
        Either = 3,
    }

    /// <summary>The kind of a typed verb argument, with an inclusive numeric bound where applicable.</summary>
    public enum ArgKind
    {
        /// <summary>An exact identifier from a static allowlist (prefab/item/recipe/field name). Bounds n/a.</summary>
        AllowlistedId = 1,
        /// <summary>A bounded non-negative integer count, inclusive [Min, Max].</summary>
        BoundedInt = 2,
        /// <summary>A bounded double (e.g. radius/quality), inclusive [Min, Max].</summary>
        BoundedDouble = 3,
        /// <summary>A short opaque string (slot id / phase token), length-bounded [Min, Max] chars.</summary>
        BoundedString = 4,
    }

    /// <summary>A single typed argument declaration for a verb.</summary>
    public sealed class VerbArg
    {
        public string Name { get; }
        public ArgKind Kind { get; }
        public double Min { get; }
        public double Max { get; }

        public VerbArg(string name, ArgKind kind, double min, double max)
        {
            Name = name;
            Kind = kind;
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Validate one supplied argument value against this declaration. Fail-closed:
        /// a null value, wrong type, or out-of-bound number is rejected.
        /// </summary>
        public bool IsInBounds(object? value)
        {
            switch (Kind)
            {
                case ArgKind.AllowlistedId:
                case ArgKind.BoundedString:
                {
                    if (value is not string s) return false;
                    // Allowlist membership is enforced by the caller against the static
                    // allowlist; here we bound the raw length so an unbounded blob can't
                    // ride in as a "name". BoundedString uses Min/Max as char bounds;
                    // AllowlistedId uses a fixed sane cap.
                    int lo = Kind == ArgKind.BoundedString ? (int)Min : 1;
                    int hi = Kind == ArgKind.BoundedString ? (int)Max : 128;
                    return s.Length >= lo && s.Length <= hi;
                }
                case ArgKind.BoundedInt:
                {
                    if (value is not long l)
                    {
                        // Accept int/long; reject anything else (no implicit double->int).
                        if (value is int i) l = i;
                        else return false;
                    }
                    return l >= (long)Min && l <= (long)Max;
                }
                case ArgKind.BoundedDouble:
                {
                    double d;
                    if (value is double dd) d = dd;
                    else if (value is long ll) d = ll;
                    else if (value is int ii) d = ii;
                    else return false;
                    if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                    return d >= Min && d <= Max;
                }
                default:
                    return false;
            }
        }
    }

    /// <summary>A named verb: its channel/role and typed argument schema.</summary>
    public sealed class CapabilityVerb
    {
        public string Name { get; }
        public VerbChannel Channel { get; }
        public IReadOnlyList<VerbArg> Args { get; }

        public CapabilityVerb(string name, VerbChannel channel, IReadOnlyList<VerbArg> args)
        {
            Name = name;
            Channel = channel;
            Args = args;
        }

        /// <summary>True when this verb may run under <paramref name="role"/>.</summary>
        public bool AllowsRole(HarnessRole role) => Channel switch
        {
            VerbChannel.ServerRpc => role == HarnessRole.Server,
            VerbChannel.ClientLoopback => role == HarnessRole.Client,
            VerbChannel.Either => true,
            _ => false,
        };
    }

    /// <summary>
    /// The immutable, static verb catalog (ADR-0009 §3.1). This is the ONLY set of
    /// verbs that can ever exist; the per-run capability manifest may permit a SUBSET
    /// of these, never a superset. Bounds are conservative T022 values.
    /// </summary>
    public static class VerbCatalog
    {
        // Conservative shared bounds. Rmax pickup radius, quality range, count cap.
        private const double RadiusMax = 8.0;
        private const long CountMax = 64;
        private const long QualityMin = 1;
        private const long QualityMax = 4;
        private const int SlotMax = 32;
        private const int PhaseMax = 16;

        private static readonly Dictionary<string, CapabilityVerb> _byName = Build();

        private static Dictionary<string, CapabilityVerb> Build()
        {
            var verbs = new List<CapabilityVerb>
            {
                // ── Fixture (Server role, per-peer ZRpc) ──────────────────────
                new("SpawnStation", VerbChannel.ServerRpc, new[]
                {
                    new VerbArg("prefab", ArgKind.AllowlistedId, 0, 0),
                    new VerbArg("posRadius", ArgKind.BoundedDouble, 0, RadiusMax),
                }),
                new("GrantVanillaMaterials", VerbChannel.ServerRpc, new[]
                {
                    new VerbArg("itemId", ArgKind.AllowlistedId, 0, 0),
                    new VerbArg("qty", ArgKind.BoundedInt, 1, CountMax),
                }),
                new("PlaceVanillaPiece", VerbChannel.ServerRpc, new[]
                {
                    new VerbArg("prefab", ArgKind.AllowlistedId, 0, 0),
                    new VerbArg("posRadius", ArgKind.BoundedDouble, 0, RadiusMax),
                }),
                new("SetWorldTime", VerbChannel.ServerRpc, new[]
                {
                    new VerbArg("phase", ArgKind.BoundedString, 1, PhaseMax),
                }),

                // ── Action (Client role, loopback) ────────────────────────────
                new("Craft", VerbChannel.ClientLoopback, new[]
                {
                    new VerbArg("recipeName", ArgKind.AllowlistedId, 0, 0),
                    new VerbArg("station", ArgKind.AllowlistedId, 0, 0),
                }),
                new("UpgradeItem", VerbChannel.ClientLoopback, new[]
                {
                    new VerbArg("itemSlot", ArgKind.BoundedString, 1, SlotMax),
                    new VerbArg("targetQuality", ArgKind.BoundedInt, QualityMin, QualityMax),
                }),
                new("DropItem", VerbChannel.ClientLoopback, new[]
                {
                    new VerbArg("itemSlot", ArgKind.BoundedString, 1, SlotMax),
                }),
                new("PickUpNearest", VerbChannel.ClientLoopback, new[]
                {
                    new VerbArg("itemName", ArgKind.AllowlistedId, 0, 0),
                    new VerbArg("radius", ArgKind.BoundedDouble, 0, RadiusMax),
                }),
                new("TamperField", VerbChannel.ClientLoopback, new[]
                {
                    new VerbArg("itemSlot", ArgKind.BoundedString, 1, SlotMax),
                    new VerbArg("field", ArgKind.AllowlistedId, 0, 0),
                }),

                // ── Observation (either role) ─────────────────────────────────
                new("ReadInventory", VerbChannel.Either, Array.Empty<VerbArg>()),
                new("ReadItem", VerbChannel.Either, new[]
                {
                    new VerbArg("itemSlot", ArgKind.BoundedString, 1, SlotMax),
                }),
                new("ReadTooltip", VerbChannel.Either, new[]
                {
                    new VerbArg("itemSlot", ArgKind.BoundedString, 1, SlotMax),
                }),
                new("ReadWorldName", VerbChannel.Either, Array.Empty<VerbArg>()),
                new("ReadWorldUid", VerbChannel.Either, Array.Empty<VerbArg>()),

                // ── Lifecycle (either role) ───────────────────────────────────
                new("Ping", VerbChannel.Either, Array.Empty<VerbArg>()),
                new("Cleanup", VerbChannel.Either, new[]
                {
                    new VerbArg("scope", ArgKind.BoundedString, 1, PhaseMax),
                }),
                new("Disarm", VerbChannel.Either, Array.Empty<VerbArg>()),
            };

            var map = new Dictionary<string, CapabilityVerb>(StringComparer.Ordinal);
            foreach (var v in verbs)
            {
                map.Add(v.Name, v); // duplicate name in the static table is a programming error
            }
            return map;
        }

        /// <summary>All catalog verb names (ordinal set).</summary>
        public static IReadOnlyCollection<string> Names => _byName.Keys;

        /// <summary>True when <paramref name="name"/> is a known catalog verb.</summary>
        public static bool IsKnown(string? name) => name != null && _byName.ContainsKey(name);

        /// <summary>Look up a verb; returns null when unknown.</summary>
        public static CapabilityVerb? Get(string? name)
            => name != null && _byName.TryGetValue(name, out var v) ? v : null;
    }
}
