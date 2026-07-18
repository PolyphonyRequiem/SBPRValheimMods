using System.IO;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// R6 (Blocker 5) — engine-free atomic, crash-safe ledger IO. Covers the atomic replace-without-delete
    /// contract, crash-boundary recovery (leftover temp / backup), and fail-closed on unreadable corruption.
    /// </summary>
    public sealed class HomesteadLedgerAtomicIoTests
    {
        private const string World = "uid:-898655635";
        private const string V1 = "niflheim-homestead-playtest-v1";

        private sealed class Paths : System.IDisposable
        {
            internal readonly string Dir;
            internal string Live => Path.Combine(Dir, "w.ledger.txt");
            internal string Temp => Path.Combine(Dir, "w.ledger.txt.tmp");
            internal string Backup => Path.Combine(Dir, "w.ledger.txt.bak");
            internal Paths()
            {
                Dir = Path.Combine(Path.GetTempPath(), "sbpr-ledger-test-" + System.Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Dir);
            }
            public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
        }

        private static string Serialized(int zx, int zz, HomesteadEventOutcome outcome)
        {
            var l = new HomesteadWorldLedger();
            l.SetWorldIdentity(World);
            l.Record(zx, zz, outcome, V1, "detail");
            return l.Serialize();
        }

        [Fact]
        public void Write_then_load_round_trips_the_ledger()
        {
            using var p = new Paths();
            HomesteadLedgerAtomicIo.WriteAtomic(p.Live, p.Temp, p.Backup, Serialized(1, 2, HomesteadEventOutcome.NoValidSeat));

            var loaded = HomesteadLedgerAtomicIo.LoadWithRecovery(World, p.Live, p.Temp, p.Backup);
            Assert.True(loaded.TryGet(1, 2, out var record));
            Assert.Equal(HomesteadEventOutcome.NoValidSeat, record.Outcome);
            Assert.False(File.Exists(p.Temp));   // temp consumed by the rename
        }

        [Fact]
        public void A_second_write_keeps_a_backup_of_the_previous_good_file()
        {
            using var p = new Paths();
            HomesteadLedgerAtomicIo.WriteAtomic(p.Live, p.Temp, p.Backup, Serialized(1, 2, HomesteadEventOutcome.NoValidSeat));
            HomesteadLedgerAtomicIo.WriteAtomic(p.Live, p.Temp, p.Backup, Serialized(3, 4, HomesteadEventOutcome.Created));

            Assert.True(File.Exists(p.Backup));   // previous good file retained (no delete-then-move window)
            var loaded = HomesteadLedgerAtomicIo.LoadWithRecovery(World, p.Live, p.Temp, p.Backup);
            Assert.True(loaded.TryGet(3, 4, out var record));
            Assert.Equal(HomesteadEventOutcome.Created, record.Outcome);
        }

        [Fact]
        public void A_missing_live_file_with_no_temp_or_backup_is_an_empty_ledger()
        {
            using var p = new Paths();
            var loaded = HomesteadLedgerAtomicIo.LoadWithRecovery(World, p.Live, p.Temp, p.Backup);
            Assert.Equal(0, loaded.Count);
        }

        [Fact]
        public void A_crash_before_the_first_rename_recovers_from_the_leftover_temp()
        {
            using var p = new Paths();
            // Simulate a crash: contents landed in the temp file but the rename never happened.
            File.WriteAllText(p.Temp, Serialized(5, 6, HomesteadEventOutcome.ManifestRequired));

            var loaded = HomesteadLedgerAtomicIo.LoadWithRecovery(World, p.Live, p.Temp, p.Backup);
            Assert.True(loaded.TryGet(5, 6, out var record));
            Assert.Equal(HomesteadEventOutcome.ManifestRequired, record.Outcome);
        }

        [Fact]
        public void A_corrupt_live_file_recovers_from_a_valid_backup()
        {
            using var p = new Paths();
            File.WriteAllText(p.Live, "garbage-not-our-schema");
            File.WriteAllText(p.Backup, Serialized(7, 8, HomesteadEventOutcome.Created));

            var loaded = HomesteadLedgerAtomicIo.LoadWithRecovery(World, p.Live, p.Temp, p.Backup);
            Assert.True(loaded.TryGet(7, 8, out var record));
            Assert.Equal(HomesteadEventOutcome.Created, record.Outcome);
        }

        [Fact]
        public void A_corrupt_live_file_with_no_recovery_source_fails_closed()
        {
            using var p = new Paths();
            File.WriteAllText(p.Live, "garbage-not-our-schema");

            Assert.Throws<HomesteadLedgerIoException>(() =>
                HomesteadLedgerAtomicIo.LoadWithRecovery(World, p.Live, p.Temp, p.Backup));
        }
    }
}
