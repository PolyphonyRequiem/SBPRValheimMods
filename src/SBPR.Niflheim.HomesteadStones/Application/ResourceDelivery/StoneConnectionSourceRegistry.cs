using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;

namespace SBPR.Niflheim.HomesteadStones.Application.ResourceDelivery
{
    // RD-T004 (Tracer 1) — the durable coordinator that maintains exact Connection SOURCE sets from
    // real Bond/Attunement lifecycle events (spec RD-002 / data-model Aggregate 1 / contracts
    // §"Relationship-to-Connection integration": "Connection source transitions are part of the
    // existing CreateBond, CreateAttunement, and ReleaseRelationship logical transaction"). Named
    // acceptance: AT-RD-002.
    //
    // WHAT THIS OWNS
    //   The recoverable projection from "an account's Stone relationship became active / was released"
    //   to "the exact ConnectionSource set of every affected account-pair Connection." It couples the
    //   pure QualifyingSourceRule (which decides qualification) to the pure ConnectionAggregate (which
    //   owns lifecycle/age/grace), and makes the whole thing survive restart and replay.
    //
    // EVENT-SOURCED RECOVERY (mirrors OperationReceiptStore / RelationshipCommandHandler discipline)
    //   Every accepted lifecycle event (Activated / Released) is appended to a framed, CRC-checked,
    //   fsync'd journal keyed by operationId. The in-memory projections — the per-Stone active
    //   participant roster and the per-ConnectionId aggregate — are a pure function of replaying the
    //   committed events in journal order. Because ConnectionAggregate.AddSource/RemoveSource take the
    //   event's server time, replaying in order reconstructs the EXACT age/grace/source state after a
    //   crash or restart. Re-submitting a committed operationId is an idempotent replay; reusing an
    //   operationId with a different binding is an OperationConflict.
    //
    // SCOPE (Tracer 1): source add/remove/grace/reconnect integration only. It does NOT authenticate
    // principals or authorize releases — the RelationshipCommandHandler already did that upstream and
    // hands this coordinator the resolved, authoritative facts. It introduces NO social graph and NO
    // provider-shaped identity: the only inputs are two accounts' authoritative per-Stone roles.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, CRC32 — all
    // present in .NET Framework 4.8. Engine-free; link-compiles into the net8 test project.

    public enum ConnectionSourceEventKind
    {
        /// <summary>An account's Bond/Attunement to a Stone became active — may open new qualifying
        /// sources against every other currently-active participant at that Stone.</summary>
        RelationshipActivated = 1,
        /// <summary>An account's relationship at a Stone was released — removes every source derived
        /// from a pair that involved it; a final-link removal drops the Connection into grace.</summary>
        RelationshipReleased = 2
    }

    public enum ConnectionSourceOutcome
    {
        Applied,
        Replayed,
        OperationConflict,
        NoOp
    }

    /// <summary>Result of a coordinator lifecycle event. Reports the affected canonical Connection keys
    /// so the caller can acknowledge relationship success only when Connection projections are
    /// recoverable (contracts §"acknowledge relationship success only when Connection projections are
    /// recoverable").</summary>
    public readonly struct ConnectionSourceEventResult
    {
        public ConnectionSourceEventResult(ConnectionSourceOutcome outcome, string resultCode,
            IReadOnlyList<string> affectedConnectionKeys)
        {
            Outcome = outcome;
            ResultCode = resultCode ?? string.Empty;
            AffectedConnectionKeys = affectedConnectionKeys ?? Array.Empty<string>();
        }

        public ConnectionSourceOutcome Outcome { get; }
        public string ResultCode { get; }
        public IReadOnlyList<string> AffectedConnectionKeys { get; }
    }

    public sealed class StoneConnectionSourceRegistry
    {
        private const string RecEvent = "CSRC";

        private readonly string _journalPath;

