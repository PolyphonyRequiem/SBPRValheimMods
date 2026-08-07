using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // ADO #138 — the SERVER-CHECKED proximity authority for the proximate relationship acts.
    //
    // The hole this closes: RelationshipCommandHandler had NO position check at all. Forming a Bond or
    // requesting an Attunement is, by decision, an act performed AT the Stone — but the server took the
    // caller's word for it. Whether the acting character was standing at the Stone was only ever checked
    // (if at all) by the net48 caller in front of the handler, which is exactly the shape ADO #137 closed
    // for the refund primitive: an authority that lives in a caller is not an authority.
    //
    // The rule (card title, already decided): CreateBond and CreateAttunement require the acting
    // character to actually be inside the target Stone's Area, and the SERVER decides that — never the
    // client's claim. ReleaseRelationship is NOT gated: releasing is not the proximate act, and gating it
    // would strand a character who released away from the Stone. Other progression selections stay
    // explicitly non-proximate (spec scenario 7 / SC-008 / T035 remote-shaped commands) — this gate is
    // deliberately narrow.
    //
    // There is NO second position source. The one server-owned position fact is the acting character's
    // own server-side ZDO transform — the same fact the placement pipeline, the activation delivery
    // channel, and RelationshipProvisioningAdmin already read — and the one server-owned Area fact is
    // StoneAreaMembership, populated by StoneAreaRegistrar from resident Stone ZDOs. This file only
    // THREADS those two into the handler so the check happens at the authority instead of in front of it.
    //
    // Fail closed: an unknown character position, an unregistered Stone Area, or a position outside the
    // target Stone's radius all deny. An empty membership denies everything, exactly as it does for
    // placement (OutsideStoneArea).
    //
    // net48 audit: System + collections + engine-free value objects only. No UnityEngine/Valheim/BepInEx,
    // so every branch link-compiles into the net8 test project.

    /// <summary>Server-owned authority answering "is this acting character actually at that Stone right
    /// now?". Consulted by <c>RelationshipCommandHandler</c> before the proximate relationship acts.
    /// Implementations MUST derive the answer from server state only.</summary>
    public interface IStoneProximityAuthority
    {
        bool IsAtStone(AuthoritativePrincipal principal, StoneId stoneId);
    }

    /// <summary>Server-observed world positions of acting characters, keyed by the bound internal
    /// <see cref="CharacterId"/>. The engine-bound layer publishes the position it read off the acting
    /// peer's own character ZDO — never a client payload. A character with no published observation is
    /// unknown and fails closed.</summary>
    public interface IServerObservedCharacterPositions
    {
        bool TryGetPosition(CharacterId character, out double x, out double z);
    }

    /// <summary>Process-local index of server-observed character positions. Deliberately non-durable
    /// (mirrors <see cref="BoundSessionPrincipalIndex"/>): a restart clears it and the next server-side
    /// observation republishes. Nothing here is authority on its own — it is a relay of the server's ZDO
    /// reading to the command authority that must enforce on it.</summary>
    public sealed class ServerObservedCharacterPositions : IServerObservedCharacterPositions
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, KeyValuePair<double, double>> _positions =
            new Dictionary<string, KeyValuePair<double, double>>(StringComparer.Ordinal);

        /// <summary>Publish (or refresh) the SERVER-READ world position of one acting character. An empty
        /// character id is ignored — the index never holds an unbound identity.</summary>
        public void Publish(CharacterId character, double x, double z)
        {
            if (string.IsNullOrEmpty(character.Value)) return;
            lock (_gate) { _positions[character.Value] = new KeyValuePair<double, double>(x, z); }
        }

        /// <summary>Drop a character's observation (peer disconnect / operator close). Idempotent.</summary>
        public void Clear(CharacterId character)
        {
            if (string.IsNullOrEmpty(character.Value)) return;
            lock (_gate) { _positions.Remove(character.Value); }
        }

        public bool TryGetPosition(CharacterId character, out double x, out double z)
        {
            x = 0.0; z = 0.0;
            if (string.IsNullOrEmpty(character.Value)) return false;
            lock (_gate)
            {
                if (!_positions.TryGetValue(character.Value, out var p)) return false;
                x = p.Key; z = p.Value;
                return true;
            }
        }

        /// <summary>Live observation count (test/operator visibility).</summary>
        public int ObservedCount { get { lock (_gate) { return _positions.Count; } } }
    }

    /// <summary>The production proximity authority: the acting character's server-observed position must
    /// fall inside the TARGET Stone's registered Area (not merely inside some Area — a character standing
    /// at Stone B cannot bond to Stone A). Composed over the SAME <see cref="StoneAreaMembership"/> the
    /// placement pipeline uses.</summary>
    public sealed class StoneAreaProximityAuthority : IStoneProximityAuthority
    {
        private readonly StoneAreaMembership _areas;
        private readonly IServerObservedCharacterPositions _positions;

        public StoneAreaProximityAuthority(StoneAreaMembership areas, IServerObservedCharacterPositions positions)
        {
            _areas = areas ?? throw new ArgumentNullException(nameof(areas));
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        }

        public bool IsAtStone(AuthoritativePrincipal principal, StoneId stoneId)
        {
            if (string.IsNullOrEmpty(stoneId.Value)) return false;
            if (!_positions.TryGetPosition(principal.Character, out double x, out double z)) return false;
            return _areas.IsInside(stoneId, x, z);
        }
    }

    /// <summary>Explicit deny-everything authority. Named so a composition that has no server position
    /// authority available is a VISIBLE closed door rather than an accidental open one. Not a fallback:
    /// a caller must choose it deliberately.</summary>
    public sealed class DenyAllStoneProximityAuthority : IStoneProximityAuthority
    {
        public static readonly DenyAllStoneProximityAuthority Instance = new DenyAllStoneProximityAuthority();
        public bool IsAtStone(AuthoritativePrincipal principal, StoneId stoneId) => false;
    }
}
