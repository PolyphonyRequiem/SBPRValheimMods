using System;
using UnityEngine;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;

namespace SBPR.Niflheim.HomesteadStones.Features.Archer
{
    /// <summary>
    /// T025-RT — the engine-bound Archer / Practice Range content registrar. Turns the shipped,
    /// engine-free authored values in <see cref="PracticeRangeContent"/> into live runtime content:
    ///
    ///   1. The Practice Arrow item (<c>ArrowPractice</c>) — an additive Ammo ItemDrop (ADR-0006) whose
    ///      damages are all ZERO, so a fired practice arrow contributes 0 ammo damage while the bow's own
    ///      draw damage is fully retained. This is DATA-DRIVEN, not a patch: vanilla
    ///      <c>Attack.FireProjectileBurst</c> combines shot damage as
    ///      <c>hitData.m_damage.Add(m_weapon.GetDamage())</c> then <c>hitData.m_damage.Add(ammoItem.GetDamage())</c>
    ///      (decompiled assembly_valheim ~line 1726/1740); a zero-damage ammo item therefore adds nothing,
    ///      exactly matching <see cref="PracticeRangeProvider.ResolvePracticeArrowDamage"/>.
    ///   2. The Practice Arrow recipe — exactly 100 arrows for 8 Wood
    ///      (<see cref="PracticeRangeContent.PracticeArrowRecipe"/>), hand-craftable (no station).
    ///   3. Deterministic target return — the practice arrow is appended to the vanilla
    ///      <c>piece_ArcheryTarget</c>'s <c>ArcheryTarget.m_returnAmmo</c> list, so an arrow that
    ///      terminally impacts the target is returned exactly once by the vanilla path
    ///      (<c>ArcheryTarget.DropArrows()</c>, ~line 98832) — no roll — which is precisely the hook a
    ///      later Fletcher's Habit roll (T027) yields to, matching
    ///      <see cref="PracticeRangeProvider.ResolveTargetReturn"/>.
    ///
    /// The exact vanilla Archery Target build piece (<see cref="PracticeRangeContent.ArcheryTargetPrefab"/>
    /// = <c>piece_ArcheryTarget</c>) is added to the Hammer build table so it is buildable at all; the
    /// per-attempt capability AND (active Local Effect AND ordinary build Permission) is enforced by
    /// <see cref="ArcheryTargetPlacementGate"/>.
    ///
    /// net48-only (UnityEngine/Valheim) — not link-compiled into the net8 test suite. All values it emits
    /// trace to the unit-tested <see cref="PracticeRangeContent"/>.
    /// </summary>
    internal static class ArcherContent
    {
        // The vanilla arrow blueprint we READ (never clone) for the ammo type, fire projectile, and
        // arrow visual. ArrowWood is the earliest, always-present vanilla arrow.
        private const string ArrowBlueprint = "ArrowWood";

        // Bow ammo family. Vanilla bows expose m_ammoType "$ammo_arrows"; a practice arrow must share it
        // to be nockable. We READ it off the ArrowWood blueprint at runtime rather than hardcode, so a
        // future game patch that renames the family can't silently drift us.
        private static bool contentBuilt;

        /// <summary>ZNetScene phase — construct + register the Practice Arrow item prefab. Idempotent.</summary>
        internal static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns == null) return;
            if (zns.GetPrefab(PracticeRangeContent.PracticeArrowItem) != null) return;

            var blueprint = zns.GetPrefab(ArrowBlueprint);
            if (blueprint == null)
            {
                Plugin.Log.LogError(
                    $"[Niflheim/Archer] Arrow blueprint '{ArrowBlueprint}' not in ZNetScene; cannot build "
                    + $"{PracticeRangeContent.PracticeArrowItem}. Practice Arrow will be absent this boot.");
                return;
            }

            var go = ArcherContentAssets.NewHolderObject(PracticeRangeContent.PracticeArrowItem);

            // Networked, persistent dropped-item identity (mirrors vanilla arrow: ZNetView + ZSyncTransform
            // + Rigidbody + ItemDrop). Additive — we add only these, never Instantiate the arrow root.
            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            nview.m_distant = false;

