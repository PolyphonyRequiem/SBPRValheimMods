using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Recovery
{
    // ProgressionStateRepair (tasks.md T005). Two operator-facing behaviours over the versioned
    // aggregates, both grounded in data-model.md §"Validation and recovery":
    //
    //   AT-INVARIANT-QUARANTINE  — scan a loaded aggregate set against its own invariants AND the
    //     current-build registry; ISOLATE contradictory/unknown records and REPORT why. It never
    //     guesses which side of an interrupted mutation is correct and never invents a repair.
    //
    //   AT-UNRELEASED-DATA-RESET — when a Stone aggregate carries an INCOMPATIBLE unreleased
    //     content-registry version, EXPLICITLY reset the disposable fixture to a clean current-build
    //     baseline and REBUILD the derived view. Production migration/grandfathering is deferred
    //     (data-model.md modeling rule 6): incompatible unreleased test data may be reset rather than
    //     silently reinterpreted. This is the disposable-fixture path only — it never rewrites real
    //     production progression, because none exists in this proof.
    //
    // net48 audit: engine-free (value objects + snapshot codec + the pure DerivedActivationView). No
    // net5+ surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 tests.

    public enum QuarantineReason
    {
        StoneLevelInvariant,        // !(0 <= ActiveStoneLevel <= HistoricalStoneLevel)
        NegativeMirroredAp,         // Mirrored Stone AP is negative (ledgers are non-negative)
        UnknownNodeDevelopment,     // a developed node is not in the current-build registry
        UnknownPurchaseNode,        // a character purchase references an unknown/stale node
        NegativeCharacterBalance,   // Personal/Cumulative AP or BP negative
        LocalNodePurchased,         // a Local Node appears as a personal purchase (never allowed)
        AuthorityMismatch,          // authority index keyed to another account/Stone than the caller
        InvalidRevision,            // an aggregate revision is negative (revisions are non-negative)
        UnsupportedSchemaVersion,   // an aggregate schema version is not one the current build supports
        ContentVersionMismatch,     // the Stone's content-registry version is not the current build's
        WrongTreePurchase,          // a purchase's claimed Tree does not own the resolved node
        UnavailableNodeDevelopment, // a developed node is first-build Unavailable (rejects development)
        UnavailableNodePurchased,   // a purchase references a first-build Unavailable node
        NegativeLedgerValue         // a modeled ledger field (committed BP, node BP progress/cost, facet credit) is negative
    }

    public readonly struct QuarantineNotice
    {
        public QuarantineNotice(QuarantineReason reason, string subjectId, string detail)
        {
            Reason = reason;
            SubjectId = subjectId ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public QuarantineReason Reason { get; }

        /// <summary>Stable identity of the offending record (node key, Stone id, etc.).</summary>
        public string SubjectId { get; }
        public string Detail { get; }

        public override string ToString() => Reason + " [" + SubjectId + "]: " + Detail;
    }

    public sealed class QuarantineReport
    {
        private readonly List<QuarantineNotice> _notices;

        public QuarantineReport(List<QuarantineNotice> notices)
        {
            _notices = notices ?? new List<QuarantineNotice>();
        }

        public bool IsClean => _notices.Count == 0;
        public IReadOnlyList<QuarantineNotice> Notices => _notices;

        public bool Has(QuarantineReason reason)
        {
            foreach (var n in _notices)
                if (n.Reason == reason) return true;
            return false;
        }
    }

    public readonly struct FixtureResetResult
    {
        public FixtureResetResult(bool wasReset, int contentRegistryVersionBefore, int contentRegistryVersionAfter,
            StoneProgressionAggregate stone, CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority, DerivedActivationView rebuiltView)
        {
            WasReset = wasReset;
            ContentRegistryVersionBefore = contentRegistryVersionBefore;
            ContentRegistryVersionAfter = contentRegistryVersionAfter;
            Stone = stone;
            Character = character;
            Authority = authority;
            RebuiltView = rebuiltView;
        }

        public bool WasReset { get; }
        public int ContentRegistryVersionBefore { get; }
        public int ContentRegistryVersionAfter { get; }
        public StoneProgressionAggregate Stone { get; }

        /// <summary>The disposable character state after reset. On an incompatible reset the affected
        /// Stone record is cleared to a clean baseline (no stale purchases/balances survive); on a
        /// compatible no-op the caller's character is returned unchanged.</summary>
        public CharacterProgressionAggregate Character { get; }

        /// <summary>The disposable authority state after reset. On an incompatible reset the authority
        /// is released (vacant) so no stale active relationship survives the rebuilt projection.</summary>
        public AccountStoneAuthorityIndex Authority { get; }

        public DerivedActivationView RebuiltView { get; }
    }

    public sealed class ProgressionStateRepair
    {
        private readonly HomesteadProgressionCatalog _catalog;

        public ProgressionStateRepair(HomesteadProgressionCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Scan a loaded aggregate set for contradictions and unknown/stale content, isolating
        /// each offending record with a reason. Never mutates the aggregates and never guesses a repair
        /// (AT-INVARIANT-QUARANTINE).</summary>
        public QuarantineReport Scan(
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            var notices = new List<QuarantineNotice>();

            // Envelope validation (data-model.md §"Validation and recovery": validate aggregate revisions
            // and schema versions on load). Revisions are non-negative monotone counters; a negative one
            // is a corrupt/interrupted envelope. Schema versions must be ones this build understands —
            // an unsupported (future/unknown) schema is quarantined, never blindly reinterpreted.
            if (stone.Revision < 0)
                notices.Add(new QuarantineNotice(QuarantineReason.InvalidRevision, stone.StoneId.Value,
                    "Stone revision=" + stone.Revision + " is negative"));
            if (character.Revision < 0)
                notices.Add(new QuarantineNotice(QuarantineReason.InvalidRevision, character.Account.Value,
                    "character revision=" + character.Revision + " is negative"));
            if (authority.Revision < 0)
                notices.Add(new QuarantineNotice(QuarantineReason.InvalidRevision, authority.Account.Value,
                    "authority revision=" + authority.Revision + " is negative"));

            if (stone.SchemaVersion != StoneProgressionAggregate.CurrentSchemaVersion)
                notices.Add(new QuarantineNotice(QuarantineReason.UnsupportedSchemaVersion, stone.StoneId.Value,
                    "Stone schema version " + stone.SchemaVersion + " != supported "
                    + StoneProgressionAggregate.CurrentSchemaVersion));
            if (character.SchemaVersion != CharacterProgressionAggregate.CurrentSchemaVersion)
                notices.Add(new QuarantineNotice(QuarantineReason.UnsupportedSchemaVersion, character.Account.Value,
                    "character schema version " + character.SchemaVersion + " != supported "
                    + CharacterProgressionAggregate.CurrentSchemaVersion));
            if (authority.SchemaVersion != AccountStoneAuthorityIndex.CurrentSchemaVersion)
                notices.Add(new QuarantineNotice(QuarantineReason.UnsupportedSchemaVersion, authority.Account.Value,
                    "authority schema version " + authority.SchemaVersion + " != supported "
                    + AccountStoneAuthorityIndex.CurrentSchemaVersion));

            // Content-registry version: a Stone stamped with a content build other than the current one
            // is an incompatible fixture. Scan REPORTS it (the operator then chooses explicit reset via
            // ResetIncompatibleFixture); it never silently reinterprets stale content.
            if (stone.ContentRegistryVersion != _catalog.ContentRegistryVersion)
                notices.Add(new QuarantineNotice(QuarantineReason.ContentVersionMismatch, stone.StoneId.Value,
                    "Stone content-registry version " + stone.ContentRegistryVersion
                    + " != current build " + _catalog.ContentRegistryVersion));

            // Stone-level invariant: 0 <= Active <= Historical.
            if (!(stone.ActiveStoneLevel >= 0 && stone.ActiveStoneLevel <= stone.HistoricalStoneLevel))
                notices.Add(new QuarantineNotice(QuarantineReason.StoneLevelInvariant, stone.StoneId.Value,
                    "ActiveStoneLevel=" + stone.ActiveStoneLevel + " HistoricalStoneLevel=" + stone.HistoricalStoneLevel));

            // Mirrored Stone AP is an accumulate-only, non-negative ledger.
            if (stone.MirroredStoneAp < 0)
                notices.Add(new QuarantineNotice(QuarantineReason.NegativeMirroredAp, stone.StoneId.Value,
                    "MirroredStoneAp=" + stone.MirroredStoneAp));

            // Modeled Stone-side ledgers are non-negative (data-model.md §"Validation and recovery":
            // validate all ledger non-negativity). Committed-Tree cumulative BP invested is an
            // accumulate-only counter; a negative value is corrupt state, isolated not repaired.
            foreach (var ct in stone.CommittedTrees)
            {
                if (ct.CumulativeBpInvested < 0)
                    notices.Add(new QuarantineNotice(QuarantineReason.NegativeLedgerValue, ct.Tree.Key,
                        "committed tree '" + ct.Tree.Key + "' CumulativeBpInvested=" + ct.CumulativeBpInvested + " is negative"));
            }

            // Every developed node must belong to the current-build registry (unknown same-build
            // references reject clearly — here they quarantine rather than misbind).
            foreach (var dev in stone.NodeDevelopment)
            {
                // Per-node BP ledger fields are non-negative (accumulated progress toward a non-negative
                // authored cost). Negative progress or cost is contradictory state — isolate, never repair.
                if (dev.BpProgress < 0)
                    notices.Add(new QuarantineNotice(QuarantineReason.NegativeLedgerValue, dev.Node.Key,
                        "node '" + dev.Node.Key + "' BpProgress=" + dev.BpProgress + " is negative"));
                if (dev.BpCost < 0)
                    notices.Add(new QuarantineNotice(QuarantineReason.NegativeLedgerValue, dev.Node.Key,
                        "node '" + dev.Node.Key + "' BpCost=" + dev.BpCost + " is negative"));

                var devDef = _catalog.TryResolveNode(dev.Node);
                if (devDef == null)
                {
                    notices.Add(new QuarantineNotice(QuarantineReason.UnknownNodeDevelopment, dev.Node.Key,
                        _catalog.HasNodeKey(dev.Node)
                            ? "developed node version " + dev.Node.Version + " is not the current build"
                            : "developed node key is unknown to the current build"));
                    continue;
                }
                // A first-build Unavailable node authors no developable gate; a persisted development
                // record for one is contradictory state and is isolated, never accepted as real.
                if (devDef.Status == NodeFirstBuildStatus.Unavailable)
                    notices.Add(new QuarantineNotice(QuarantineReason.UnavailableNodeDevelopment, dev.Node.Key,
                        "developed node '" + devDef.DisplayLabel + "' is unavailable in the first build"));
            }

            // Authority index must key to this caller's account and this Stone.
            if (!authority.Account.Equals(character.Account))
                notices.Add(new QuarantineNotice(QuarantineReason.AuthorityMismatch, authority.Account.Value,
                    "authority account != character account " + character.Account.Value));
            if (!authority.StoneId.Equals(stone.StoneId))
                notices.Add(new QuarantineNotice(QuarantineReason.AuthorityMismatch, authority.StoneId.Value,
                    "authority Stone != Stone aggregate " + stone.StoneId.Value));

            // Character-side ledgers and purchase ownership.
            foreach (var sr in character.StoneRecords)
            {
                if (sr.PersonalAp < 0 || sr.CumulativeAp < 0 || sr.PersonalBp < 0)
                    notices.Add(new QuarantineNotice(QuarantineReason.NegativeCharacterBalance, sr.StoneId.Value,
                        "PersonalAp=" + sr.PersonalAp + " CumulativeAp=" + sr.CumulativeAp + " PersonalBp=" + sr.PersonalBp));

                // Facet Credit is a non-negative ledger (data-model.md §"Validation and recovery":
                // validate all ledger non-negativity). A negative credit amount is corrupt state.
                foreach (var fc in sr.FacetCredits)
                {
                    if (fc.Amount < 0)
                        notices.Add(new QuarantineNotice(QuarantineReason.NegativeLedgerValue, sr.StoneId.Value,
                            "facet credit '" + fc.FacetId + "' Amount=" + fc.Amount + " is negative"));
                }

                foreach (var p in sr.Purchases)
                {
                    var def = _catalog.TryResolveNode(p.Node);
                    if (def == null)
                    {
                        notices.Add(new QuarantineNotice(QuarantineReason.UnknownPurchaseNode, p.Node.Key,
                            _catalog.HasNodeKey(p.Node)
                                ? "purchased node version " + p.Node.Version + " is not the current build"
                                : "purchased node key is unknown to the current build"));
                        continue;
                    }
                    // Local Nodes are Stone-owned and never appear as a personal purchase.
                    if (def.Ownership == NodeOwnership.StoneCultivated)
                        notices.Add(new QuarantineNotice(QuarantineReason.LocalNodePurchased, p.Node.Key,
                            "Local Node '" + def.DisplayLabel + "' cannot be a personal purchase"));

                    // A first-build Unavailable node rejects purchase/Offering; a persisted purchase for
                    // one is contradictory and is isolated, never accepted.
                    if (def.Status == NodeFirstBuildStatus.Unavailable)
                        notices.Add(new QuarantineNotice(QuarantineReason.UnavailableNodePurchased, p.Node.Key,
                            "purchased node '" + def.DisplayLabel + "' is unavailable in the first build"));

                    // The purchase's claimed Tree must own the resolved node (known node recorded under
                    // the wrong Tree is contradictory registry state — isolate, never rebind).
                    if (!def.Tree.Equals(p.Tree))
                        notices.Add(new QuarantineNotice(QuarantineReason.WrongTreePurchase, p.Node.Key,
                            "purchased node '" + p.Node.Key + "' belongs to tree '" + def.Tree.Key
                            + "', not claimed tree '" + p.Tree.Key + "'"));
                }
            }

            return new QuarantineReport(notices);
        }

        /// <summary>Reset an incompatible unreleased Stone fixture to a clean current-build baseline and
        /// rebuild the derived view. If the Stone already carries the current content-registry version,
        /// this is a no-op reset (WasReset=false) — the fixture is compatible and nothing is discarded.
        /// This is the disposable-fixture path only (AT-UNRELEASED-DATA-RESET).</summary>
        public FixtureResetResult ResetIncompatibleFixture(
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority,
            string resetProvenance)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            int before = stone.ContentRegistryVersion;

            if (before == _catalog.ContentRegistryVersion)
            {
                // Compatible: keep the fixture as-is, just rebuild the derived projection.
                return new FixtureResetResult(false, before, before, stone, character, authority,
                    DerivedActivationView.Derive(stone, character, authority));
            }

            string prov = resetProvenance ?? "reset:incompatible-fixture";

            // Incompatible unreleased fixture: EXPLICITLY discard the disposable selected/developed
            // state and rebuild a clean current-build baseline. We preserve only stable identity and
            // the preconfigured proof levels (data-model.md: proof begins at Historical/Active 2). No
            // production migration/grandfathering is attempted.
            var resetStone = new StoneProgressionAggregate(
                stone.StoneId,
                revision: stone.Revision + 1,
                historicalStoneLevel: 2,
                activeStoneLevel: 2,
                foundationalTree: _catalog.FoundationalTree,
                foundationalCatalog: _catalog.FoundationalCatalog,
                contentRegistryVersion: _catalog.ContentRegistryVersion,
                createdProvenance: stone.CreatedProvenance,
                updatedProvenance: prov,
                mirroredStoneAp: 0,
                lastAppliedReceiptId: prov,
                committedTrees: Array.Empty<CommittedTreeRecord>(),
                nodeDevelopment: Array.Empty<NodeDevelopmentRecord>(),
                family: _catalog.Family,
                variant: _catalog.Variant);

            // The disposable Homestead/character test state is reset too (research.md: "reset the
            // disposable Homestead/character test state explicitly"). A stale character purchase or
            // balance keyed to this Stone must NOT survive a claimed clean reset, so we drop this
            // Stone's record entirely and rebuild it as a clean 0/0/0 baseline with no purchases. Other
            // Stones' records are untouched. This is disposable-fixture reset, not production migration.
            var resetStoneRecords = new List<CharacterStoneRecord>();
            foreach (var sr in character.StoneRecords)
            {
                if (sr.StoneId.Equals(stone.StoneId))
                    continue; // stale disposable record dropped; rebuilt clean below
                resetStoneRecords.Add(sr);
            }
            resetStoneRecords.Add(new CharacterStoneRecord(stone.StoneId, personalAp: 0, cumulativeAp: 0, personalBp: 0));

            var resetCharacter = new CharacterProgressionAggregate(
                character.Account,
                character.Character,
                character.WorldProductScope,
                revision: character.Revision + 1,
                bondSlots: character.BondSlots,
                attunementSlots: character.AttunementSlots,
                lastAppliedReceiptId: prov,
                stoneRecords: resetStoneRecords);

            // Release the authority: no stale active relationship may survive the rebuilt projection.
            var resetAuthority = new AccountStoneAuthorityIndex(
                authority.Account,
                authority.StoneId,
                revision: authority.Revision + 1,
                reservations: null,
                lastReleaseReceiptId: prov);

            var view = DerivedActivationView.Derive(resetStone, resetCharacter, resetAuthority);
            return new FixtureResetResult(true, before, _catalog.ContentRegistryVersion,
                resetStone, resetCharacter, resetAuthority, view);
        }
    }
}
