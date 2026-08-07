using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Features.HomesteadStone;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Archer
{
    /// <summary>
    /// T027 — the net48 runtime seam that makes Fletcher's Habit actually recover arrows on a joined client.
    /// Fletcher's Habit is a personal PERMANENT Effect (not a Character Effect like Field Fletching I, not a
    /// Stone-owned Local Effect like Practice Range): once purchased it is OWNED durably — through relationship
    /// loss and revocation (spec line 130 "Permanent Effects remain active"; line 260 "A released character
    /// retains Permanent Effects and Progression Keys"). While owned by the shooter, a fired eligible Wood
    /// Arrow that terminally impacts a recoverable surface has ONE authoritative recovery chance to respawn
    /// the EXACT consumed arrow instance (spec line 161; contracts.md §Archer "ProjectileRecoveryProvider ...
    /// one authoritative terminal-impact decision for one exact consumed eligible arrow; deterministic
    /// Practice Range return suppresses this roll"; research.md line 139).
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game to read/adapt, AGENTS.md / ADR-0001):
    ///   * <c>Projectile.Setup(owner, vel, hitNoise, hitData, item, ammo)</c> (decomp :2811) — where the
    ///     fired projectile learns which ItemData it was loosed from. A postfix here captures, keyed by the
    ///     projectile instance, the exact consumed AMMO provenance (item id, quality, variant, durability,
    ///     crafter, custom data) so a recovered arrow is provably the EXACT consumed instance, plus whether
    ///     the shooter is the local owner. Only Wood Arrow projectiles owned by the local player are tracked.
    ///   * <c>Projectile.OnHit(collider, hitPoint, water, normal)</c> (decomp :2944) — the single terminal
    ///     impact. A postfix classifies the terminal surface, resolves whether the shooter OWNS Fletcher's
    ///     Habit, asks the shipped, unit-tested <see cref="ProjectileRecoveryProvider"/> for the ONE
    ///     authoritative decision, and — on Recovered — drops the exact consumed ItemData ONCE via vanilla
    ///     <c>ItemDrop.DropItem</c> (additive, ADR-0006 — a fresh dropped instance, no prefab clone of a
    ///     ZNetView-bearing base). The once-per-instance / multishot no-duplication guarantee is enforced by
    ///     a per-process <see cref="ProjectileRecoverySession"/> keyed by the projectile's ZDOID.
    ///
    /// TARGET-RETURN EXCLUSION: Practice Range wires the practice/Wood arrow into the vanilla
    /// <c>ArcheryTarget.m_returnAmmo</c> deterministic return (T025). When the terminal surface is that
    /// Archery Target, the deterministic path already returns the arrow; the provider is told
    /// <c>targetReturnWon: true</c> and SUPPRESSES the roll — no double return (spec Edge case).
    ///
    /// AUTHORITY (fail closed, two authoritative paths):
    ///   * On the authoritative HOST the projectile's owner runs OnHit server-side; the composed
    ///     <see cref="LocalProgressionObserver.Server"/> resolves the shooter's purchase (durable ownership)
    ///     straight from the character/authority/Stone stores via
    ///     <see cref="ProjectileRecoveryProvider.OwnsFletchersHabit"/>.
    ///   * On a PURE CLIENT the server runtime is null; ownership is read ONLY from the bounded personal
    ///     read model the server pushed into <see cref="LocalProgressionObserver.PersonalClientCache"/>
    ///     (<see cref="PersonalActivationSnapshot.IsOwned"/>), refetched for the Stone the shooter stands in.
    ///     Absent an owned snapshot the roll never runs (vanilla behaviour). The client authors nothing.
    ///
    /// The RNG is a single trusted <see cref="UnityEngine.Random"/> draw made where the owning code runs, so
    /// exactly one roll resolves one fired instance. References Valheim (Projectile, ItemDrop, Character,
    /// ZNetView) → net48-only, NOT link-compiled into net8. The pure provider + session it drives are fully
    /// unit-tested. Clean-side (ADR-0001): base-game types only.
    /// </summary>
    [HarmonyPatch]
    internal static class ProjectileRecoveryGate
    {
        private static readonly ProjectileRecoveryProvider Provider =
            new ProjectileRecoveryProvider(new Domain.Content.HomesteadProgressionCatalog());

        private static readonly VersionedId FletchersHabitNode = ProjectileRecoveryProvider.FletchersHabitNode;

        // The once-per-fired-instance / multishot no-duplication guard, per process. Keyed by projectile ZDOID.
        private static readonly ProjectileRecoverySession Session = new ProjectileRecoverySession();

        // Per-fired-projectile context captured at Setup, so OnHit can recover the EXACT consumed instance for
        // the correct shooter. ConditionalWeakTable keys on the live Projectile object and lets the GC reclaim
        // the entry when the projectile is destroyed — no leak, no ZDO orphan.
        private sealed class FiredContext
        {
            public ConsumedArrowProvenance Provenance;
            public bool LocalOwner;
            public long InstanceKey;
            public StoneId ShooterStone;
            public bool HaveStone;
            public bool Resolved;
        }

        private static readonly ConditionalWeakTable<Projectile, FiredContext> Contexts =
            new ConditionalWeakTable<Projectile, FiredContext>();

        private const float AreaRadius = 20.0f;
        private const float RefetchIntervalSeconds = 2.0f;
        private static float _lastRequest;
        private static StoneId _lastRequestedStone;
        private static bool _haveLastRequested;
        private static long _syntheticKey;

        /// <summary>Capture fire-time provenance for a Wood Arrow projectile fired by the local player.</summary>
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        [HarmonyPostfix]
        private static void Setup_Postfix(Projectile __instance, Character owner, ItemDrop.ItemData ammo)
        {
            try
            {
                if (__instance == null || ammo == null || ammo.m_shared == null) return;

                // Only the local player's own shots are relevant (the shooter must OWN Fletcher's Habit; the
                // decision + recovery runs where the local/owner code runs). Ignore other characters' shots.
                var localPlayer = Player.m_localPlayer;
                if (localPlayer == null || owner != localPlayer) return;

                // Only the exact eligible arrow (Wood Arrow). Any other ammo is untouched.
                string ammoId = StripCloneSuffix(ammo.m_dropPrefab != null ? ammo.m_dropPrefab.name
                    : (ammo.m_shared.m_name ?? string.Empty));
                if (!string.Equals(ammoId, FletchersHabitContent.EligibleArrowItem, StringComparison.Ordinal))
                    return;

                var ctx = new FiredContext
                {
                    Provenance = CaptureProvenance(ammo, ammoId),
                    LocalOwner = true,
                    InstanceKey = NextInstanceKey(__instance),
                    Resolved = false,
                };
                ResolveShooterStone(localPlayer, ctx);

                Contexts.Remove(__instance);
                Contexts.Add(__instance, ctx);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Archer] Fletcher's Habit Setup postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>The single terminal impact: classify the surface, resolve ownership, ask the provider for
        /// the ONE authoritative decision, and recover the exact consumed instance once on a pass.</summary>
        [HarmonyPatch(typeof(Projectile), "OnHit")]
        [HarmonyPostfix]
        private static void OnHit_Postfix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water)
        {
            try
            {
                if (__instance == null) return;
                if (!Contexts.TryGetValue(__instance, out var ctx) || ctx == null) return;
                if (!ctx.LocalOwner || ctx.Resolved) return;
                ctx.Resolved = true; // guard against a re-entrant OnHit for this same projectile object.

                bool owned = ResolveOwnedForShooter(ctx);
                if (!owned) return; // vanilla behaviour — never fire the roll or drop anything.

                var surface = ClassifySurface(collider, water, out bool targetReturnWon);

                double roll = UnityEngine.Random.value; // one trusted draw, half-open [0,1).
                var decision = Session.ResolveOnce(Provider, ctx.InstanceKey, owned, ctx.Provenance,
                    surface, targetReturnWon, roll);

                if (decision.Outcome == RecoveryOutcome.Recovered && decision.Recovered)
                    RespawnExactArrow(ctx.Provenance, hitPoint);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Archer] Fletcher's Habit OnHit postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>Map a vanilla terminal impact to the recovery-relevant surface. Water and a null collider
        /// (miss / TTL / lost) are non-recoverable; a hit collider carrying a <c>Character</c> is a creature;
        /// a collider on the Archery Target owns the deterministic-return path (sets targetReturnWon so the
        /// roll is suppressed); everything else solid is a recoverable structure/ground.</summary>
        private static RecoverySurface ClassifySurface(Collider collider, bool water, out bool targetReturnWon)
        {
            targetReturnWon = false;
            if (water) return RecoverySurface.Water;
            if (collider == null) return RecoverySurface.LostOrExpired;

            // The Archery Target's vanilla deterministic return (Practice Range T025) owns this arrow: it is
            // wired into ArcheryTarget.m_returnAmmo and returns it exactly once. Suppress the Fletcher's Habit
            // roll (spec Edge case). Detected by the ArcheryTarget component in the hit hierarchy.
            if (collider.GetComponentInParent<ArcheryTarget>() != null)
            {
                targetReturnWon = true;
                return RecoverySurface.ArcheryTarget;
            }

            if (collider.GetComponentInParent<Character>() != null)
                return RecoverySurface.Creature;

            return RecoverySurface.SolidStructure;
        }

        private static bool ResolveOwnedForShooter(FiredContext ctx)
        {
            var server = LocalProgressionObserver.Server;
            if (server != null)
                return ResolveHostOwned(ctx);

            // Pure client: durable ownership from the server-stamped personal snapshot (Purchased bit),
            // relationship-independent. Fail closed absent an owned snapshot for the shooter's Stone.
            if (!ctx.HaveStone) return false;
            MaybeRequestSnapshot(ctx.ShooterStone);
            return LocalProgressionObserver.PersonalClientCache.IsOwnedForStone(ctx.ShooterStone, FletchersHabitNode);
        }

        private static bool ResolveHostOwned(FiredContext ctx)
        {
            var server = LocalProgressionObserver.Server;
            var foundational = FoundationalPlacementObserver.Server;
            var player = Player.m_localPlayer;
            if (server == null || foundational == null || player == null) return false;
            if (!ctx.HaveStone) return false;

            long actingPlayerId = player.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) || !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            var occupant = principal.Account;
            var character = principal.Character;
            if (string.IsNullOrEmpty(occupant.Value)) return false;

            var stone = server.Stones.GetStone(ctx.ShooterStone);
            var characterAgg = server.Characters.GetCharacter(occupant, character);
            var authority = server.Authority.GetAuthority(occupant, ctx.ShooterStone);
            if (stone == null || characterAgg == null || authority == null) return false;

            // Durable Permanent-Effect ownership: purchase + developed, relationship-independent.
            return Provider.OwnsFletchersHabit(stone, characterAgg, authority);
        }

        /// <summary>Respawn the EXACT consumed arrow instance ONCE at the impact point. Additive (ADR-0006):
        /// a fresh dropped ItemDrop instance built from the vanilla arrow prefab via <c>ItemDrop.DropItem</c>,
        /// stamped with the captured provenance — never a clone of a live ZNetView-bearing projectile.</summary>
        private static void RespawnExactArrow(ConsumedArrowProvenance provenance, Vector3 hitPoint)
        {
            var odb = ObjectDB.instance;
            var prefab = odb != null ? odb.GetItemPrefab(provenance.ItemId) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null || drop.m_itemData == null)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/Archer] Fletcher's Habit could not resolve '{provenance.ItemId}' prefab to recover; skipping.");
                return;
            }

            // A fresh instance carrying the exact consumed provenance (not the shared prefab item data).
            var recovered = drop.m_itemData.Clone();
            recovered.m_stack = 1;
            recovered.m_quality = provenance.Quality;
            recovered.m_variant = provenance.Variant;
            recovered.m_durability = (float)provenance.Durability;
            recovered.m_crafterID = provenance.CrafterId;
            recovered.m_crafterName = provenance.CrafterName;
            if (!string.IsNullOrEmpty(provenance.CustomData))
                ApplyCustomData(recovered, provenance.CustomData);

            Vector3 dropPos = hitPoint + Vector3.up * 0.2f;
            ItemDrop.DropItem(recovered, 1, dropPos, Quaternion.identity);
            Plugin.Log.LogInfo(
                $"[Niflheim/Archer] Fletcher's Habit recovered one exact '{provenance.ItemId}' at terminal impact.");
        }

        private static ConsumedArrowProvenance CaptureProvenance(ItemDrop.ItemData ammo, string ammoId)
        {
            string customData = SerializeCustomData(ammo);
            return new ConsumedArrowProvenance(
                itemId: ammoId,
                quality: ammo.m_quality,
                variant: ammo.m_variant,
                durability: ammo.m_durability,
                crafterId: ammo.m_crafterID,
                crafterName: ammo.m_crafterName ?? string.Empty,
                customData: customData);
        }

        private static string SerializeCustomData(ItemDrop.ItemData ammo)
        {
            if (ammo.m_customData == null || ammo.m_customData.Count == 0) return string.Empty;
            var parts = new List<string>(ammo.m_customData.Count);
            foreach (var kv in ammo.m_customData)
                parts.Add(kv.Key + "=" + kv.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join("\u001f", parts.ToArray());
        }

        private static void ApplyCustomData(ItemDrop.ItemData item, string serialized)
        {
            if (item.m_customData == null)
                item.m_customData = new Dictionary<string, string>();
            item.m_customData.Clear();
            foreach (var pair in serialized.Split('\u001f'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                item.m_customData[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }
        }

        private static void ResolveShooterStone(Player player, FiredContext ctx)
        {
            var znet = ZNet.instance;
            if (player == null || znet == null) return;

            // On the host, resolve the authoritative Stone Area at the shooter's position; on a pure client,
            // fall back to the client-visible resident Stone index (same convenience the recipe gate uses).
            var foundational = FoundationalPlacementObserver.Server;
            Vector3 pp = player.transform.position;
            if (foundational != null && foundational.StoneAreas.TryResolve(pp.x, pp.z, out var hostStone))
            {
                ctx.ShooterStone = hostStone;
                ctx.HaveStone = true;
                return;
            }

            var world = new WorldId(HomesteadWorldIdentity.FromUid(znet.GetWorldUID()));
            var stones = HomesteadStoneClientIndex.ResidentStones();
            StoneId? best = null;
            float bestSq = AreaRadius * AreaRadius;
            foreach (var s in stones)
            {
                float dx = pp.x - s.X, dz = pp.z - s.Z;
                float sq = (dx * dx) + (dz * dz);
                if (sq > AreaRadius * AreaRadius) continue;
                if (best == null || sq < bestSq)
                {
                    bestSq = sq;
                    best = StoneId.FromHostZone(world, s.ZoneX, s.ZoneZ);
                }
            }
            if (best != null)
            {
                ctx.ShooterStone = best.Value;
                ctx.HaveStone = true;
            }
        }

        private static void MaybeRequestSnapshot(StoneId stoneId)
        {
            float now = Time.realtimeSinceStartup;
            bool stoneChanged = !_haveLastRequested || !_lastRequestedStone.Equals(stoneId);
            if (!stoneChanged && (now - _lastRequest) < RefetchIntervalSeconds) return;
            _lastRequest = now;
            _lastRequestedStone = stoneId;
            _haveLastRequested = true;
            PersonalActivationDeliveryObserver.RequestSnapshot(stoneId);
        }

        /// <summary>A stable per-fired-instance key for the no-duplication session. Prefer the projectile's
        /// networked ZDOID (identical across owner/RPC observations); fall back to a monotonic synthetic key
        /// when the ZDO is not yet valid.</summary>
        private static long NextInstanceKey(Projectile projectile)
        {
            var nview = projectile != null ? projectile.GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid())
            {
                var uid = nview.GetZDO().m_uid;
                // Combine the ZDOID's userID + id into a single stable long.
                return ((long)uid.UserID << 32) ^ (uint)uid.ID;
            }
            return --_syntheticKey; // negative, unique, monotonic — never collides with a real ZDOID hash.
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
