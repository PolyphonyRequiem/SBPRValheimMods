// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  LedgerSnapshot + SnapshotCodec — the pure, deterministic durable form of an
//  OwnedResourceLedger. Crash recovery reloads a snapshot, rebuilds the ledger,
//  and reconciles against world truth. The codec is a total function: Encode then
//  Decode round-trips exactly, and a malformed/truncated snapshot decodes to a
//  typed failure (never a partial ledger and never an exception the caller can't
//  see coming), so a crash mid-write cannot corrupt recovery into deleting
//  unrelated objects.
//
//  Format is a tiny line record: one header line + one line per entry. Fields are
//  tab-separated and every string field is escaped, so a fixture/logical id with
//  odd characters cannot break the framing. Engine-free: System.* only — no JSON
//  dependency, no engine serializer.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>Immutable durable snapshot of a ledger: the fixture id plus every entry.</summary>
    public sealed class LedgerSnapshot
    {
        public LedgerSnapshot(string fixtureId, IReadOnlyList<OwnedResourceEntry> entries)
        {
            if (string.IsNullOrEmpty(fixtureId)) throw new ArgumentException("fixtureId must be non-empty.", nameof(fixtureId));
            FixtureId = fixtureId;
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public string FixtureId { get; }
        public IReadOnlyList<OwnedResourceEntry> Entries { get; }
    }

    public sealed class SnapshotDecodeResult
    {
        private SnapshotDecodeResult(bool ok, LedgerSnapshot? snapshot, string error)
        {
            Ok = ok;
            Snapshot = snapshot;
            Error = error ?? string.Empty;
        }

        public bool Ok { get; }
        public LedgerSnapshot? Snapshot { get; }
        public string Error { get; }

        public static SnapshotDecodeResult Success(LedgerSnapshot s) => new SnapshotDecodeResult(true, s, string.Empty);
        public static SnapshotDecodeResult Failure(string error) => new SnapshotDecodeResult(false, null, error);
    }

    /// <summary>Pure, deterministic snapshot codec. No I/O, no world access — the caller owns durability
    /// (writing the encoded string to a durable store); the codec only maps snapshot &lt;-&gt; text.</summary>
    public static class SnapshotCodec
    {
        private const string Magic = "SBPR-QA-FIXLEDGER";
        private const int Version = 1;

        public static string Encode(LedgerSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var sb = new StringBuilder();
            sb.Append(Magic).Append('\t')
              .Append(Version.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(Esc(snapshot.FixtureId)).Append('\t')
              .Append(snapshot.Entries.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');

            foreach (var e in snapshot.Entries)
            {
                sb.Append(Esc(e.Id.FixtureId)).Append('\t')
                  .Append(Esc(e.Id.LogicalId)).Append('\t')
                  .Append(e.Id.Ordinal.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(((int)e.Category).ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(Esc(e.LogicalId)).Append('\t')
                  .Append(e.RadiusMeters.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(((int)e.State).ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(Esc(e.Handle)).Append('\n');
            }
            return sb.ToString();
        }

        public static SnapshotDecodeResult Decode(string text)
        {
            if (text == null) return SnapshotDecodeResult.Failure("null snapshot text");
            var lines = text.Split('\n');
            if (lines.Length == 0) return SnapshotDecodeResult.Failure("empty snapshot");

            var header = lines[0].Split('\t');
            if (header.Length != 4) return SnapshotDecodeResult.Failure("malformed header");
            if (!string.Equals(header[0], Magic, StringComparison.Ordinal))
                return SnapshotDecodeResult.Failure("bad magic");
            if (!int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ver) || ver != Version)
                return SnapshotDecodeResult.Failure("unsupported version");
            string fixtureId = Unesc(header[2]);
            if (!int.TryParse(header[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int declaredCount) || declaredCount < 0)
                return SnapshotDecodeResult.Failure("bad entry count");

            var entries = new List<OwnedResourceEntry>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue; // trailing newline
                var f = lines[i].Split('\t');
                if (f.Length != 8) return SnapshotDecodeResult.Failure("malformed entry line " + i);

                string idFixture = Unesc(f[0]);
                string idLogical = Unesc(f[1]);
                if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ordinal) || ordinal < 0)
                    return SnapshotDecodeResult.Failure("bad ordinal on line " + i);
                if (!int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int catRaw)
                    || !Enum.IsDefined(typeof(ResourceCategory), catRaw))
                    return SnapshotDecodeResult.Failure("bad category on line " + i);
                string logicalId = Unesc(f[4]);
                if (!double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double radius)
                    || double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0)
                    return SnapshotDecodeResult.Failure("bad radius on line " + i);
                if (!int.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stateRaw)
                    || !Enum.IsDefined(typeof(OwnedResourceState), stateRaw))
                    return SnapshotDecodeResult.Failure("bad state on line " + i);
                string handle = Unesc(f[7]);

                OwnedResourceId id;
                try
                {
                    id = new OwnedResourceId(idFixture, idLogical, ordinal);
                }
                catch (Exception ex)
                {
                    return SnapshotDecodeResult.Failure("bad id on line " + i + ": " + ex.Message);
                }

                entries.Add(new OwnedResourceEntry(id, (ResourceCategory)catRaw, logicalId,
                    radius, (OwnedResourceState)stateRaw, handle));
            }

            if (entries.Count != declaredCount)
                return SnapshotDecodeResult.Failure("entry count mismatch: header " + declaredCount + " vs body " + entries.Count);

            return SnapshotDecodeResult.Success(new LedgerSnapshot(fixtureId, entries));
        }

        // Escape tab/newline/backslash so no field can break the line/tab framing.
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case '\\': sb.Append('\\'); break;
                        case 't': sb.Append('\t'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        default: sb.Append(n); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