        // Projections (a pure function of the committed event log).
        // Per-Stone active participant roster, keyed by Stone value.
        private readonly Dictionary<string, List<StoneParticipant>> _rosterByStone =
            new Dictionary<string, List<StoneParticipant>>(StringComparer.Ordinal);
        // Per-Connection aggregate, keyed by canonical Connection key.
        private readonly Dictionary<string, ConnectionAggregate> _connectionsByKey =
            new Dictionary<string, ConnectionAggregate>(StringComparer.Ordinal);
        // Committed operation binding digests (idempotency).
        private readonly Dictionary<string, string> _committedOps =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public StoneConnectionSourceRegistry(string journalPath)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        // ---- Read projections ----

        /// <summary>The current aggregate for a Connection, or a fresh empty one when none exists yet.
        /// Never null so callers always have a stable maturity/lifecycle baseline.</summary>
        public ConnectionAggregate GetConnection(ConnectionId id)
        {
            if (_connectionsByKey.TryGetValue(id.CanonicalKey, out var agg)) return agg;
            return ConnectionAggregate.CreateEmpty(id);
        }

        /// <summary>The active participant roster at a Stone (defensive copy). Empty when the Stone has
        /// no active relationships.</summary>
        public IReadOnlyList<StoneParticipant> GetStoneRoster(StoneId stoneId)
        {
            if (_rosterByStone.TryGetValue(stoneId.Value, out var list))
                return new List<StoneParticipant>(list);
            return Array.Empty<StoneParticipant>();
        }

