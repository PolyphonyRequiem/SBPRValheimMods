using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    // T034 — the OPERATOR INSPECTION + QUARANTINE OUTPUT surface for one loaded (Stone, character,
    // authority) triple, and the disposable-data reset report (tasks.md T034; data-model.md
    // §"Validation and recovery"; acceptance AT-RESTART-SUITE / AT-UNRELEASED-DATA-RESET).
    //
    // WHY THIS EXISTS, and the pitfall it is built against.
    // "It still boots" is NOT "it derives the right answer." A character carrying stale recorded state
    // loads cleanly, satisfies every boot assertion, and then derives a WRONG balance. So this surface
    // never reports a bare "loaded OK": every boot fact is printed BESIDE the derived answer it is
    // supposed to justify — the quarantine notices AND the re-derived per-Stone balances, purchase
    // count, node development tally, and per-node active/dormant projection. An operator comparing two
    // restarts can therefore see a CONTENT divergence, not merely that both processes started.
    //
    // It OBSERVES; it never repairs. Every notice comes from the shipped ProgressionStateRepair.Scan
    // (this file re-derives no invariant of its own, so there is no second rule set to drift), and the
    // reset report is a rendering of the shipped ProgressionStateRepair.ResetIncompatibleFixture result.
    // Nothing here mutates an aggregate, writes a journal, or chooses a side of an interrupted mutation.
    //
    // net48 audit: System / Collections.Generic / Text / Globalization plus shipped engine-free domain +
    // recovery types. No UnityEngine / Valheim / BepInEx / Harmony reference, so it link-compiles into
    // the net8 test project and is fully unit-tested. It is not a [HarmonyPatch] class and therefore
    // needs no Plugin.Awake() registration (AGENTS.md patch-registration rule applies to patch classes).

    /// <summary>The re-derived, per-Stone answer an operator must be able to compare across a restart.
    /// These are DERIVED facts (balances, counts, projection tallies), never "the process booted".</summary>
    public readonly struct StoneBalanceLine
    {
        public StoneBalanceLine(StoneId stoneId, int personalAp, int cumulativeAp, int personalBp,
            int purchaseCount, int activeRelationshipCount, int skillCapChoiceCount)
        {
            StoneId = stoneId;
            PersonalAp = personalAp;
            CumulativeAp = cumulativeAp;
            PersonalBp = personalBp;
            PurchaseCount = purchaseCount;
            ActiveRelationshipCount = activeRelationshipCount;
            SkillCapChoiceCount = skillCapChoiceCount;
        }

        public StoneId StoneId { get; }
        public int PersonalAp { get; }
        public int CumulativeAp { get; }
        public int PersonalBp { get; }
        public int PurchaseCount { get; }
        public int ActiveRelationshipCount { get; }
        public int SkillCapChoiceCount { get; }
    }

    /// <summary>One inspection of a loaded aggregate triple: what is contradictory (quarantine) AND what
    /// the state currently DERIVES. Both halves are required — a clean quarantine report alone cannot
    /// tell an operator whether the answer is right.</summary>
    public sealed class ProgressionInspection
    {
        internal ProgressionInspection(
            StoneId stoneId, AccountId account, CharacterId character,
            QuarantineReport quarantine,
            int contentRegistryVersion, int buildContentRegistryVersion,
            int activeStoneLevel, int historicalStoneLevel,
            long stoneRevision, long characterRevision, long authorityRevision,
            long mirroredStoneAp,
            int committedTreeCount, int developedNodeCount, int offeredNodeCount,
            int derivedActiveNodeCount, int derivedDormantNodeCount,
            bool callerHasActiveRelationship,
            IReadOnlyList<StoneBalanceLine> balances)
        {
            StoneId = stoneId;
            Account = account;
            Character = character;
            Quarantine = quarantine;
            ContentRegistryVersion = contentRegistryVersion;
            BuildContentRegistryVersion = buildContentRegistryVersion;
            ActiveStoneLevel = activeStoneLevel;
            HistoricalStoneLevel = historicalStoneLevel;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
            AuthorityRevision = authorityRevision;
            MirroredStoneAp = mirroredStoneAp;
            CommittedTreeCount = committedTreeCount;
            DevelopedNodeCount = developedNodeCount;
            OfferedNodeCount = offeredNodeCount;
            DerivedActiveNodeCount = derivedActiveNodeCount;
            DerivedDormantNodeCount = derivedDormantNodeCount;
            CallerHasActiveRelationship = callerHasActiveRelationship;
            Balances = balances ?? Array.Empty<StoneBalanceLine>();
        }

        public StoneId StoneId { get; }
        public AccountId Account { get; }
        public CharacterId Character { get; }

        /// <summary>The shipped Scan's verdict. This file adds no invariant of its own.</summary>
        public QuarantineReport Quarantine { get; }

        public int ContentRegistryVersion { get; }
        public int BuildContentRegistryVersion { get; }
        public int ActiveStoneLevel { get; }
        public int HistoricalStoneLevel { get; }
        public long StoneRevision { get; }
        public long CharacterRevision { get; }
        public long AuthorityRevision { get; }
        public long MirroredStoneAp { get; }
        public int CommittedTreeCount { get; }
        public int DevelopedNodeCount { get; }
        public int OfferedNodeCount { get; }

        /// <summary>Nodes the DerivedActivationView currently projects Active for this caller.</summary>
        public int DerivedActiveNodeCount { get; }

        /// <summary>Nodes purchased but currently suppressed (relationship/gate) for this caller.</summary>
        public int DerivedDormantNodeCount { get; }

        public bool CallerHasActiveRelationship { get; }

        public IReadOnlyList<StoneBalanceLine> Balances { get; }

        /// <summary>True when the loaded state carries at least one contradiction. A false here means
        /// "no contradiction found", NEVER "the derived answer is correct" — compare the balances.</summary>
        public bool IsQuarantined => !Quarantine.IsClean;

        /// <summary>A stable, order-independent fingerprint of the DERIVED answer (balances, counts, and
        /// projection tallies). Two restarts over the same durable truth must produce the SAME string;
        /// a difference is a content divergence, which is exactly the failure "it still boots" hides.
        /// It deliberately EXCLUDES revisions and quarantine text, which legitimately move.</summary>
        public string DerivedFingerprint
        {
            get
            {
                var lines = new List<string>();
                foreach (var b in Balances)
                    lines.Add(string.Join(";", new[]
                    {
                        b.StoneId.Value,
                        N(b.PersonalAp), N(b.CumulativeAp), N(b.PersonalBp),
                        N(b.PurchaseCount), N(b.ActiveRelationshipCount), N(b.SkillCapChoiceCount)
                    }));
                lines.Sort(StringComparer.Ordinal);
                var sb = new StringBuilder();
                sb.Append("stone=").Append(StoneId.Value)
                  .Append("|lvl=").Append(N(ActiveStoneLevel)).Append('/').Append(N(HistoricalStoneLevel))
                  .Append("|mirroredAp=").Append(MirroredStoneAp.ToString(CultureInfo.InvariantCulture))
                  .Append("|trees=").Append(N(CommittedTreeCount))
                  .Append("|dev=").Append(N(DevelopedNodeCount))
                  .Append("|offered=").Append(N(OfferedNodeCount))
                  .Append("|active=").Append(N(DerivedActiveNodeCount))
                  .Append("|dormant=").Append(N(DerivedDormantNodeCount))
                  .Append("|rel=").Append(CallerHasActiveRelationship ? "1" : "0");
                foreach (var l in lines) sb.Append("|rec=").Append(l);
                return sb.ToString();
            }
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Engine-free operator inspection + quarantine/reset rendering over the shipped repair.</summary>
    public static class ProgressionDiagnostics
    {
        /// <summary>Printed on every report. A boot that produces no quarantine notice has proven the
        /// state is not self-contradictory — it has NOT proven the derived answer is right, and it has
        /// proven nothing at all about a joined client. Kept public and const so a test can assert the
        /// rendered text carries it verbatim: the caveat is part of the deliverable.</summary>
        public const string BootIsNotCorrectnessCaveat =
            "A CLEAN REPORT MEANS 'NO CONTRADICTION FOUND', NOT 'THE ANSWER IS RIGHT'. Stale recorded "
            + "state can load cleanly and still derive a WRONG balance. Compare the DERIVED values above "
            + "across the restart, not merely the fact that the process booted. Nothing here is evidence "
            + "about a joined client in-world.";

        /// <summary>Inspect one loaded aggregate triple. Runs the SHIPPED quarantine scan and, beside it,
        /// re-derives the answer the state currently produces. Mutates nothing.</summary>
        public static ProgressionInspection Inspect(
            HomesteadProgressionCatalog catalog,
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            var quarantine = new ProgressionStateRepair(catalog).Scan(stone, character, authority);

            int developed = 0, offered = 0;
            foreach (var dev in stone.NodeDevelopment)
            {
                if (dev.Developed) developed++;
                if (dev.Offered) offered++;
            }

            var balances = new List<StoneBalanceLine>();
            foreach (var sr in character.StoneRecords)
            {
                int activeRel = 0;
                foreach (var rel in sr.Relationships)
                    if (rel.IsActive) activeRel++;
                balances.Add(new StoneBalanceLine(sr.StoneId, sr.PersonalAp, sr.CumulativeAp, sr.PersonalBp,
                    sr.Purchases.Count, activeRel, sr.SkillCapChoices.Count));
            }

            // The derived projection is only computable when the index is keyed to this caller; a
            // mismatched key already quarantines as AuthorityMismatch and Derive would throw on it.
            // Report the projection as absent rather than fabricating zeros that look like a real answer.
            int active = -1, dormant = -1;
            bool callerActive = false;
            if (authority.Account.Equals(character.Account) && authority.StoneId.Equals(stone.StoneId))
            {
                var view = DerivedActivationView.Derive(stone, character, authority);
                active = 0;
                dormant = 0;
                foreach (var row in view.Nodes)
                {
                    if (row.State == DerivedNodeState.Active) active++;
                    else if (row.State == DerivedNodeState.Dormant) dormant++;
                }
                callerActive = view.CallerHasActiveRelationship;
            }

            return new ProgressionInspection(
                stone.StoneId, character.Account, character.Character, quarantine,
                stone.ContentRegistryVersion, catalog.ContentRegistryVersion,
                stone.ActiveStoneLevel, stone.HistoricalStoneLevel,
                stone.Revision, character.Revision, authority.Revision,
                stone.MirroredStoneAp,
                stone.CommittedTrees.Count, developed, offered,
                active, dormant, callerActive, balances);
        }

        /// <summary>Render the operator report: the derived answer FIRST, then the quarantine notices,
        /// then the caveat. Purely a rendering of <paramref name="inspection"/>; it recomputes nothing.</summary>
        public static string Render(ProgressionInspection inspection)
        {
            if (inspection == null) throw new ArgumentNullException(nameof(inspection));

            var sb = new StringBuilder();
            sb.AppendLine("=== Homestead Progression Recovery Inspection ===");
            sb.AppendLine("stone: " + inspection.StoneId.Value);
            sb.AppendLine("account/character: " + inspection.Account.Value + " / " + inspection.Character.Value);
            sb.AppendLine("content_registry_version: " + N(inspection.ContentRegistryVersion)
                + " (build " + N(inspection.BuildContentRegistryVersion) + ")"
                + (inspection.ContentRegistryVersion != inspection.BuildContentRegistryVersion
                    ? "  INCOMPATIBLE FIXTURE - explicit reset required, never silent reinterpretation" : ""));
            sb.AppendLine("revisions: stone=" + L(inspection.StoneRevision)
                + " character=" + L(inspection.CharacterRevision)
                + " authority=" + L(inspection.AuthorityRevision));

            sb.AppendLine("-- derived answer (compare THIS across a restart) --");
            sb.AppendLine("  stone_level_active/historical: " + N(inspection.ActiveStoneLevel)
                + "/" + N(inspection.HistoricalStoneLevel));
            sb.AppendLine("  mirrored_stone_ap: " + L(inspection.MirroredStoneAp));
            sb.AppendLine("  committed_trees: " + N(inspection.CommittedTreeCount));
            sb.AppendLine("  nodes_developed/offered: " + N(inspection.DevelopedNodeCount)
                + "/" + N(inspection.OfferedNodeCount));
            sb.AppendLine("  caller_has_active_relationship: " + inspection.CallerHasActiveRelationship);
            sb.AppendLine("  derived_nodes_active/dormant: "
                + (inspection.DerivedActiveNodeCount < 0
                    ? "not derivable (authority index keyed to another account/Stone)"
                    : N(inspection.DerivedActiveNodeCount) + "/" + N(inspection.DerivedDormantNodeCount)));
            foreach (var b in inspection.Balances)
            {
                sb.AppendLine("  record[" + b.StoneId.Value + "]: personalAp=" + N(b.PersonalAp)
                    + " cumulativeAp=" + N(b.CumulativeAp) + " personalBp=" + N(b.PersonalBp)
                    + " purchases=" + N(b.PurchaseCount)
                    + " activeRelationships=" + N(b.ActiveRelationshipCount)
                    + " skillCapChoices=" + N(b.SkillCapChoiceCount));
            }
            sb.AppendLine("  derived_fingerprint: " + inspection.DerivedFingerprint);

            sb.AppendLine("-- quarantine --");
            if (inspection.Quarantine.IsClean)
            {
                sb.AppendLine("  CLEAN (no contradictory or unknown record isolated)");
            }
            else
            {
                sb.AppendLine("  QUARANTINE: " + N(inspection.Quarantine.Notices.Count)
                    + " isolated record(s); none repaired, none guessed");
                foreach (var notice in inspection.Quarantine.Notices)
                    sb.AppendLine("    - " + notice);
            }

            sb.AppendLine(BootIsNotCorrectnessCaveat);
            return sb.ToString();
        }

        /// <summary>Inspect and render in one call.</summary>
        public static string BuildAndRender(
            HomesteadProgressionCatalog catalog,
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority) =>
            Render(Inspect(catalog, stone, character, authority));

        /// <summary>Render an EXPLICIT disposable-fixture reset (AT-UNRELEASED-DATA-RESET). States what
        /// was discarded and what the rebuilt baseline derives, so a reset is auditable rather than a
        /// silent reinterpretation. Rendering only — the reset itself was performed by the shipped
        /// ProgressionStateRepair.ResetIncompatibleFixture.</summary>
        public static string RenderReset(FixtureResetResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Homestead Progression Disposable-Fixture Reset ===");
            sb.AppendLine("stone: " + result.Stone.StoneId.Value);
            sb.AppendLine("content_registry_version: " + N(result.ContentRegistryVersionBefore)
                + " -> " + N(result.ContentRegistryVersionAfter));
            if (!result.WasReset)
            {
                sb.AppendLine("action: NONE (compatible fixture; nothing discarded, derived view rebuilt)");
            }
            else
            {
                sb.AppendLine("action: RESET (incompatible UNRELEASED fixture explicitly discarded)");
                sb.AppendLine("  discarded: committed Trees, node development, this Stone's character");
                sb.AppendLine("             record (balances, purchases, relationships, skill-cap choices),");
                sb.AppendLine("             and the account-Stone authority reservations");
                sb.AppendLine("  preserved: Stone identity and the preconfigured proof Stone levels");
                sb.AppendLine("  NOT a production migration: no purchase, balance, item property or");
                sb.AppendLine("  relationship was invented, and no real progression was rewritten.");
            }
            sb.AppendLine("rebuilt_view_active_stone_level: "
                + N(result.RebuiltView.ActiveStoneLevel));
            sb.AppendLine("rebuilt_view_caller_has_active_relationship: "
                + result.RebuiltView.CallerHasActiveRelationship);
            sb.AppendLine("rebuilt_view_node_rows: " + N(result.RebuiltView.Nodes.Count));
            sb.AppendLine(BootIsNotCorrectnessCaveat);
            return sb.ToString();
        }

        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
