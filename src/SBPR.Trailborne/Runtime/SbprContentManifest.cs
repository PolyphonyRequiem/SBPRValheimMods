using System.Collections.Generic;
using SBPR.Trailborne.Core.Content;
using SBPR.Trailborne.Features.Pigments;
using SBPR.Trailborne.Features.MarkerSigns;
using SBPR.Trailborne.Features.Cartography;
using SBPR.Trailborne.Features.Portals;
using SBPR.Trailborne.Features.Sunstone;
using SBPR.Trailborne.Features.Exploration;
using SBPR.Trailborne.Features.Trailhead;

namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// Builds the ENGINE-FREE <see cref="ContentRegistry"/> from SBPR's existing feature name+cost
    /// constants (arch review Model B). This is the single declarative source of truth for the
    /// recipes/pieces that were previously stated TWICE — once by each feature's live
    /// <c>DoObjectDBWiring</c> and again by <c>SpecCheck</c>'s hand-copied <c>Manifest</c> array.
    /// SpecCheck now reads THIS registry, so the boot guard and the intended shape can no longer
    /// drift (they're the same list).
    ///
    /// <para><b>R3 (wire-contract) safety.</b> Every string here is an EXISTING feature const
    /// (<c>Pigments.PigmentRedName</c>, <c>SunstoneLens.LensName</c>, …) and every number an
    /// existing cost const (<c>SunstoneLens.LensIronCost</c>, <c>IronCompass.IronCost</c>, …). This
    /// builder MINTS NO new prefab/resource string and NO new cost literal — it references the same
    /// consts the live wiring uses, so the registry and the registration literally cannot disagree
    /// on a name or a number. (The lives in the SHELL, not the Core, precisely because those consts
    /// live in engine-fused feature classes; the Core holds only the data TYPES.)</para>
    ///
    /// <para><b>Scope (sliced P2).</b> Only the ~declarative rows that were duplicated between the
    /// live wiring and SpecCheck's Manifest. The generated loops (cairn colours via
    /// <c>Cairns.Colors</c>, marker signs via <c>MarkerSigns.MarkerTypes</c>) and the
    /// asset-renderability / Portal-Energy checks stay procedural in SpecCheck — they are not
    /// declarative recipe rows. Rewriting the 18 live <c>DoObjectDBWiring</c> methods to READ this
    /// registry is a later, higher-blast-radius phase; this phase makes the registry the single
    /// source the CHECK reads, retiring the drift without touching live registration.</para>
    /// </summary>
    public static class SbprContentManifest
    {
        // The Explorer's Bench station name, shared by every SBPR item recipe below.
        private const string Bench = Trailhead.ExplorersBenchName; // "piece_sbpr_explorers_bench"

        private static Req R(string resource, int amount) => new Req(resource, amount);

        /// <summary>Build the registry from the live feature consts. Called once (cached) by
        /// <see cref="Registry"/>. Rows mirror the former SpecCheck.Manifest, in the same order.</summary>
        private static ContentRegistry Build()
        {
            var recipes = new List<RecipeDef>
            {
                // ── Trailblazer's Spade (hard-literal row — spade has no feature name const) ──
                new RecipeDef("SBPR_TrailblazersSpade", 1, Bench, new[]
                {
                    R("Wood", 5), R("Flint", 2), R("LeatherScraps", 2),
                }),

                // ── Pigments (4 — names via Pigments.*Name; amount 2; single foraged resource) ──
                new RecipeDef(Pigments.PigmentRedName,   2, Bench, new[] { R("Raspberry", 1) }),
                new RecipeDef(Pigments.PigmentWhiteName, 2, Bench, new[] { R("BoneFragments", 1) }),
                new RecipeDef(Pigments.PigmentBlueName,  2, Bench, new[] { R("Blueberries", 1) }),
                new RecipeDef(Pigments.PigmentBlackName, 2, Bench, new[] { R("Coal", 1) }),

                // ── Local Map (hard-literal name; DeerHide ×2 + FineWood ×4) ──
                new RecipeDef("SBPR_LocalMap", 1, Bench, new[]
                {
                    R("DeerHide", 2), R("FineWood", 4),
                }),

                // ── Cartographer's Kit (KitName; the 40-pigment gate via Pigments.*Name) ──
                new RecipeDef(CartographersKit.KitName, 1, Bench, new[]
                {
                    R(Pigments.PigmentRedName,   10),
                    R(Pigments.PigmentWhiteName, 10),
                    R(Pigments.PigmentBlueName,  10),
                    R(Pigments.PigmentBlackName, 10),
                    R("FineWood", 4),
                }),

                // ── Portal Seed (SeedItemName; costs via Portals.Seed*Cost; Eye via shared const) ──
                new RecipeDef(Portals.SeedItemName, 1, Bench, new[]
                {
                    R("AncientSeed", Portals.SeedAncientSeedCost),
                    R(MarkerSigns.EyeResource, Portals.SeedGreydwarfEyeCost),  // "GreydwarfEye"
                    R("SurtlingCore", Portals.SeedSurtlingCoreCost),
                }),

                // ── Sunstone Lens (LensName; costs via SunstoneLens.Lens*Cost; Sunstone via const) ──
                new RecipeDef(SunstoneLens.LensName, 1, Bench, new[]
                {
                    R(SunstoneLens.SunstoneName, SunstoneLens.LensSunstoneCost),
                    R("Iron", SunstoneLens.LensIronCost),
                    R("Guck", SunstoneLens.LensGuckCost),
                }),

                // ── Iron Compass (CompassName; costs via IronCompass.*Cost; Red pigment via const) ──
                new RecipeDef(IronCompass.CompassName, 1, Bench, new[]
                {
                    R("Iron", IronCompass.IronCost),
                    R("Ooze", IronCompass.OozeCost),
                    R(Pigments.PigmentRedName, IronCompass.RedPigmentCost),
                }),
            };

            var pieces = new List<PieceDef>
            {
                // ── Stations / lamps / sign (hard-literal names; no feature name const) ──
                new PieceDef("piece_sbpr_explorers_bench", null, new[]
                {
                    R("Wood", 10), R("Stone", 4), R("TrophyDeer", 1),
                }),
                new PieceDef("piece_sbpr_path_lamp", null, new[]
                {
                    R("Wood", 3), R("Resin", 2),
                }),
                new PieceDef("piece_sbpr_sign", null, new[] { R("Wood", 2) }),

                // ── Surveyor's Table (hard-literal name; Black-Forest cost) ──
                new PieceDef("piece_sbpr_surveyors_table", null, new[]
                {
                    R("FineWood", 10), R("Bronze", 2), R("DeerHide", 4), R("BoneFragments", 8),
                }),

                // ── Bear Hide Tent (hard-literal; PROVISIONAL — must equal BearHideTent.BuildResources) ──
                new PieceDef("piece_sbpr_bearhide_tent", null, new[]
                {
                    R("BjornHide", 4), R("FineWood", 6), R("LeatherScraps", 4),
                }),

                // ── Trailside Camp triad — Bedroll + Camp Fire (card t_439f2351 defects 2,3).
                //    Hard-literal names; PROVISIONAL Black-Forest costs — MUST equal each
                //    feature's BuildResources() (Bedroll.BuildResources / CampFire.BuildResources). ──
                new PieceDef("piece_sbpr_bedroll", null, new[]
                {
                    R("BjornHide", 2), R("LeatherScraps", 3), R("Wood", 4),
                }),
                new PieceDef("piece_sbpr_camp_fire", null, new[]
                {
                    R("Wood", 5), R("Stone", 3), R("Coal", 2),
                }),

                // ── Ancient Portal (PortalPieceName; sole cost is one Portal Seed) ──
                new PieceDef(Portals.PortalPieceName, null, new[]
                {
                    R(Portals.SeedItemName, 1),
                }),

                // ── Twisted Portal (PortalPieceName; costs via TwistedPortal.Portal*Cost; shared consts) ──
                new PieceDef(TwistedPortal.PortalPieceName, null, new[]
                {
                    R("FineWood", TwistedPortal.PortalFineWoodCost),
                    R(MarkerSigns.EyeResource, TwistedPortal.PortalGreydwarfEyeCost),   // "GreydwarfEye"
                    R("SurtlingCore", TwistedPortal.PortalSurtlingCoreCost),
                    R(SunstoneLens.SunstoneName, TwistedPortal.PortalSunstoneCost),     // "SBPR_Sunstone"
                }),
            };

            return new ContentRegistry(recipes, pieces);
        }

        private static ContentRegistry? cached;

        /// <summary>The shared, engine-free content registry (built once from the feature consts).</summary>
        public static ContentRegistry Registry => cached ??= Build();
    }
}
