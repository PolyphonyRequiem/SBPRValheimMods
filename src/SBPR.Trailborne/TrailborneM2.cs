using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    /// <summary>
    /// M2 content: 4 Cairn Marker variants (one per ink color) + 4 Cairn
    /// piece variants (one per color). Cairns are now full 5-tier with
    /// the v0.1.0 LOCKED behaviour:
    ///
    ///   • Stone ladder (cumulative): T1=9, T2=12, T3=15, T4=18, T5=21
    ///   • Comfort floor by tier:     T1=3, T2=4, T3=5, T4=6, T5=7
    ///   • Tier-1 build cost: 9 Stone + 1 Resin + 1 Cairn Marker
    ///   • Upgrade / Repair gesture: E-press, gated by HP &lt; 75%,
    ///     always repairs to max AND upgrades if tier &lt; 5. Cost = 3 Stone + 1 Resin
    ///     (per design/PARKED-2026-06-03.md; flat per gesture).
    ///   • Cairn is IMMUNE to player + monster damage. Only weather/time decay ticks.
    ///   • Out-of-zone decay: ZDO-persisted SBPR_LastWearTick (long, in-game day-time);
    ///     Harmony postfix on WearNTear.Awake backfills missed wear at vanilla rate.
    ///   • Shift+E debug flag (SBPR_DebugCairnDamage, default true v0.1.0) drops a
    ///     pristine cairn to 70% so the combo gesture is exercisable without waiting
    ///     on weather.
    ///
    /// Decay state machine (LOCKED per requirements.md §A3.5):
    ///   ≥75% HP   → pristine (resin glows visually — wired in M2.5+ when we have a glow VFX)
    ///   &lt;75% HP   → fizzled (visual maintenance signal)
    ///   &lt;25% HP   → downgrade tier  [HOOK PRESENT; M2.5+ wires actual downgrade visual]
    ///    0% HP    → collapse         [vanilla WearNTear destroy path]
    ///
    /// All gated behind SBPRContext.OnSBServer.
    /// </summary>
    public static class TrailborneM2
    {
        // Color identifiers — must match TrailborneM1 ink names
        public static readonly string[] Colors = { "red", "white", "blue", "black" };

        public static string MarkerName(string color) => "SBPR_CairnMarker_" + color;
        public static string CairnName (string color) => "piece_sbpr_cairn_" + color;
        public static string InkNameFor(string color)
        {
            switch (color)
            {
                case "red":   return TrailborneM1.InkRedName;
                case "white": return TrailborneM1.InkWhiteName;
                case "blue":  return TrailborneM1.InkBlueName;
                case "black": return TrailborneM1.InkBlackName;
                default: return TrailborneM1.InkWhiteName;
            }
        }

        // Back-compat: code outside M2 still references this name for "any marker".
        public const string CairnMarkerItemName = "SBPR_CairnMarker_white";

        private const string SourceConsumable = "Coins";
        private const string SourceBonfire    = "bonfire";

        // ── Cairn tier tables (LOCKED v0.1.0) ────────────────────────
        // Indexed 1..5; index 0 is sentinel.
        public  const int MaxTier = 5;
        private static readonly int[] StoneByTier        = { 0, 9, 12, 15, 18, 21 };
        private static readonly int[] ComfortFloorByTier = { 0, 3,  4,  5,  6,  7 };
        public  const int UpgradeStoneCost = 3;
        public  const int UpgradeResinCost = 1;
        public  const float CairnComfortRadius = 10f;
        public  const float PristineHpFraction = 0.75f;
        public  const float DowngradeHpFraction = 0.25f;
        public  const float DebugDamageTargetFraction = 0.70f;

        // ZDO keys
        public  const string ZdoTier         = "SBPR_CairnTier";
        public  const string ZdoLastWearTick = "SBPR_LastWearTick";

        public static int StoneCostForTier(int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > MaxTier) tier = MaxTier;
            return StoneByTier[tier];
        }

        public static int ComfortFloorForTier(int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > MaxTier) tier = MaxTier;
            return ComfortFloorByTier[tier];
        }

        public static void RegisterPrefabs(ZNetScene zns)
        {
            foreach (var c in Colors)
            {
                RegisterCairnMarkerPrefab(zns, c);
                RegisterCairnPiecePrefab(zns, c);
            }
        }

        private static void RegisterCairnMarkerPrefab(ZNetScene zns, string color)
        {
            var name = MarkerName(color);
            if (zns.GetPrefab(name) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceConsumable, name);
            if (clone == null) return;
            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_shared.m_name        = "Cairn Marker (" + Capitalize(color) + ")";
                drop.m_itemData.m_shared.m_description =
                    "A wooden marker plank with a " + color + " hide pennant. Place on stones to declare a Cairn.";
                drop.m_itemData.m_shared.m_maxStackSize = 10;
                drop.m_itemData.m_shared.m_weight      = 0.5f;
                drop.m_itemData.m_shared.m_itemType    = ItemDrop.ItemData.ItemType.Material;
                var sprite = TrailborneAssets.LoadPngAsSprite("cairn_marker_v0.1.png");
                if (sprite != null) drop.m_itemData.m_shared.m_icons = new[] { sprite };
            }
            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M2] Registered cairn marker item: {name}");
        }

        private static void RegisterCairnPiecePrefab(ZNetScene zns, string color)
        {
            var name = CairnName(color);
            if (zns.GetPrefab(name) != null) return;
            // Bonfire is a chunky stone-y piece; use as a base and bury its visual
            // children under a runtime-assembled kitbash stack (see BuildKitbashArt).
            var clone = TrailborneAssets.ClonePrefab(SourceBonfire, name);
            if (clone == null)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Source bonfire prefab missing, skipping cairn ({color}).");
                return;
            }
            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Cairn (" + Capitalize(color) + ")";
                piece.m_description =
                    "A " + color + "-marked stone cairn. Stack stones to raise its tier and comfort floor. " +
                    "E to repair-and-upgrade when fizzled (<75% HP). Immune to combat damage — weathers over time.";
                piece.m_category    = Piece.PieceCategory.Crafting;
                piece.m_resources   = new[]
                {
                    BuildReq("Stone", StoneCostForTier(1)),
                    BuildReq("Resin", 1),
                    BuildReq(MarkerName(color), 1),
                };
                var sprite = TrailborneAssets.LoadPngAsSprite("cairn_marker_v0.1.png");
                if (sprite != null) piece.m_icon = sprite;
                // Comfort is applied dynamically via SE_Rested patch — base piece carries 0
                // so we don't double-count in the vanilla ComfortGroup dedup table.
                piece.m_comfort = 0;
                piece.m_comfortGroup = Piece.ComfortGroup.None;
            }

            var tag = clone.AddComponent<TrailborneCairnTag>();
            tag.Color = color;

            // The Cairn interactable handles E (repair+upgrade combo) and Shift+E
            // (debug damage, gated on TrailbornePlugin.DebugCairnDamage config).
            clone.AddComponent<TrailborneCairnInteractable>();

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M2] Registered cairn piece: {name}");
        }

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            foreach (var color in Colors)
            {
                var markerName = MarkerName(color);
                var marker = zns?.GetPrefab(markerName);
                if (marker != null) TrailborneAssets.RegisterItemInObjectDB(marker);

                if (!HasRecipe(markerName))
                {
                    var markerItem = odb.GetItemPrefab(markerName);
                    if (markerItem != null)
                    {
                        var recipe = ScriptableObject.CreateInstance<Recipe>();
                        recipe.name              = "Recipe_" + markerName;
                        recipe.m_item            = markerItem.GetComponent<ItemDrop>();
                        recipe.m_amount          = 1;
                        recipe.m_minStationLevel = 1;
                        recipe.m_craftingStation = FindStation("piece_sbpr_explorers_bench");
                        recipe.m_resources       = new[]
                        {
                            BuildReq("LeatherScraps", 2),
                            BuildReq("FineWood", 1),
                            BuildReq(InkNameFor(color), 1),
                        };
                        odb.m_recipes.Add(recipe);
                    }
                }
            }

            // Cairn pieces into Hammer build menu + REBUILD their resource list
            // now that markers exist in ObjectDB. (Pieces built at ZNetScene.Awake
            // had null marker requirements because ODB wasn't populated yet.)
            var hammerTable = TrailborneAssets.GetHammerPieceTable();
            foreach (var color in Colors)
            {
                var cairnPrefab = zns?.GetPrefab(CairnName(color));
                if (cairnPrefab == null) continue;
                var piece = cairnPrefab.GetComponent<Piece>();
                if (piece != null)
                {
                    piece.m_resources = new[]
                    {
                        BuildReq("Stone", StoneCostForTier(1)),
                        BuildReq("Resin", 1),
                        BuildReq(MarkerName(color), 1),
                    };
                }
                if (hammerTable != null) TrailborneAssets.AddPieceToTable(cairnPrefab, hammerTable);
            }

            TrailbornePlugin.Log.LogInfo(
                $"[Trailborne/M2] M2 ObjectDB wiring complete (4 marker variants + 4 cairn variants + " +
                $"5-tier ladder 9/12/15/18/21 stone, comfort floors 3/4/5/6/7).");
        }

        // ───────────────────────────────────────────────
        // SE_Rested comfort patch — inject cairn comfort floor
        // ───────────────────────────────────────────────

        /// <summary>
        /// Returns the highest comfort floor of any in-range cairn at <paramref name="position"/>,
        /// or 0 if none. Tier-aware: reads ZDO SBPR_CairnTier (default 1).
        /// </summary>
        public static int GetCairnComfortBonus(Vector3 position)
        {
            int floor = 0;
            var hits = Physics.OverlapSphere(position, CairnComfortRadius);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h == null) continue;
                var tag = h.GetComponentInParent<TrailborneCairnTag>();
                if (tag == null) continue;
                int tier = tag.ReadTier();
                int bonus = ComfortFloorForTier(tier);
                if (bonus > floor) floor = bonus;
            }
            return floor;
        }

        private static bool HasRecipe(string itemPrefabName)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return false;
            foreach (var r in odb.m_recipes)
                if (r != null && r.m_item != null && r.m_item.gameObject != null && r.m_item.gameObject.name == itemPrefabName)
                    return true;
            return false;
        }

        private static CraftingStation FindStation(string piecePrefabName)
        {
            var zns = ZNetScene.instance;
            var p = zns?.GetPrefab(piecePrefabName);
            var station = p?.GetComponent<CraftingStation>();
            if (station == null)
            {
                TrailbornePlugin.Log.LogWarning(
                    $"[Trailborne/M2] FindStation: '{piecePrefabName}' missing or has no CraftingStation. " +
                    "Recipe will register against null station (no bench requirement).");
            }
            return station;
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return TrailborneAssets.BuildReq(resourcePrefabName, amount, "M2");
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }

    /// <summary>
    /// Marker tag attached to each cairn clone. Carries color identity and provides
    /// ZDO-backed tier accessors. Also assembles + rescales the kitbash rock stack
    /// based on tier on Awake / tier change.
    /// </summary>
    public class TrailborneCairnTag : MonoBehaviour
    {
        public string Color;
        private ZNetView _nview;
        private GameObject _kitbashRoot;
        private int _lastBuiltTier = -1;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            BuildKitbashArt(); // tier from ZDO or default 1
        }

        public int ReadTier()
        {
            if (_nview == null || _nview.GetZDO() == null) return 1;
            int t = _nview.GetZDO().GetInt(TrailborneM2.ZdoTier, 1);
            if (t < 1) t = 1;
            if (t > TrailborneM2.MaxTier) t = TrailborneM2.MaxTier;
            return t;
        }

        public bool WriteTier(int newTier)
        {
            if (_nview == null || _nview.GetZDO() == null) return false;
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
            if (newTier < 1) newTier = 1;
            if (newTier > TrailborneM2.MaxTier) newTier = TrailborneM2.MaxTier;
            _nview.GetZDO().Set(TrailborneM2.ZdoTier, newTier);
            BuildKitbashArt();
            return true;
        }

        /// <summary>
        /// Kitbash: take a single `rock_low` mesh, flatten it vertically, stack N
        /// copies with rotation + offset + lateral-scale jitter where N scales with
        /// tier. Reseeded deterministically from the ZDO id so a cairn looks the
        /// same across reloads. Cheap, no asset bundles, gives us a tiered visual
        /// for v0.1.0 playtest without custom meshes.
        /// </summary>
        public void BuildKitbashArt()
        {
            int tier = ReadTier();
            if (tier == _lastBuiltTier && _kitbashRoot != null) return;
            _lastBuiltTier = tier;

            // Strip prior kitbash root
            if (_kitbashRoot != null) UnityEngine.Object.Destroy(_kitbashRoot);

            var zns = ZNetScene.instance;
            if (zns == null) return;
            var rockSrc = zns.GetPrefab("rock_low");
            if (rockSrc == null)
            {
                TrailbornePlugin.Log.LogWarning("[Trailborne/M2] rock_low prefab missing; cairn art will be bonfire-stub.");
                return;
            }

            _kitbashRoot = new GameObject("SBPR_CairnKitbash");
            _kitbashRoot.transform.SetParent(transform, worldPositionStays: false);

            // Pile size scales with tier: T1=4, T2=6, T3=8, T4=10, T5=12 stones.
            int stones = 2 + tier * 2;

            // Deterministic seed from ZDO so the same cairn looks the same after reload.
            int seed = 1337;
            if (_nview != null && _nview.GetZDO() != null)
                seed = _nview.GetZDO().m_uid.GetHashCode();
            var rng = new System.Random(seed);

            // Stack from bottom up; each layer shrinks slightly.
            float baseRadius = 0.45f + 0.05f * tier;   // wider pile at higher tier
            float layerHeight = 0.16f;                 // flat layer thickness
            for (int i = 0; i < stones; i++)
            {
                // Reuse only the rock_low's visual children — clone a fresh GO with the mesh.
                var rockClone = GameObject.Instantiate(rockSrc, _kitbashRoot.transform);
                // Strip behaviour components — we want art only, no terrain modifier / pickable.
                StripGameplayComponents(rockClone);

                float t01 = (float)i / Mathf.Max(1, stones - 1); // 0..1 from bottom to top
                float ringScale = Mathf.Lerp(1.0f, 0.4f, t01);   // smaller at the top

                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float offRad = (float)(rng.NextDouble() * baseRadius * ringScale * 0.6f);
                float ox = Mathf.Cos(angle) * offRad;
                float oz = Mathf.Sin(angle) * offRad;
                float oy = layerHeight * i + 0.05f;

                // Lateral scale jitter — flatten Y, vary X/Z
                float sxz = (float)(0.55f + rng.NextDouble() * 0.45f) * (0.8f + 0.4f * ringScale);
                float sy  = (float)(0.18f + rng.NextDouble() * 0.10f);

                rockClone.transform.localPosition = new Vector3(ox, oy, oz);
                rockClone.transform.localRotation = Quaternion.Euler(
                    (float)(rng.NextDouble() * 20.0 - 10.0),
                    (float)(rng.NextDouble() * 360.0),
                    (float)(rng.NextDouble() * 20.0 - 10.0));
                rockClone.transform.localScale = new Vector3(sxz, sy, sxz);
                rockClone.SetActive(true);
            }

            // Push the bonfire's vanilla mesh out of view (children named "Flame"/"Fuel"/etc).
            // We keep the WearNTear + Piece + ZNetView on the root prefab — only hide visuals.
            HideVanillaVisualChildren();
        }

        private static void StripGameplayComponents(GameObject go)
        {
            // Remove anything that would cause weird side effects (loot drops, terrain edits, sounds).
            var bad = new List<Component>();
            foreach (var c in go.GetComponentsInChildren<Component>(includeInactive: true))
            {
                if (c == null) continue;
                if (c is Transform) continue;
                if (c is MeshFilter) continue;
                if (c is MeshRenderer) continue;
                if (c is Renderer) continue;
                bad.Add(c);
            }
            foreach (var c in bad)
            {
                try { UnityEngine.Object.DestroyImmediate(c); } catch { /* swallow — some components refuse mid-Awake destroy */ }
            }
        }

        private void HideVanillaVisualChildren()
        {
            // Bonfire base art lives under named children; turn them off without nuking the prefab.
            string[] hideNames = { "Flame", "fire", "Fire", "Smoke", "smoke", "model", "default", "BFX", "fuel", "Fuel" };
            foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t == null || t == transform) continue;
                if (t.parent == _kitbashRoot?.transform || t.IsChildOf(_kitbashRoot?.transform ?? transform) && _kitbashRoot != null && t != _kitbashRoot.transform) continue;
                foreach (var n in hideNames)
                {
                    if (t.name.Contains(n))
                    {
                        var rs = t.GetComponentsInChildren<Renderer>(includeInactive: true);
                        foreach (var r in rs) if (r != null) r.enabled = false;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Interactable surface for cairns:
    ///   • E (no alt)          → repair+upgrade combo, gated on HP &lt; 75%
    ///   • Shift+E (alt=true)  → debug-damage to 70% HP, gated on TrailbornePlugin.DebugCairnDamage
    ///
    /// Hover text reports tier, HP%, and next-action affordance.
    /// </summary>
    public class TrailborneCairnInteractable : MonoBehaviour, Hoverable, Interactable
    {
        private const float UseDistance = 4.0f;
        private TrailborneCairnTag _tag;
        private WearNTear _wnt;
        private Piece _piece;

        private void Awake()
        {
            _tag = GetComponent<TrailborneCairnTag>();
            _wnt = GetComponent<WearNTear>();
            _piece = GetComponent<Piece>();
        }

        public string GetHoverName()
        {
            return _piece != null ? _piece.m_name : "Cairn";
        }

        public string GetHoverText()
        {
            if (_tag == null) return GetHoverName();
            int tier = _tag.ReadTier();
            float hp = _wnt != null ? _wnt.GetHealthPercentage() : 1f;
            int hpPct = Mathf.RoundToInt(hp * 100f);
            int comfortFloor = TrailborneM2.ComfortFloorForTier(tier);

            string line2;
            if (hp < TrailborneM2.PristineHpFraction)
            {
                if (tier < TrailborneM2.MaxTier)
                    line2 = $"[<color=yellow><b>$KEY_Use</b></color>] Repair + Upgrade → T{tier + 1} ({TrailborneM2.UpgradeStoneCost} Stone + {TrailborneM2.UpgradeResinCost} Resin)";
                else
                    line2 = $"[<color=yellow><b>$KEY_Use</b></color>] Repair ({TrailborneM2.UpgradeStoneCost} Stone + {TrailborneM2.UpgradeResinCost} Resin)";
            }
            else
            {
                line2 = "Pristine — no maintenance needed.";
            }

            string line3 = "";
            if (TrailbornePlugin.DebugCairnDamage != null && TrailbornePlugin.DebugCairnDamage.Value)
                line3 = "\n[<color=#ff8b3d><b>Shift+$KEY_Use</b></color>] (debug) damage to 70%";

            return $"{_piece.m_name}\nTier {tier} / {TrailborneM2.MaxTier} — comfort floor {comfortFloor} — HP {hpPct}%\n{line2}{line3}";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (user != Player.m_localPlayer) return false;
            if (Vector3.Distance(user.transform.position, transform.position) > UseDistance + 1.0f) return false;
            if (_tag == null || _wnt == null) return false;

            if (alt)
            {
                // Shift+E debug damage
                if (TrailbornePlugin.DebugCairnDamage == null || !TrailbornePlugin.DebugCairnDamage.Value)
                    return false;
                return DoDebugDamage();
            }

            return DoRepairAndUpgrade(user);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private bool DoRepairAndUpgrade(Humanoid user)
        {
            float hp = _wnt.GetHealthPercentage();
            if (hp >= TrailborneM2.PristineHpFraction)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    "Cairn is pristine — no maintenance needed.");
                return true;
            }

            var inv = user.GetInventory();
            if (inv == null) return false;
            int needStone = TrailborneM2.UpgradeStoneCost;
            int needResin = TrailborneM2.UpgradeResinCost;
            if (inv.CountItems("$item_stone") < needStone || inv.CountItems("$item_resin") < needResin)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    $"Need {needStone} Stone + {needResin} Resin.");
                return true;
            }

            // Pay
            inv.RemoveItem("$item_stone", needStone);
            inv.RemoveItem("$item_resin", needResin);

            // Repair to max via vanilla path — handles ZDO + RPC fanout cleanly.
            try { _wnt.Repair(); }
            catch (Exception e) { TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Repair() threw: {e.Message}"); }

            int tier = _tag.ReadTier();
            string action = "Repaired";
            if (tier < TrailborneM2.MaxTier)
            {
                _tag.WriteTier(tier + 1);
                action = $"Repaired + upgraded to T{tier + 1}";
            }

            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                $"{action} ({needStone}S + {needResin}R)");

            // Refresh comfort if the player is nearby (cheap, the SE_Rested patch
            // re-runs OverlapSphere on each comfort calculation tick anyway).
            return true;
        }

        private bool DoDebugDamage()
        {
            if (_wnt == null) return false;
            float curPct = _wnt.GetHealthPercentage();
            if (curPct < TrailborneM2.PristineHpFraction)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    $"(debug) Cairn already fizzled — HP {Mathf.RoundToInt(curPct * 100f)}%");
                return true;
            }
            // ApplyDamage bypasses our Damage(HitData) immunity prefix on purpose —
            // this is the deliberate debug back-door for the v0.1.0 playtest.
            float damageAmount = _wnt.m_health * (1f - TrailborneM2.DebugDamageTargetFraction);
            try { _wnt.ApplyDamage(damageAmount); }
            catch (Exception e)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Debug ApplyDamage threw: {e.Message}");
                return false;
            }
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                $"(debug) Cairn damaged to ~{Mathf.RoundToInt(TrailborneM2.DebugDamageTargetFraction * 100f)}% HP");
            return true;
        }
    }

    /// <summary>
    /// Harmony patches for cairn behavior:
    ///   • WearNTear.Damage prefix → swallow damage on cairns (combat-immune,
    ///     only natural UpdateWear decay ticks affect HP).
    ///   • WearNTear.Awake postfix → backfill missed wear ticks when a chunk
    ///     loads after being out-of-zone, using ZDO SBPR_LastWearTick.
    ///   • SE_Rested.CalculateComfortLevel postfix → max-clamp cairn comfort
    ///     floor into the result.
    /// </summary>
    [HarmonyPatch]
    public static class TrailborneCairnPatches
    {
        // ── Damage immunity ─────────────────────────────────────────────
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
        [HarmonyPrefix]
        public static bool WearNTear_Damage_Prefix(WearNTear __instance)
        {
            if (__instance == null) return true;
            // GetComponentInParent so Harmony works regardless of which child collider was hit.
            var tag = __instance.GetComponent<TrailborneCairnTag>();
            if (tag == null) tag = __instance.GetComponentInParent<TrailborneCairnTag>();
            if (tag != null)
            {
                // Swallow the damage entirely. Cairns only decay via UpdateWear weather paths.
                return false;
            }
            return true;
        }

        // ── Out-of-zone decay backfill ──────────────────────────────────
        [HarmonyPatch(typeof(WearNTear), "Awake")]
        [HarmonyPostfix]
        public static void WearNTear_Awake_Postfix(WearNTear __instance)
        {
            if (__instance == null) return;
            var tag = __instance.GetComponent<TrailborneCairnTag>();
            if (tag == null) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || nview.GetZDO() == null) return;
            if (!nview.IsOwner()) return; // owner-only writes ZDO

            double nowDay = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() / 86400.0 : 0.0;
            // GetFloat works for backfill — Valheim's in-game-day is small enough to fit comfortably.
            float lastWearDay = nview.GetZDO().GetFloat(TrailborneM2.ZdoLastWearTick, -1f);
            if (lastWearDay < 0f)
            {
                // First load — seed and bail.
                nview.GetZDO().Set(TrailborneM2.ZdoLastWearTick, (float)nowDay);
                return;
            }

            float deltaDays = (float)nowDay - lastWearDay;
            if (deltaDays <= 0f)
            {
                nview.GetZDO().Set(TrailborneM2.ZdoLastWearTick, (float)nowDay);
                return;
            }

            // Vanilla c_RainDamage is 5 HP per c_RainDamageTime (60s) clamped to
            // c_RainDamageMax (0.5 of max). Use a conservative day-rate proxy:
            // ~10 HP/day weather decay when missed. Tuning lives in v0.2.0.
            const float decayHpPerDay = 10f;
            float decayHp = decayHpPerDay * deltaDays;
            float curHp = nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health);
            float newHp = Mathf.Max(__instance.m_health * 0.05f, curHp - decayHp); // don't kill from backfill
            if (newHp < curHp)
            {
                nview.GetZDO().Set(ZDOVars.s_health, newHp);
                nview.InvokeRPC(ZNetView.Everybody, "WNTHealthChanged", newHp);
                TrailbornePlugin.Log.LogInfo(
                    $"[Trailborne/M2] Cairn backfill: missed {deltaDays:F2}d → decayed {decayHp:F1} HP ({curHp:F0} → {newHp:F0}).");
            }
            nview.GetZDO().Set(TrailborneM2.ZdoLastWearTick, (float)nowDay);
        }

        // ── Comfort floor injection ─────────────────────────────────────
        // Patches SE_Rested.CalculateComfortLevel(bool inShelter, Vector3 position)
        // to clamp the result UP to the highest cairn comfort floor in range.
        // Doesn't touch the vanilla ComfortGroup table — cairns live OUTSIDE it
        // intentionally so they stack on top instead of dedup-replacing furniture.
        [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), new Type[] { typeof(bool), typeof(Vector3) })]
        [HarmonyPostfix]
        public static void SE_Rested_CalculateComfortLevel_Postfix(bool inShelter, Vector3 position, ref int __result)
        {
            try
            {
                int bonus = TrailborneM2.GetCairnComfortBonus(position);
                if (bonus > __result) __result = bonus;
            }
            catch (Exception e)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Comfort patch suppressed exception: {e.Message}");
            }
        }
    }
}
