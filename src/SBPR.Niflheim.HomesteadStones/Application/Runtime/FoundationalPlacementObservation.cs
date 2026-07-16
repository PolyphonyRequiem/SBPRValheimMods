using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009 — the engine-free representation of ONE server-observed live placement event, the seam
    // between the net48 Valheim observer (Features/Progression/FoundationalPlacementObserver.cs) and
    // the shipped Foundational AP domain pipeline. Every field here is a TRUSTED, server-attributed
    // fact: the engine-bound observer derives the acting account/character from the authenticated
    // connection/server context (never the client payload), resolves the placed prefab's stable
    // catalog identity, the current Stone Area membership, the placement success state, the catalog
    // version, and a stable physical-instance repetition key — then hands this struct across.
    //
    // Keeping the observation engine-free is what lets FoundationalPlacementRuntime (and every field
    // derivation helper) be exercised by the net8 test project exactly like the rest of the slice,
    // while the shipping net48 mod feeds it real Valheim facts. There is NO client-authoritative field
    // and no claim: the server is the sole author of every value here.
    //
    // net48 audit: System + SHA256 + value objects only. No net5+ surface, no UnityEngine/Valheim.
    public readonly struct FoundationalPlacementObservation
    {
        public FoundationalPlacementObservation(
            StoneId stoneId,
            string actingPlatformId,
            string actingCharacterId,
            string stablePieceId,
            string pieceInstanceProvenance,
            bool insideStoneArea,
            bool placementSucceeded,
            string foundationalCatalogVersion)
        {
            StoneId = stoneId;
            ActingPlatformId = actingPlatformId ?? string.Empty;
            ActingCharacterId = actingCharacterId ?? string.Empty;
            StablePieceId = stablePieceId ?? string.Empty;
            PieceInstanceProvenance = pieceInstanceProvenance ?? string.Empty;
            InsideStoneArea = insideStoneArea;
            PlacementSucceeded = placementSucceeded;
            FoundationalCatalogVersion = foundationalCatalogVersion ?? string.Empty;
        }

        /// <summary>The Stone whose Area the placement occurred in (server-derived from world facts).</summary>
        public StoneId StoneId { get; }

        /// <summary>Authenticated platform id of the acting connection (server context, never payload).</summary>
        public string ActingPlatformId { get; }

        /// <summary>Acting character observed at command time (server-attributed peer character).</summary>
        public string ActingCharacterId { get; }

        /// <summary>Stable Foundational-catalog piece id resolved from the placed prefab identity.</summary>
        public string StablePieceId { get; }

        /// <summary>Stable id of the PHYSICAL placed instance (e.g. its durable ZDOID string), so the
        /// same physical piece is credited at most once across re-observation, retry, and restart.</summary>
        public string PieceInstanceProvenance { get; }

        public bool InsideStoneArea { get; }
        public bool PlacementSucceeded { get; }
        public string FoundationalCatalogVersion { get; }

        /// <summary>Deterministic operation id for this physical placement event. Derived from the
        /// Stone + physical-instance provenance so re-observing the SAME placed piece (reconnect,
        /// restart, duplicate event) converges on the one recorded receipt (a pure replay), while a
        /// genuinely distinct physical instance earns its own operation. Falls back to a per-actor
        /// piece-id key only when no physical provenance is available (never client-supplied).</summary>
        public OperationId DeriveOperationId()
        {
            string material = string.IsNullOrEmpty(PieceInstanceProvenance)
                ? string.Join("|", new[] { "foundational-live", StoneId.Value, ActingPlatformId, ActingCharacterId, StablePieceId })
                : string.Join("|", new[] { "foundational-live", StoneId.Value, PieceInstanceProvenance });
            return new OperationId("op-fnd-" + ShortDigest(material));
        }

        private static string ShortDigest(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? string.Empty));
                var sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
