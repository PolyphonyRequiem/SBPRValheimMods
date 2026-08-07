using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace Tracer3Harness
{
    // Independent Tracer-3 (T011) verification harness. Link-compiles the SHIPPED T010 Facet slice
    // (StoneFacets + FacetCommandHandler + StoneAggregateStore) and drives it through REAL
    // out-of-process death: a `commit-kill` child commits one Tree (the FacetCommandHandler fsyncs
    // its intent+committed journal boundaries inside Handle) and then SIGKILLs ITS OWN pid — no
    // managed unwind, no finally, no graceful close. A fresh `recover` process then reconstructs the
    // Stone projection from the fsync'd journal ONLY and proves the commitment persisted EXACTLY ONCE
    // (one Committed Tree, revision advanced once, same-op resubmission Replays rather than
    // double-commits). This is the dimension the in-process xUnit restart test cannot prove: the
    // in-process test rehydrates a fresh handler in the SAME live process; here the writer process is
    // genuinely dead before the reader boots.
    internal static class Program
    {
        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);
        private const int SIGKILL = 9;

        // ── Fixture identities (mirror tests/NiflheimFacetCommitTests.cs) ──
        private static readonly WorldId World = new WorldId("uid:facet-harness");
        private static StoneId Stone() => StoneId.FromHostZone(World, 12, -4);
        private static readonly AccountId Account = new AccountId("acct-gov");
        private static readonly CharacterId Governor = new CharacterId("char-gov");
        private const string BondRelId = "rel-bond-gov";

        private static int Main(string[] args)
        {
            if (args.Length == 0) { Console.Error.WriteLine("need mode"); return 2; }
            switch (args[0])
            {
                case "commit-kill": return CommitKill(args[1], args[2]);
                case "recover":     return Recover(args[1], args[2]);
                default: Console.Error.WriteLine("unknown mode " + args[0]); return 2;
            }
        }

        // Seed base state (empty Facets, Stone Level 2, revision 5) and build a handler over the
        // journal. The handler rehydrates from journal at construction, so a re-run over a journal
        // that already has the commit is a pure projection rebuild.
        private static FacetCommandHandler NewHandler(string journal, out InMemoryStoneAggregateStore stones)
        {
            stones = new InMemoryStoneAggregateStore();
            stones.PutStone(BuildStone(revision: 5, activeLevel: 2));
            var characters = new InMemoryCharacterAggregateStore();
            characters.PutCharacter(BuildGovernor());
            var authority = new InMemoryAccountStoneAuthorityStore();
            authority.ApplyAuthorityProjection("seed-bond", BondIndex());
            return new FacetCommandHandler(journal, new PrincipalResolver(),
                stones, characters, authority, new AllowGovernorPolicy());
        }

        // Commit Cooking into the Profession Facet, then SIGKILL self — the journal is already
        // fsync'd inside Handle, so a hard death here leaves exactly the committed boundary durable.
        private static int CommitKill(string journal, string opId)
        {
            var handler = NewHandler(journal, out _);
            var result = handler.Handle(GovCommit(opId, HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            Console.Out.Write("CHILD_COMMITTED OUTCOME=" + result.Outcome +
                " CODE=" + result.ResultCode + " REV=" + result.StoneRevision);
            Console.Out.Flush();
            kill(Process.GetCurrentProcess().Id, SIGKILL); // real death, no unwind
            Environment.FailFast("SIGKILL did not take"); // unreachable
            return 0;
        }

        // Fresh process: rebuild projection from the fsync'd journal ONLY, report the reconstructed
        // commitment, then resubmit the same op and confirm it Replays (exactly-once, no double bump).
        private static int Recover(string journal, string opId)
        {
            var handler = NewHandler(journal, out var stones);
            var stone = stones.GetStone(Stone());
            int count = stone == null ? -1 : stone.CommittedTrees.Count;
            string key = (stone != null && stone.CommittedTrees.Count > 0)
                ? stone.CommittedTrees[0].Tree.Key : "<none>";
            long rev = stone == null ? -1 : stone.Revision;
            Console.Out.WriteLine("BOOT_COMMITTED_COUNT=" + count);
            Console.Out.WriteLine("BOOT_COMMITTED_KEY=" + key);
            Console.Out.WriteLine("BOOT_STONE_REV=" + rev.ToString(CultureInfo.InvariantCulture));

            var replay = handler.Handle(GovCommit(opId, HomesteadProgressionCatalog.ProfessionFacetId, "Cooking"));
            var after = stones.GetStone(Stone());
            Console.Out.WriteLine("REPLAY_OUTCOME=" + replay.Outcome);
            Console.Out.WriteLine("REPLAY_CODE=" + replay.ResultCode);
            Console.Out.WriteLine("POST_COMMITTED_COUNT=" + (after == null ? -1 : after.CommittedTrees.Count));
            Console.Out.WriteLine("POST_STONE_REV=" + (after == null ? -1 : after.Revision));
            Console.Out.WriteLine("REPLAY_RECEIPT=" + replay.ReceiptId);
            return 0;
        }

        // ── Command + fixture builders (identical shape to the xUnit fixtures) ──

        private static CommitTreeToFacetCommand GovCommit(string op, string facetId, string treeKey)
            => new CommitTreeToFacetCommand(new OperationId(op), Stone(),
                new AuthenticatedConnection(Account.Value, Governor.Value), default,
                facetId, treeKey, 1, StoneFacetPalette.CurrentPaletteVersion, 5);

        private static StoneProgressionAggregate BuildStone(long revision, int activeLevel)
            => new StoneProgressionAggregate(Stone(), revision,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 9, lastAppliedReceiptId: "r-seed",
                committedTrees: null, nodeDevelopment: null);

        private static CharacterProgressionAggregate BuildGovernor()
        {
            var bond = new RelationshipRecord(BondRelId, RelationshipKind.Bond, RelationshipStatus.Active,
                "Homestead:All", "Governor", "relreceipt:seed-bond", string.Empty);
            var stoneRecord = new CharacterStoneRecord(Stone(), 7, 7, 4,
                // ADO #132 provenance note: `facetCredits` was removed from CharacterStoneRecord when the
                // Facet Credit rule was retired. This file is FROZEN Tracer-3 evidence — it is not compiled
                // by any project and is preserved verbatim as the artifact that was actually run. Do not
                // "fix" it; it will not compile against current source, and that is correct.
                facetCredits: null, purchases: null, relationships: new[] { bond });
            return new CharacterProgressionAggregate(Account, Governor, "facet-harness/trailborne",
                revision: 2, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private static AccountStoneAuthorityIndex BondIndex()
            => AccountStoneAuthorityIndex.Vacant(Account, Stone()).WithReservationAdded(
                new AuthorityReservation(Governor, RelationshipKind.Bond, BondRelId, "relreceipt:seed-bond"), 1);

        private sealed class AllowGovernorPolicy : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category)
                => string.Equals(responsibilityRange, "Homestead:All", StringComparison.Ordinal)
                   && string.Equals(ownerGovernorRole, "Governor", StringComparison.Ordinal)
                   && category != FacetCategory.None;
        }
    }
}
