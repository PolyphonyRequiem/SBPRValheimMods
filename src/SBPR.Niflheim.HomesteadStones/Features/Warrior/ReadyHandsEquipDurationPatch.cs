using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Features.HomesteadStone;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Warrior
{
    /// <summary>
    /// T030 — the net48 runtime seam that makes Ready Hands actually shorten queued weapon swaps on a
    /// joined client. Ready Hands is a PERSONAL Character Effect (like Field Fletching I, not a Stone-owned
    /// Local Node): while active for the acting occupant it SHORTENS the copied queued equip AND unequip
    /// durations for eligible MELEE weapons only (spec §"Warrior"; contracts.md §Warrior
    /// "EquipDurationProvider: Ready Hands modifies copied queued equip and unequip durations for authored
    /// eligible melee weapons only; no shared prefab mutation"). It authors and mutates NOTHING on the
    /// shared item prefab — it scales ONLY the fresh per-action copy the vanilla queue just created,
    /// consuming the shipped, unit-tested <see cref="EquipDurationProvider"/> as its single authority.
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game to read/adapt, AGENTS.md / ADR-0001):
    ///   * <c>Humanoid.QueueEquipAction(ItemData)</c> (decomp assembly_valheim :22237) and
    ///     <c>Humanoid.QueueUnequipAction(ItemData)</c> (:22262). Each builds a fresh
    ///     <c>MinorActionData</c> whose <c>m_duration</c> is COPIED from
    ///     <c>item.m_shared.m_equipDuration</c> (:22252 / :22275) and appends it to the private
    ///     <c>m_actionQueue</c>. The queue ticks that per-action COPY (UpdateActionQueue :22211), never the
    ///     shared field. A postfix reads the just-appended action and, when Ready Hands is active for the
    ///     local occupant and the item is an eligible melee weapon, scales its <c>m_duration</c> by the
    ///     provider's factor. Reload is a THIRD action type built from <c>GetWeaponLoadingTime()</c>
    ///     (:22292), never from <c>m_equipDuration</c>, and is never queued through these two methods — so
    ///     it is structurally outside this patch (AT-READY-HANDS-EXCLUSIONS: reload).
    ///
    /// WHY NO SHARED-PREFAB MUTATION: we only ever write <c>MinorActionData.m_duration</c> — a value that
    /// vanilla already COPIED off the shared prefab into a throwaway per-action struct. The shared
    /// <c>ItemData.m_shared.m_equipDuration</c> is never touched, so every other item sharing the prefab,
    /// and the same item after the effect ends, keep the unchanged vanilla duration.
    ///
    /// SINGLE AUTHORITY: the shorten/skip decision routes through <see cref="EquipDurationProvider"/> — the
    /// same pure policy the domain tests pin. This patch is a THIN adapter: it maps the item's authored
    /// <c>SkillType</c> onto the engine-free <see cref="WeaponSkillClass"/>, resolves the Ready Hands active
    /// bit authoritatively, and applies the provider's resolved duration to the queued copy.
    ///
    /// ACTIVATION SOURCE (fail closed, two authoritative paths — mirrors FieldFletchingRecipeGate):
    ///   * HOST: resolve the acting occupant's purchase + active relationship from the composed
    ///     <see cref="LocalProgressionServer"/> stores and derive the Ready Hands active bit through the
    ///     shipped DerivedActivationView.
    ///   * PURE CLIENT: read ONLY the bounded personal read model the server pushed into
    ///     <see cref="LocalProgressionObserver.PersonalClientCache"/>, refetched on a bounded interval for
    ///     the Stone the local player stands in. No active snapshot ⇒ full vanilla duration (fail closed).
    ///
    /// References Valheim (Player, Humanoid, ItemDrop, ZNet) → net48-only, NOT link-compiled into net8.
    /// The pure provider it drives is fully unit-tested. Clean-side (ADR-0001): base-game types only.
    /// </summary>
    [HarmonyPatch]
    internal static class ReadyHandsEquipDurationPatch
    {
        private static readonly EquipDurationProvider Provider = new EquipDurationProvider();

        private static readonly VersionedId ReadyHandsNode = WarriorNodes.ReadyHands;

        private const float AreaRadius = 20.0f;

        private const float RefetchIntervalSeconds = 2.0f;
        private static float _lastRequest;
        private static StoneId _lastRequestedStone;
        private static bool _haveLastRequested;

        // ── Both halves: postfix the two queue methods and scale the just-appended action's copy ─────

        [HarmonyPatch(typeof(Humanoid), "QueueEquipAction")]
        [HarmonyPostfix]
        private static void QueueEquipAction_Postfix(Humanoid __instance, ItemDrop.ItemData item)
            => ScaleLastQueuedAction(__instance, item, QueuedEquipAction.Equip);

        [HarmonyPatch(typeof(Humanoid), "QueueUnequipAction")]
        [HarmonyPostfix]
        private static void QueueUnequipAction_Postfix(Humanoid __instance, ItemDrop.ItemData item)
            => ScaleLastQueuedAction(__instance, item, QueuedEquipAction.Unequip);

        /// <summary>Scale the duration of the action the vanilla queue method just appended (the last entry
        /// in <c>m_actionQueue</c> matching this item + action), when Ready Hands is active for the LOCAL
        /// occupant and the item is an eligible melee weapon. Only the per-action COPY is written; the
        /// shared prefab is untouched. Local player only, and only if the queue actually grew (vanilla
        /// toggles a re-queued action off — decomp :22243 — leaving nothing of ours to scale).</summary>
        private static void ScaleLastQueuedAction(Humanoid instance, ItemDrop.ItemData item, QueuedEquipAction action)
        {
            try
            {
                if (instance == null || item == null) return;
                if (instance != Player.m_localPlayer) return;      // client decision, local player only.

                var shared = item.m_shared;
                if (shared == null) return;
                if (shared.m_equipDuration <= 0f) return;          // vanilla never queued an action for this.

                var skillClass = MapSkill(shared.m_skillType);
                if (!EquipDurationProvider.IsEligibleMeleeSkill(skillClass)) return; // exclusions: no touch.

                bool active = ResolveActiveForLocalOccupant();
                if (!active) return;                               // dormant/unpurchased ⇒ full vanilla duration.

                // Locate the action the vanilla method just appended for THIS item + action type.
                var queue = Traverse.Create(instance).Field("m_actionQueue").GetValue<System.Collections.IList>();
                if (queue == null || queue.Count == 0) return;

                object? target = null;
                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    var entry = queue[i];
                    if (entry == null) continue;
                    var entryItem = Traverse.Create(entry).Field("m_item").GetValue<ItemDrop.ItemData>();
                    if (!ReferenceEquals(entryItem, item)) continue;
                    var typeVal = Traverse.Create(entry).Field("m_type").GetValue();
                    if (!MatchesAction(typeVal, action)) continue; // never a reload entry for this item.
                    target = entry;
                    break;
                }
                if (target == null) return;

                var durTrav = Traverse.Create(target).Field("m_duration");
                float baseDuration = durTrav.GetValue<float>();
                if (baseDuration <= 0f) return;

                var decision = Provider.ResolveDuration(active, skillClass, action, baseDuration);
                if (decision.Shortened)
                    durTrav.SetValue((float)decision.Duration);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Warrior] Ready Hands equip-duration postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>Map Valheim's <c>Skills.SkillType</c> onto the engine-free <see cref="WeaponSkillClass"/>.
        /// The numeric values match the vanilla enum (decomp :23820), but the map is explicit so a
        /// future/unknown skill falls to <see cref="WeaponSkillClass.None"/> (never eligible) — the correct
        /// fail-safe.</summary>
        private static WeaponSkillClass MapSkill(Skills.SkillType skill)
        {
            switch (skill)
            {
                case Skills.SkillType.Swords: return WeaponSkillClass.Swords;
                case Skills.SkillType.Knives: return WeaponSkillClass.Knives;
                case Skills.SkillType.Clubs: return WeaponSkillClass.Clubs;
                case Skills.SkillType.Polearms: return WeaponSkillClass.Polearms;
                case Skills.SkillType.Spears: return WeaponSkillClass.Spears;
                case Skills.SkillType.Axes: return WeaponSkillClass.Axes;
                case Skills.SkillType.Blocking: return WeaponSkillClass.Blocking;
                case Skills.SkillType.Bows: return WeaponSkillClass.Bows;
                case Skills.SkillType.Crossbows: return WeaponSkillClass.Crossbows;
                case Skills.SkillType.ElementalMagic: return WeaponSkillClass.ElementalMagic;
                case Skills.SkillType.BloodMagic: return WeaponSkillClass.BloodMagic;
                case Skills.SkillType.Unarmed: return WeaponSkillClass.Unarmed;
                case Skills.SkillType.Pickaxes: return WeaponSkillClass.Pickaxes;
                case Skills.SkillType.WoodCutting: return WeaponSkillClass.WoodCutting;
                default: return WeaponSkillClass.None;
            }
        }

        private static bool MatchesAction(object? minorActionType, QueuedEquipAction action)
        {
            // MinorActionData.ActionType: Equip=0, Unequip=1, Reload=2 (decomp :15343).
            if (minorActionType == null) return false;
            int val = Convert.ToInt32(minorActionType);
            switch (action)
            {
                case QueuedEquipAction.Equip: return val == 0;
                case QueuedEquipAction.Unequip: return val == 1;
                default: return false;
            }
        }

        // ── Activation resolution (mirrors FieldFletchingRecipeGate) ─────────────────────────────────

        /// <summary>Resolve whether Ready Hands is currently ACTIVE for the LOCAL occupant. HOST: derive the
        /// acting occupant's purchase + active relationship from the composed server stores. PURE CLIENT:
        /// read ONLY the server-stamped personal snapshot from the bounded client cache, refetched on a
        /// bounded interval for the Stone the local player stands in. No server runtime and no held active
        /// snapshot ⇒ false (full vanilla duration). No client-supplied claim is ever trusted.</summary>
        private static bool ResolveActiveForLocalOccupant()
        {
            var server = LocalProgressionObserver.Server;
            if (server != null)
                return ResolveHostActive(server);

            var stoneId = ResolveLocalStone();
            if (stoneId == null) return false;

            MaybeRequestSnapshot(stoneId.Value);
            return LocalProgressionObserver.PersonalClientCache.IsActiveForStone(stoneId.Value, ReadyHandsNode);
        }

        private static bool ResolveHostActive(LocalProgressionServer server)
        {
            var foundational = FoundationalPlacementObserver.Server;
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (foundational == null || player == null || znet == null) return false;

            Vector3 pp = player.transform.position;
            if (!foundational.StoneAreas.TryResolve(pp.x, pp.z, out var stoneId))
                return false;

            long actingPlayerId = player.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            var occupant = principal.Account;
            var character = principal.Character;
            if (string.IsNullOrEmpty(occupant.Value)) return false;

            var stone = server.Stones.GetStone(stoneId);
            var characterAgg = server.Characters.GetCharacter(occupant, character);
            var authority = server.Authority.GetAuthority(occupant, stoneId);
            if (stone == null || characterAgg == null || authority == null) return false;

            // Single authority: the shipped, unit-tested DerivedActivationView. Active == Ready Hands
            // purchased AND active relationship. No re-derivation here.
            var view = Domain.Activation.DerivedActivationView.Derive(stone, characterAgg, authority);
            return Provider.IsActive(view);
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

        private static StoneId? ResolveLocalStone()
        {
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (player == null || znet == null) return null;

            var world = new WorldId(HomesteadWorldIdentity.FromUid(znet.GetWorldUID()));
            Vector3 pp = player.transform.position;

            var stones = HomesteadStoneClientIndex.ResidentStones();
            if (stones.Count == 0) return null;

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
            return best;
        }
    }
}