        /// <summary>Every Connection key that currently carries at least one active source. Stable order
        /// for replay/audit.</summary>
        public IReadOnlyList<string> ActiveSourceConnectionKeys()
        {
            var keys = new List<string>();
            foreach (var kv in _connectionsByKey)
                if (kv.Value.HasSources) keys.Add(kv.Key);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        // ---- Lifecycle events ----

        /// <summary>Integrate an account's newly-active Bond/Attunement at a Stone. Derives every
        /// qualifying source against the OTHER currently-active participants (spec RD-002) and adds the
        /// exact ConnectionSource to each affected Connection. Idempotent by operationId.</summary>
        public ConnectionSourceEventResult ActivateRelationship(
            string operationId, WorldId world, ProductScope product, StoneId stoneId,
            AccountId account, string relationshipId, RelationshipKind kind, long serverTimeSeconds,
            string activationProvenance = "")
        {
            var role = StoneParticipant.RoleOf(kind);
            string binding = BindingDigest(ConnectionSourceEventKind.RelationshipActivated, world, product,
                stoneId, account, relationshipId, (int)role, serverTimeSeconds);
            var idem = CheckIdempotency(operationId, binding, out var conflict);
            if (conflict != null) return conflict.Value;
            if (idem != null)
                return new ConnectionSourceEventResult(ConnectionSourceOutcome.Replayed, "Replayed",
                    AffectedForActivation(world, product, stoneId, account, relationshipId, role));

            if (role == StoneRelationshipRole.None)
                return new ConnectionSourceEventResult(ConnectionSourceOutcome.NoOp, "NotAStoneRelationship",
                    Array.Empty<string>());

            AppendEvent(operationId, binding, ConnectionSourceEventKind.RelationshipActivated, world, product,
                stoneId, account, relationshipId, (int)role, serverTimeSeconds, activationProvenance);

            var affected = ApplyActivation(world, product, stoneId, account, relationshipId, role,
                serverTimeSeconds, activationProvenance);
            return new ConnectionSourceEventResult(ConnectionSourceOutcome.Applied, "Applied", affected);
        }

        /// <summary>Integrate the release of an account's relationship at a Stone. Removes EXACTLY the
        /// sources derived from qualifying pairs that involved this relationship (contracts §"add/remove
        /// exact ConnectionSourceId records"); removing a Connection's final source drops it into grace.
        /// Idempotent by operationId.</summary>
        public ConnectionSourceEventResult ReleaseRelationship(
            string operationId, WorldId world, ProductScope product, StoneId stoneId,
            AccountId account, string relationshipId, long serverTimeSeconds)
        {
            string binding = BindingDigest(ConnectionSourceEventKind.RelationshipReleased, world, product,
                stoneId, account, relationshipId, 0, serverTimeSeconds);
            var idem = CheckIdempotency(operationId, binding, out var conflict);
            if (conflict != null) return conflict.Value;
            if (idem != null)
                // The participant is already gone after a committed replay; report no live affected set.
                return new ConnectionSourceEventResult(ConnectionSourceOutcome.Replayed, "Replayed",
                    Array.Empty<string>());

            AppendEvent(operationId, binding, ConnectionSourceEventKind.RelationshipReleased, world, product,
                stoneId, account, relationshipId, 0, serverTimeSeconds, string.Empty);

            var affected = ApplyRelease(world, product, stoneId, account, relationshipId, serverTimeSeconds);
            return new ConnectionSourceEventResult(ConnectionSourceOutcome.Applied, "Applied", affected);
        }

        /// <summary>Idempotent grace-expiry reconciliation for a Connection: if it is in grace and the
        /// expiry has passed, reset its accumulated age (spec RD-004). Pure projection update; not itself
        /// a journaled lifecycle event (it is derivable from time, mirroring ConnectionAggregate).</summary>
        public ConnectionAggregate ReconcileGraceExpiry(ConnectionId id, long serverTimeSeconds)
        {
            var current = GetConnection(id);
            var next = current.ReconcileGraceExpiry(serverTimeSeconds);
            if (!ReferenceEquals(next, current))
                _connectionsByKey[id.CanonicalKey] = next;
            return next;
        }

        // ---- Projection mutation (shared by live apply and journal replay) ----

        private List<string> ApplyActivation(WorldId world, ProductScope product, StoneId stoneId,
            AccountId account, string relationshipId, StoneRelationshipRole role, long serverTimeSeconds,
            string activationProvenance)
        {
            var affected = new List<string>();
            var roster = RosterFor(stoneId);
            var newcomer = new StoneParticipant(account, relationshipId, role);

            // Pair the newcomer against every OTHER active participant. A qualifying pair adds the exact
            // derived source to that account-pair Connection (spec RD-002). Same-account participants and
            // Attuned↔Attuned pairs derive nothing.
            foreach (var other in roster)
            {
                var derived = QualifyingSourceRule.DeriveSource(world, product, stoneId,
                    newcomer, other, activationProvenance);
                if (!derived.HasValue) continue;
                AddSourceTo(derived.Value, serverTimeSeconds);
                affected.Add(derived.Value.ConnectionId.CanonicalKey);
            }

            // Register the newcomer as active AFTER pairing, so it never pairs with itself.
            roster.Add(newcomer);
            return affected;
        }

        private List<string> ApplyRelease(WorldId world, ProductScope product, StoneId stoneId,
            AccountId account, string relationshipId, long serverTimeSeconds)
        {
            var affected = new List<string>();
            if (!_rosterByStone.TryGetValue(stoneId.Value, out var roster)) return affected;

            // Find the exact participant being released (matched by account + relationshipId).
            int idx = -1;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].Account.Equals(account)
                    && string.Equals(roster[i].RelationshipId, relationshipId, StringComparison.Ordinal))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) return affected; // never active here — nothing to remove
            var leaving = roster[idx];

            // Remove EXACTLY the sources derived from qualifying pairs that involved this participant.
            // Peer identity is by ROSTER SLOT (skip `idx`), so a leaving relationship is correctly paired
            // against every other active relationship — including a sibling relationship on the same
            // account held under a distinct relationship id.
            for (int i = 0; i < roster.Count; i++)
            {
                if (i == idx) continue;
                var derived = QualifyingSourceRule.DeriveSource(world, product, stoneId,
                    leaving, roster[i], string.Empty);
                if (!derived.HasValue) continue;
                RemoveSourceFrom(derived.Value, serverTimeSeconds);
                affected.Add(derived.Value.ConnectionId.CanonicalKey);
            }

