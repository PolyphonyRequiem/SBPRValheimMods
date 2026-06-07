using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Cairns
{
    // The Pigments TYPE lives in namespace SBPR.Trailborne.Features.Pigments. From
    // inside this sibling Features.* namespace the bare name `Pigments` would
    // otherwise bind to that sibling NAMESPACE (the enclosing `Features` scope is
    // searched before a compilation-unit alias), so we alias the name to the type
    // INSIDE this namespace body to keep the readable `Pigments.PigmentRedName` syntax.
    using Pigments = SBPR.Trailborne.Features.Pigments.Pigments;
    using Trailhead = SBPR.Trailborne.Features.Trailhead.Trailhead;

    /// <summary>
    /// M2 content: 4 Cairn Marker variants (one per ink color) + 4 Cairn
    /// piece variants (one per color). Cairns are now full 5-tier with
    /// the v0.1.0 LOCKED behaviour:
    ///
    ///   • Stone ladder (cumulative): T1=9, T2=12, T3=15, T4=18, T5=21
    ///   • Comfort floor by tier:     T1=3, T2=4, T3=5, T4=6, T5=7
    ///   • Tier-1 build cost: 9 Stone + 1 Resin + 1 Cairn Marker
    ///   • Upgrade / Repair gesture: E-press, gated by HP &lt; 75%,
    ///     always repairs to max AND upgrades if tier &lt; 5. Cost = 3 Stone + 1 Resin
    ///     (per design/PARKED-2026-06-03.md; flat per gesture).
    ///   • Cairn is IMMUNE to player + monster damage. Only weather/time decay ticks.
    ///   • Out-of-zone decay: ZDO-persisted SBPR_LastWearTick (long, in-game day-time);
    ///     Harmony postfix on WearNTear.Awake backfills missed wear at vanilla rate.
    ///   • Shift+E debug flag (SBPR_DebugCairnDamage, default true v0.1.0) drops a
    ///     pristine cairn to 70% so the combo gesture is exercisable without waiting
    ///     on weather.
    ///
    /// Decay state machine (LOCKED per requirements.md §A3.5 / §A2.1b):
    ///   ≥75% HP   → pristine (small wear EMBER lit at the pile top — §A2.1b)
    ///   &lt;75% HP   → fizzled (ember out; also the repair-eligible threshold)
    ///   &lt;25% HP   → downgrade one tier, HP reset to 100% of new tier; pile
    ///                rebuilds at the lower stone count  (CairnTag.HpBracketTick)
    ///    0% HP    → collapse (at tier 1 only; vanilla WearNTear destroy path)
    ///
    /// The pile visual + ember live in CairnTag (§A2.1b): a per-tier haphazard,
    /// deterministic (ZDO-seeded) stack of squashed rock_low clones whose count
    /// equals the stone ladder, with the HP-gated ember layered on the
    /// PR #23-neutralized bonfire base.
    ///
    /// All gated behind ServerContext.OnSBServer.
    /// </summary>
    public static class Cairns
    {
        // Color identifiers — must match Pigment names
        public static readonly string[] Colors = { "red", "white", "blue", "black" };

        public static string MarkerName(string color) => "SBPR_CairnMarker_" + color;
        public static string CairnName (string color) => "piece_sbpr_cairn_" + color;
        public static string PigmentNameFor(string color)
        {
            switch (color)
            {
                case "red":   return Pigments.PigmentRedName;
                case "white": return Pigments.PigmentWhiteName;
                case "blue":  return Pigments.PigmentBlueName;
                case "black": return Pigments.PigmentBlackName;
                default: return Pigments.PigmentWhiteName;
            }
        }

        // Back-compat: code outside M2 still references this name for "any marker".
        public const string CairnMarkerItemName = "SBPR_CairnMarker_white";

        private const string SourceConsumable = "Coins";
        private const string SourceBonfire    = "bonfire";

        // ── Cairn tier tables (LOCKED v0.1.0) ────────────────────────
        // Indexed 1..5; index 0 is sentinel.
        public  const int MaxTier = 5;
        private static readonly int[] StoneByTier        = { 0, 9, 12, 15, 18, 21 };
        private static readonly int[] ComfortFloorByTier = { 0, 3,  4,  5,  6,  7 };
        public  const int UpgradeStoneCost = 3;
        public  const int UpgradeResinCost = 1;
        public  const float CairnComfortRadius = 10f;
        public  const float PristineHpFraction = 0.75f;
        public  const float DowngradeHpFraction = 0.25f;
        public  const float DebugDamageTargetFraction = 0.70f;

        // ZDO keys
        public  const string ZdoTier         = "SBPR_CairnTier";
        public  const string ZdoLastWearTick = "SBPR_LastWearTick";

        public static int StoneCostForTier(int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > MaxTier) tier = MaxTier;
            return StoneByTier[tier];
        }

        public static int ComfortFloorForTier(int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > MaxTier) tier = MaxTier;
            return ComfortFloorByTier[tier];
        }

        public static void RegisterPrefabs(ZNetScene zns)
        {
            foreach (var c in Colors)
            {
                RegisterCairnMarkerPrefab(zns, c);
                RegisterCairnPiecePrefab(zns, c);
            }
        }

        private static void RegisterCairnMarkerPrefab(ZNetScene zns, string color)
        {
            var name = MarkerName(color);
            if (zns.GetPrefab(name) != null) return;
            var clone = Assets.ClonePrefab(SourceConsumable, name);
            if (clone == null) return;
            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_shared.m_name        = "Cairn Marker (" + Capitalize(color) + ")";
                drop.m_itemData.m_shared.m_description =
                    "A wooden marker plank with a " + color + " hide pennant. Place on stones to declare a Cairn.";
                drop.m_itemData.m_shared.m_maxStackSize = 10;
                drop.m_itemData.m_shared.m_weight      = 0.5f;
                drop.m_itemData.m_shared.m_itemType    = ItemDrop.ItemData.ItemType.Material;
                var sprite = Assets.LoadPngAsSprite("cairn_marker_v0.1.png");
                if (sprite != null) drop.m_itemData.m_shared.m_icons = new[] { sprite };
            }
            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M2] Registered cairn marker item: {name}");
        }

        private static void RegisterCairnPiecePrefab(ZNetScene zns, string color)
        {
            var name = CairnName(color);
            if (zns.GetPrefab(name) != null) return;
            // Bonfire is a chunky stone-y piece; use as a base and bury its visual
            // children under a runtime-assembled kitbash stack (see BuildKitbashArt).
            var clone = Assets.ClonePrefab(SourceBonfire, name);
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M2] Source bonfire prefab missing, skipping cairn ({color}).");
                return;
            }
            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Cairn (" + Capitalize(color) + ")";
                piece.m_description =
                    "A " + color + "-marked stone cairn. Stack stones to raise its tier and comfort floor. " +
                    "E to repair-and-upgrade when fizzled (<75% HP). Immune to combat damage — weathers over time.";
                // MUST be Misc: the spade's from-scratch PieceTable declares only the
                // single Misc-backed "Trail" category (Trailblazing.BuildSpadePieceTable).
                // A piece whose category isn't declared on that table is added to
                // m_pieces but its tab never renders, so it's INVISIBLE in the build
                // menu. v0.2.2 shipped these as Crafting → all four cairns silently
                // vanished from the spade menu. Keep cairns in the one "Trail" tab,
                // matching the locked single-tab design; a category-routing guard in
                // BuildSpadePieceTable now screams at boot if this ever drifts again.
                piece.m_category    = Piece.PieceCategory.Misc;
                piece.m_resources   = new[]
                {
                    BuildReq("Stone", StoneCostForTier(1)),
                    BuildReq("Resin", 1),
                    // Marker isn't in ObjectDB yet at prefab-build time; this requirement
                    // is rebuilt in DoObjectDBWiring once the markers are registered.
                    // Suppress the known-transient "NOT FOUND" warning for this phase.
                    BuildReq(MarkerName(color), 1, warn: false),
                };
                var sprite = Assets.LoadPngAsSprite("cairn_marker_v0.1.png");
                if (sprite != null) piece.m_icon = sprite;
                // Comfort is applied dynamically via SE_Rested patch — base piece carries 0
                // so we don't double-count in the vanilla ComfortGroup dedup table.
                piece.m_comfort = 0;
                piece.m_comfortGroup = Piece.ComfortGroup.None;
            }

            var tag = clone.AddComponent<CairnTag>();
            tag.Color = color;

            // The Cairn interactable handles E (repair+upgrade combo) and Shift+E
            // (debug damage, gated on Plugin.DebugCairnDamage config).
            clone.AddComponent<CairnInteractable>();

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M2] Registered cairn piece: {name}");
        }

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            foreach (var color in Colors)
            {
                var markerName = MarkerName(color);
                var marker = zns?.GetPrefab(markerName);
                if (marker != null) Assets.RegisterItemInObjectDB(marker);

                if (!RecipeHelpers.HasRecipe(markerName))
                {
                    var markerItem = odb.GetItemPrefab(markerName);
                    if (markerItem != null)
                    {
                        var recipe = ScriptableObject.CreateInstance<Recipe>();
                        recipe.name              = "Recipe_" + markerName;
                        recipe.m_item            = markerItem.GetComponent<ItemDrop>();
                        recipe.m_amount          = 1;
                        recipe.m_minStationLevel = 1;
                        recipe.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);
                        recipe.m_resources       = new[]
                        {
                            BuildReq("LeatherScraps", 2),
                            BuildReq("FineWood", 1),
                            BuildReq(PigmentNameFor(color), 1),
                        };
                        odb.m_recipes.Add(recipe);
                    }
                }
            }

            // Rebuild cairn resource lists now that markers exist in ObjectDB. (Pieces
            // built at ZNetScene.Awake had null marker requirements because ODB wasn't
            // populated yet.) Cairns are added to the SPADE PieceTable in Trailblazing's
            // BuildSpadePieceTable — NOT the Hammer (design-pillars: paths/signs/cairns/
            // lamps all live on the Spade; fixing 2026-06-05 playtest drift where cairns
            // landed on the Hammer despite the design pillar).
            foreach (var color in Colors)
            {
                var cairnPrefab = zns?.GetPrefab(CairnName(color));
                if (cairnPrefab == null) continue;
                var piece = cairnPrefab.GetComponent<Piece>();
                if (piece != null)
                {
                    piece.m_resources = new[]
                    {
                        BuildReq("Stone", StoneCostForTier(1)),
                        BuildReq("Resin", 1),
                        BuildReq(MarkerName(color), 1),
                    };
                }
            }

            Plugin.Log.LogInfo(
                $"[Trailborne/M2] M2 ObjectDB wiring complete (4 marker variants + 4 cairn variants + " +
                $"5-tier ladder 9/12/15/18/21 stone, comfort floors 3/4/5/6/7).");
        }

        // ───────────────────────────────────────────────
        // SE_Rested comfort patch — inject cairn comfort floor
        // ───────────────────────────────────────────────

        /// <summary>
        /// Returns the highest comfort floor of any in-range cairn at <paramref name="position"/>,
        /// or 0 if none. Tier-aware: reads ZDO SBPR_CairnTier (default 1).
        /// </summary>
        public static int GetCairnComfortBonus(Vector3 position)
        {
            int floor = 0;
            var hits = Physics.OverlapSphere(position, CairnComfortRadius);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h == null) continue;
                var tag = h.GetComponentInParent<CairnTag>();
                if (tag == null) continue;
                int tier = tag.ReadTier();
                int bonus = ComfortFloorForTier(tier);
                if (bonus > floor) floor = bonus;
            }
            return floor;
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount, bool warn = true)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "M2", warn);
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}