            var zsync = go.AddComponent<ZSyncTransform>();
            zsync.m_syncPosition = true;
            zsync.m_syncRotation = true;
            zsync.m_syncScale = false;

            var body = go.AddComponent<Rigidbody>();
            body.maxDepenetrationVelocity = 1f;

            int itemLayer = LayerMask.NameToLayer("item");
            if (itemLayer >= 0) go.layer = itemLayer;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.2f, 0.2f, 0.5f);

            // Cosmetic arrow mesh grafted (reference, not clone) from the blueprint so it reads as an arrow.
            ArcherContentAssets.GraftMeshFromBlueprint(blueprint, go, "PracticeArrowVisual");

            var drop = go.AddComponent<ItemDrop>();
            drop.m_itemData = new ItemDrop.ItemData
            {
                m_stack = 1,
                m_quality = 1,
                m_shared = new ItemDrop.ItemData.SharedData(),
            };
            var shared = drop.m_itemData.m_shared;
            shared.m_name = "Practice Arrow";
            shared.m_description =
                "A blunt-tipped training arrow. It flies true off the bow but carries no bite of its own — "
                + "loosed at a Practice Range target it thuds home and is yours to draw again.";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Ammo;
            shared.m_maxStackSize = 100;
            shared.m_weight = 0.1f;
            shared.m_maxQuality = 1;
            shared.m_teleportable = true;
            shared.m_icons = new[] { ArcherContentAssets.FallbackIcon };

            // READ the blueprint's ammo family + fire attack so the practice arrow nocks and fires exactly
            // like a wood arrow — but with its OWN zero-damage profile.
            var blueprintDrop = blueprint.GetComponent<ItemDrop>();
            var blueprintShared = blueprintDrop?.m_itemData?.m_shared;
            shared.m_ammoType = blueprintShared?.m_ammoType ?? "$ammo_arrows";

            // Fire attack: copy the blueprint's projectile-attack reference so the shot spawns a real
            // arrow projectile. A NEW Attack with the same m_attackProjectile reference is reference-not-
            // clone (ADR-0006): we point at the vanilla projectile prefab, we do not Instantiate it here.
            shared.m_attack = new Attack();
            if (blueprintShared?.m_attack != null)
                shared.m_attack.m_attackProjectile = blueprintShared.m_attack.m_attackProjectile;
            shared.m_secondaryAttack = new Attack();

            // THE damage decision (spec line 159 / AT-PRACTICE-ARROW-DAMAGE): a FRESH DamageTypes is all
            // zeros. GetDamage() returns m_damages, so the ammo contributes 0 to the combined shot while
            // the bow's own draw damage is retained. Explicit for clarity + to defeat any blueprint bleed.
            shared.m_damages = new HitData.DamageTypes();

            ArcherContentAssets.RegisterPrefabInZNetScene(zns, go);
            Plugin.Log.LogInfo(
                $"[Niflheim/Archer] Registered Practice Arrow item '{PracticeRangeContent.PracticeArrowItem}' "
                + $"(Ammo, ammoType='{shared.m_ammoType}', 0 ammo damage — bow draw retained).");
        }

        /// <summary>ObjectDB phase — register the item into ObjectDB, add the 100-for-8-Wood recipe, wire
        /// the deterministic target return, and add the vanilla Archery Target to the Hammer build table.
        /// Idempotent across ObjectDB.Awake / CopyOtherDB re-fires.</summary>
        internal static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null || zns == null) return;

            var prefab = zns.GetPrefab(PracticeRangeContent.PracticeArrowItem);
            if (prefab != null) ArcherContentAssets.RegisterItemInObjectDB(prefab);

            AddPracticeArrowRecipe(odb, prefab);
            WireDeterministicTargetReturn(zns, prefab);
            AddArcheryTargetToHammer(odb, zns);

            contentBuilt = true;
            Plugin.Log.LogInfo("[Niflheim/Archer] Practice Range ObjectDB wiring complete (item + recipe + return + piece).");
        }

        internal static bool ContentBuilt => contentBuilt;

        private static void AddPracticeArrowRecipe(ObjectDB odb, GameObject? prefab)
        {
            var recipeDef = PracticeRangeContent.PracticeArrowRecipe;

            // Idempotent: don't add a second recipe for the same output item across re-fires.
            foreach (var r in odb.m_recipes)
            {
                if (r != null && r.m_item != null && r.m_item.gameObject != null &&
                    r.m_item.gameObject.name == recipeDef.OutputItem)
                    return;
            }

            var item = odb.GetItemPrefab(recipeDef.OutputItem)?.GetComponent<ItemDrop>();
            if (item == null)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/Archer] {recipeDef.OutputItem} not in ObjectDB at recipe time; skipping recipe.");
                return;
            }

            var wood = odb.GetItemPrefab(recipeDef.WoodItem)?.GetComponent<ItemDrop>();
            if (wood == null)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/Archer] Recipe resource '{recipeDef.WoodItem}' NOT FOUND in ObjectDB. "
                    + "Practice Arrow recipe would silently require no wood — skipping recipe registration.");
                return;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + recipeDef.OutputItem;
            recipe.m_item = item;
            recipe.m_amount = recipeDef.OutputCount;         // 100
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = null;                  // hand-craftable
            recipe.m_resources = new[]
            {
                new Piece.Requirement { m_resItem = wood, m_amount = recipeDef.WoodCost, m_recover = true }, // 8 Wood
            };
            odb.m_recipes.Add(recipe);
            Plugin.Log.LogInfo(
                $"[Niflheim/Archer] Added Practice Arrow recipe: {recipeDef.OutputCount} "
                + $"{recipeDef.OutputItem} for {recipeDef.WoodCost} {recipeDef.WoodItem}.");
        }

        private static void WireDeterministicTargetReturn(ZNetScene zns, GameObject? practiceArrowPrefab)
        {
            if (practiceArrowPrefab == null) return;
            var target = zns.GetPrefab(PracticeRangeContent.ArcheryTargetPrefab);
            if (target == null)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/Archer] Vanilla '{PracticeRangeContent.ArcheryTargetPrefab}' not in ZNetScene; "
                    + "deterministic Practice Arrow return not wired this boot.");
                return;
            }
            var archery = target.GetComponentInChildren<ArcheryTarget>(true);
            if (archery == null)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/Archer] '{PracticeRangeContent.ArcheryTargetPrefab}' has no ArcheryTarget component; "
                    + "cannot wire deterministic return.");
                return;
            }
            var drop = practiceArrowPrefab.GetComponent<ItemDrop>();
            if (drop == null) return;
            if (archery.m_returnAmmo == null)
                archery.m_returnAmmo = new System.Collections.Generic.List<ItemDrop>();
            if (!archery.m_returnAmmo.Contains(drop))
            {
                archery.m_returnAmmo.Add(drop);
                Plugin.Log.LogInfo(
                    "[Niflheim/Archer] Practice Arrow added to vanilla ArcheryTarget.m_returnAmmo "
                    + "(deterministic single return — no roll).");
            }
        }

        private static void AddArcheryTargetToHammer(ObjectDB odb, ZNetScene zns)
        {
            var target = zns.GetPrefab(PracticeRangeContent.ArcheryTargetPrefab);
            if (target == null)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/Archer] '{PracticeRangeContent.ArcheryTargetPrefab}' not in ZNetScene; "
                    + "cannot add to Hammer build table.");
                return;
            }
            var hammer = odb.GetItemPrefab("Hammer");
            var table = hammer?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            if (table?.m_pieces == null)
            {
                Plugin.Log.LogWarning("[Niflheim/Archer] Hammer PieceTable not resolvable; Archery Target not added.");
                return;
            }
            // Dedupe by NAME (a re-join re-clones prefabs into new references with the same name; a
            // reference Contains would append a duplicate — the "two benches" bug class).
            string name = target.name;
            int removed = table.m_pieces.RemoveAll(p => p == null || p.name == name);
            table.m_pieces.Add(target);
            Plugin.Log.LogInfo(
                $"[Niflheim/Archer] Ensured '{name}' present in Hammer build table (stripped {removed} stale). "
                + "Per-attempt capability AND enforced by ArcheryTargetPlacementGate.");
        }
    }
}