            roster.RemoveAt(idx);
            if (roster.Count == 0) _rosterByStone.Remove(stoneId.Value);
            return affected;
        }

        private void AddSourceTo(DerivedQualifyingSource derived, long serverTimeSeconds)
        {
            string key = derived.ConnectionId.CanonicalKey;
            var current = _connectionsByKey.TryGetValue(key, out var agg)
                ? agg
                : ConnectionAggregate.CreateEmpty(derived.ConnectionId);
            _connectionsByKey[key] = current.AddSource(derived.Source, serverTimeSeconds);
        }

        private void RemoveSourceFrom(DerivedQualifyingSource derived, long serverTimeSeconds)
        {
            string key = derived.ConnectionId.CanonicalKey;
            if (!_connectionsByKey.TryGetValue(key, out var current)) return;
            _connectionsByKey[key] = current.RemoveSource(derived.Source.SourceId, serverTimeSeconds);
        }

        private List<StoneParticipant> RosterFor(StoneId stoneId)
        {
            if (!_rosterByStone.TryGetValue(stoneId.Value, out var list))
            {
                list = new List<StoneParticipant>();
                _rosterByStone[stoneId.Value] = list;
            }
            return list;
        }

        private List<string> AffectedForActivation(WorldId world, ProductScope product, StoneId stoneId,
            AccountId account, string relationshipId, StoneRelationshipRole role)
        {
            // For a replay we recompute the affected set from the CURRENT roster (which already includes
            // the committed newcomer), so callers get a stable list without re-mutating anything.
            var affected = new List<string>();
            if (role == StoneRelationshipRole.None) return affected;
            if (!_rosterByStone.TryGetValue(stoneId.Value, out var roster)) return affected;
            var self = new StoneParticipant(account, relationshipId, role);
            foreach (var other in roster)
            {
                if (other.Account.Equals(account)
                    && string.Equals(other.RelationshipId, relationshipId, StringComparison.Ordinal))
                    continue;
                var derived = QualifyingSourceRule.DeriveSource(world, product, stoneId, self, other, string.Empty);
                if (derived.HasValue) affected.Add(derived.Value.ConnectionId.CanonicalKey);
            }
            return affected;
        }

        // ---- Idempotency ----

        // Returns non-null sentinel when the op is a committed replay; sets `conflict` when the op id was
        // committed with a DIFFERENT binding.
        private string? CheckIdempotency(string operationId, string binding, out ConnectionSourceEventResult? conflict)
        {
            conflict = null;
            if (string.IsNullOrEmpty(operationId)) throw new ArgumentException("operationId required");
            if (_committedOps.TryGetValue(operationId, out var committedBinding))
            {
                if (!string.Equals(committedBinding, binding, StringComparison.Ordinal))
                {
                    conflict = new ConnectionSourceEventResult(ConnectionSourceOutcome.OperationConflict,
                        "OperationConflict", Array.Empty<string>());
                    return null;
                }
                return operationId; // committed replay
            }
            return null;
        }

        // ---- Journal ----

        private void AppendEvent(string operationId, string binding, ConnectionSourceEventKind kind,
            WorldId world, ProductScope product, StoneId stoneId, AccountId account, string relationshipId,
            int role, long serverTimeSeconds, string activationProvenance)
        {
            Append(SerializeEvent(operationId, binding, kind, world, product, stoneId, account,
                relationshipId, role, serverTimeSeconds, activationProvenance));
            _committedOps[operationId] = binding;
        }

        private void RehydrateFromJournal()
        {
            foreach (var line in ReadDurable())
            {
                var ev = ParseEvent(line);
                if (ev == null) continue;
                var e = ev.Value;
                // A committed op replays exactly once (dedupe by op id); its binding is recorded.
                if (_committedOps.ContainsKey(e.OperationId)) continue;
                _committedOps[e.OperationId] = e.Binding;

                var world = new WorldId(e.World);
                var product = new ProductScope(e.Product);
                var stoneId = new StoneId(e.Stone);
                var account = new AccountId(e.Account);
                if (e.Kind == ConnectionSourceEventKind.RelationshipActivated)
                    ApplyActivation(world, product, stoneId, account, e.RelationshipId,
                        (StoneRelationshipRole)e.Role, e.ServerTimeSeconds, e.ActivationProvenance);
                else if (e.Kind == ConnectionSourceEventKind.RelationshipReleased)
                    ApplyRelease(world, product, stoneId, account, e.RelationshipId, e.ServerTimeSeconds);
            }
        }

        private struct ParsedEvent
        {
            public string OperationId;
            public string Binding;
            public ConnectionSourceEventKind Kind;
            public string World;
            public string Product;
            public string Stone;
            public string Account;
            public string RelationshipId;
            public int Role;
            public long ServerTimeSeconds;
            public string ActivationProvenance;
        }

        private static string SerializeEvent(string operationId, string binding, ConnectionSourceEventKind kind,
            WorldId world, ProductScope product, StoneId stoneId, AccountId account, string relationshipId,
            int role, long serverTimeSeconds, string activationProvenance)
        {
            return string.Join("|", new[]
            {
                RecEvent,
                Encode(operationId),
                Encode(binding),
                ((int)kind).ToString(CultureInfo.InvariantCulture),
                Encode(world.Value),
                Encode(product.Value),
                Encode(stoneId.Value),
                Encode(account.Value),
                Encode(relationshipId),
                role.ToString(CultureInfo.InvariantCulture),
                serverTimeSeconds.ToString(CultureInfo.InvariantCulture),
                Encode(activationProvenance ?? string.Empty)
            });
        }

        private static ParsedEvent? ParseEvent(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 12 || parts[0] != RecEvent) return null;
            return new ParsedEvent
            {
                OperationId = Decode(parts[1]),
                Binding = Decode(parts[2]),
                Kind = (ConnectionSourceEventKind)int.Parse(parts[3], CultureInfo.InvariantCulture),
                World = Decode(parts[4]),
                Product = Decode(parts[5]),
                Stone = Decode(parts[6]),
                Account = Decode(parts[7]),
                RelationshipId = Decode(parts[8]),
                Role = int.Parse(parts[9], CultureInfo.InvariantCulture),
                ServerTimeSeconds = long.Parse(parts[10], CultureInfo.InvariantCulture),
                ActivationProvenance = Decode(parts[11])
            };
        }

        private static string BindingDigest(ConnectionSourceEventKind kind, WorldId world, ProductScope product,
            StoneId stoneId, AccountId account, string relationshipId, int role, long serverTimeSeconds)
        {
            return Digest(string.Join("|", new[]
            {
                ((int)kind).ToString(CultureInfo.InvariantCulture),
                world.Value, product.Value, stoneId.Value, account.Value,
                relationshipId ?? string.Empty,
                role.ToString(CultureInfo.InvariantCulture),
                serverTimeSeconds.ToString(CultureInfo.InvariantCulture)
            }));
        }

        // ---- Append-only, framed + crc-checked journal (mirrors OperationReceiptStore) ----

        private void Append(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            using (var fs = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(payload.Length);
                bw.Write(Crc32(payload));
                bw.Write(payload);
                bw.Flush();
                fs.Flush(true); // fsync-equivalent durable boundary
            }
        }

        private List<string> ReadDurable()
        {
            var results = new List<string>();
            if (!File.Exists(_journalPath)) return results;
            using (var fs = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, Encoding.UTF8))
            {
                long length = fs.Length;
                while (true)
                {
                    long recordStart = fs.Position;
                    if (recordStart + 8 > length) break;
                    int payloadLen = br.ReadInt32();
                    uint crc = br.ReadUInt32();
                    if (payloadLen < 0 || fs.Position + payloadLen > length) break;
                    byte[] payload = br.ReadBytes(payloadLen);
                    if (payload.Length != payloadLen || Crc32(payload) != crc) break;
                    results.Add(Encoding.UTF8.GetString(payload));
                }
            }
            return results;
        }

        private static string Encode(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));

        private static string Decode(string s) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(s));

        private static string Digest(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static readonly uint[] _crcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (var b in data)
                crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
