using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// R5 (t_2a8a8aaa) — durable per-world event provenance ledger. Covers restart persistence, exception
    /// capture, and the "no phantom retries after vanilla sets the generated flag" invariant.
    /// </summary>
    public sealed class HomesteadWorldLedgerTests
    {
        private const string World = "uid:-898655635";
        private const string V1 = "niflheim-homestead-playtest-v1";
        private const string V2 = "niflheim-homestead-playtest-v2";

        [Fact]
        public void A_failure_survives_a_restart_via_serialize_deserialize()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.SetWorldIdentity(World);
            ledger.Record(1, -2, HomesteadEventOutcome.NoValidSeat, V1, "no seat within 6m");

            // "Restart": serialize to the durable blob, drop the in-memory object, rehydrate.
            var rehydrated = HomesteadWorldLedger.Deserialize(World, ledger.Serialize());

            Assert.True(rehydrated.IsTerminal(1, -2, V1));
            Assert.True(rehydrated.TryGet(1, -2, out var record));
            Assert.Equal(HomesteadEventOutcome.NoValidSeat, record.Outcome);
            Assert.Equal("no seat within 6m", record.Detail);
        }

        [Fact]
        public void A_same_version_failure_re_observation_is_not_a_phantom_retry()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.Record(1, -2, HomesteadEventOutcome.NoValidSeat, V1, "first");
            // Re-observing the same zone under the SAME selector version must NOT create a new event and must
            // still report terminal — this is what stops counter-only retries after vanilla's generated flag.
            ledger.Record(1, -2, HomesteadEventOutcome.NoValidSeat, V1, "second");

            Assert.True(ledger.IsTerminal(1, -2, V1));
            Assert.True(ledger.TryGet(1, -2, out var record));
            Assert.Equal("first", record.Detail);   // original event preserved, not overwritten
            Assert.Equal(1, ledger.Count);
        }

        [Fact]
        public void A_selector_version_change_reopens_a_prior_failure()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.Record(1, -2, HomesteadEventOutcome.NoValidSeat, V1, "v1 failure");

            Assert.True(ledger.IsTerminal(1, -2, V1));
            Assert.False(ledger.IsTerminal(1, -2, V2));   // a new selector version may legitimately re-attempt
        }

        [Fact]
        public void Created_is_sticky_and_wins_over_a_later_failure()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.Record(1, -2, HomesteadEventOutcome.Created, V1, "StaticGeometry");
            ledger.Record(1, -2, HomesteadEventOutcome.NoValidSeat, V1, "should be ignored");
            ledger.Record(1, -2, HomesteadEventOutcome.Exception, V2, "should also be ignored");

            Assert.True(ledger.TryGet(1, -2, out var record));
            Assert.Equal(HomesteadEventOutcome.Created, record.Outcome);
            Assert.True(ledger.IsTerminal(1, -2, V1));
            Assert.True(ledger.IsTerminal(1, -2, V2));   // Created blocks under any version
        }

        [Fact]
        public void Exceptions_are_captured_as_terminal_events()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.Record(3, 4, HomesteadEventOutcome.Exception, V1, "NullReferenceException: host root");

            Assert.True(ledger.TryGet(3, 4, out var record));
            Assert.Equal(HomesteadEventOutcome.Exception, record.Outcome);
            Assert.True(ledger.IsTerminal(3, 4, V1));
        }

        [Fact]
        public void Deserialize_of_an_unknown_blob_yields_an_empty_ledger_not_a_guess()
        {
            var ledger = HomesteadWorldLedger.Deserialize(World, "not-our-schema\ngarbage");
            Assert.Equal(0, ledger.Count);
        }

        [Fact]
        public void Round_trips_details_containing_tabs_and_newlines()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.SetWorldIdentity(World);
            ledger.Record(5, 6, HomesteadEventOutcome.ManifestRequired, V1, "line1\tcol\nline2");

            var back = HomesteadWorldLedger.Deserialize(World, ledger.Serialize());
            Assert.True(back.TryGet(5, 6, out var record));
            Assert.Equal("line1\tcol\nline2", record.Detail);
        }

        [Fact]
        public void Migration_deferred_is_a_representable_terminal_outcome()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.Record(0, 0, HomesteadEventOutcome.MigrationDeferred, V1, "existing generated world, no runtime geometry");
            Assert.True(ledger.TryGet(0, 0, out var record));
            Assert.Equal(HomesteadEventOutcome.MigrationDeferred, record.Outcome);
        }

        // ---- R6 (Blocker 5/6/1) additions ---------------------------------------------------

        [Fact]
        public void Manifest_required_becomes_retryable_when_a_newer_generation_appears()
        {
            var ledger = new HomesteadWorldLedger();
            // Recorded ManifestRequired at generation 2.
            ledger.Record(7, 7, HomesteadEventOutcome.ManifestRequired, V1, "no row", manifestGeneration: 2);

            // Same generation → still terminal (no phantom retry).
            Assert.True(ledger.IsTerminal(7, 7, V1, liveManifestGeneration: 2));
            // A NEWER generation → retryable (a fresh operator manifest may now have a row).
            Assert.False(ledger.IsTerminal(7, 7, V1, liveManifestGeneration: 3));

            // Re-recording under the newer generation supersedes the old record.
            ledger.Record(7, 7, HomesteadEventOutcome.ManifestRequired, V1, "still no row", manifestGeneration: 3);
            Assert.True(ledger.TryGet(7, 7, out var record));
            Assert.Equal(3, record.ManifestGeneration);
        }

        [Fact]
        public void Manifest_generation_survives_serialize_round_trip()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.SetWorldIdentity(World);
            ledger.Record(1, 1, HomesteadEventOutcome.ManifestRequired, V1, "no row", manifestGeneration: 5);

            var back = HomesteadWorldLedger.Deserialize(World, ledger.Serialize());
            Assert.True(back.TryGet(1, 1, out var record));
            Assert.Equal(5, record.ManifestGeneration);
            Assert.False(back.IsTerminal(1, 1, V1, liveManifestGeneration: 6));   // retry survives restart
        }

        [Fact]
        public void Created_can_be_cleared_for_recovery_when_the_stone_is_missing()
        {
            // R6 (Blocker 5): the ledger records provenance, not creation truth. A Created whose Stone ZDO
            // no longer exists must be recoverable — ClearForRecovery drops it so the loop re-creates.
            var ledger = new HomesteadWorldLedger();
            ledger.Record(2, 2, HomesteadEventOutcome.Created, V1, "StaticGeometry");
            Assert.True(ledger.IsTerminal(2, 2, V1));

            ledger.ClearForRecovery(2, 2);

            Assert.False(ledger.IsTerminal(2, 2, V1));   // reopened for re-creation
            Assert.False(ledger.TryGet(2, 2, out _));
        }

        [Fact]
        public void Catalog_unavailable_is_never_persisted_so_it_stays_retryable()
        {
            var ledger = new HomesteadWorldLedger();
            ledger.Record(9, 9, HomesteadEventOutcome.CatalogUnavailable, V1, "catalog not loaded yet");
            Assert.Equal(0, ledger.Count);                 // not stored
            Assert.False(ledger.IsTerminal(9, 9, V1));     // retryable next tick
        }

        [Fact]
        public void A_corrupt_blob_is_flagged_not_wellformed_for_the_store_to_recover()
        {
            var good = HomesteadWorldLedger.Deserialize(World, "");
            Assert.True(good.IsWellFormed);                // genuinely empty is well-formed

            var corrupt = HomesteadWorldLedger.Deserialize(World, "not-our-schema\ngarbage");
            Assert.False(corrupt.IsWellFormed);            // header mismatch → the store must try temp/backup
        }
    }
}
