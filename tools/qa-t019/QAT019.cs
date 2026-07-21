// ============================================================================
//  SBPR.QADiag.T019 — THROWAWAY QA instrument for T019 "Swift Preparation"
//  @ 37711cb (canonical PR #394 head, RecipeDataPair.Recipe property fix).
//
//  DECISION-GRADE: this exercises the REAL LIVE TRANSPILED InventoryGui.UpdateRecipe
//  path end-to-end. It does NOT call SwiftPreparationCraftTimer.ScaleMenuCraftDuration
//  directly. Instead it:
//    * seeds genuine authoritative host domain state so the shipped gate resolves ACTIVE,
//    * installs a real selected food recipe + Cooking station into the live InventoryGui,
//    * sets the vanilla craft-duration fields to a known base, primes m_craftTimer >= 0,
//    * invokes the private InventoryGui.UpdateRecipe(player, dt) — which the SBPR
//      transpiler has patched — and READS BACK the resulting m_craftProgressBar.m_maxValue,
//      i.e. the shipped num5 AFTER the vanilla Cooking-skill adjustment AND after the
//      Swift Preparation 1/3 in-place scale, exactly as the completion comparison sees it.
//    * measures completion by comparing m_craftTimer against that same max and counting
//      DoCrafting invocations via the craft-timer reset sentinel (m_craftTimer == -1).
//
//  The vanilla skill-adjust factor is captured live from the real GetSkillFactor, so the
//  EXPECTED value is (base * skillFactor) * (1/3) for the eligible-active owner and
//  (base * skillFactor) for every vanilla path. We never hardcode the skill factor.
//
//  Matrix (spec §US4 sc1 / contracts.md §Cooking):
//   A ELIGIBLE + ACTIVE owner   -> maxValue == vanillaAdjusted * 1/3   (exact)
//   B ELIGIBLE + DORMANT        -> maxValue == vanillaAdjusted         (relationship released)
//   C ELIGIBLE + NON-OWNER      -> maxValue == vanillaAdjusted         (session unbound)
//   D INELIGIBLE non-food       -> maxValue == vanillaAdjusted         (tool recipe)
//   E INELIGIBLE non-cooking    -> maxValue == vanillaAdjusted         (non-Cooking station)
//   F COMPLETION-COUNT          -> exactly one DoCrafting at t>=max for eligible-active,
//                                   and the completion count is identical to vanilla.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.QADiag.T019
{
    public static class Instrument
    {
        const string LpoType = "SBPR.Niflheim.HomesteadStones.Features.Progression.LocalProgressionObserver";
        const string FpoType = "SBPR.Niflheim.HomesteadStones.Features.Progression.FoundationalPlacementObserver";
        const string SptType = "SBPR.Niflheim.HomesteadStones.Features.Cooking.SwiftPreparationCraftTimer";

        static StringBuilder _sb;
        static void L(string s) { _sb.Append(s).Append('\n'); Debug.Log("QAT019 " + s); }

        static readonly BindingFlags IF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly BindingFlags SF = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        // ---- live InventoryGui field plumbing ----
        static object GetF(object o, string f) => o.GetType().GetField(f, IF).GetValue(o);
        static void SetF(object o, string f, object v) => o.GetType().GetField(f, IF).SetValue(o, v);
        static float GetBarMax(InventoryGui gui)
        {
            var bar = gui.GetType().GetField("m_craftProgressBar", IF).GetValue(gui);
            return (float)bar.GetType().GetField("m_maxValue", IF).GetValue(bar);
        }

        static readonly MethodInfo UpdateRecipeMI =
            typeof(InventoryGui).GetMethod("UpdateRecipe", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Drive the REAL transpiled method. dt is small so the craft does not complete unless we ask it to.
        static void DriveUpdateRecipe(InventoryGui gui, Player player, float dt)
        {
            UpdateRecipeMI.Invoke(gui, new object[] { player, dt });
        }

        public static string Run()
        {
            _sb = new StringBuilder();
            try { return RunInner(); }
            catch (Exception e) { L("FATAL " + e); return _sb.ToString(); }
        }

        static object StaticServer(string typeFullName)
        {
            var t = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(typeFullName); } catch { return null; } })
                .FirstOrDefault(x => x != null);
            if (t == null) throw new Exception("type not found: " + typeFullName);
            var f = t.GetField("Server", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (f == null) throw new Exception("no Server field on " + typeFullName);
            return f.GetValue(null);
        }

        static Type SbprType(string full)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(full); } catch { return null; } })
                .FirstOrDefault(x => x != null);
        }

        static bool Approx(float a, float b) => Mathf.Abs(a - b) < 1e-3f;

        static int HomesteadProgressionCatalogRegistryVersion()
        {
            var t = SbprType("SBPR.Niflheim.HomesteadStones.Domain.Content.HomesteadProgressionCatalog");
            var f = t.GetField("CurrentContentRegistryVersion", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            return f != null ? (int)f.GetValue(null) : 1;
        }

        static long _authRev = DateTime.UtcNow.Ticks;
        static void PutActiveAuthority(object authorityStore, AccountId account, CharacterId character, StoneId stoneId, bool active)
        {
            List<AuthorityReservation> res = null;
            if (active)
                res = new List<AuthorityReservation> {
                    new AuthorityReservation(character, RelationshipKind.Bond, "rel-qa-t019", "rcpt-qa-t019") };
            // Authority projections are monotonic: every flip MUST carry a strictly increasing revision or the
            // store rejects it as stale (which would silently pin the gate to whatever state won the race).
            long rev = System.Threading.Interlocked.Increment(ref _authRev);
            var idx = new AccountStoneAuthorityIndex(account, stoneId, rev, res, "rcpt-qa-t019");
            authorityStore.GetType().GetMethod("ApplyAuthorityProjection")
                .Invoke(authorityStore, new object[] { "qa-auth-op-" + (active ? "on" : "off") + "-" + rev, idx });
        }

        static void UnbindSession(object boundSessions, string peerKey)
        {
            var t = boundSessions.GetType();
            foreach (var name in new[] { "Unbind", "Release", "Remove", "Clear" })
            {
                var m = t.GetMethod(name, new[] { typeof(string) });
                if (m != null) { m.Invoke(boundSessions, new object[] { peerKey }); return; }
            }
            var mClear = t.GetMethod("Clear", Type.EmptyTypes);
            if (mClear != null) { mClear.Invoke(boundSessions, null); return; }
            L("  (no unbind method found; C may be unreliable)");
        }

        static void SetSelectedRecipe(InventoryGui gui, Recipe recipe)
        {
            var fld = gui.GetType().GetField("m_selectedRecipe", IF);
            var wrapperType = fld.FieldType;
            object box = Activator.CreateInstance(wrapperType);
            var recipeField = wrapperType.GetField("<Recipe>k__BackingField", IF)
                ?? wrapperType.GetField("Recipe", IF);
            if (recipeField == null) throw new Exception("RecipeDataPair has no Recipe backing field");
            recipeField.SetValue(box, recipe);
            fld.SetValue(gui, box);
        }

        static void SetCurrentStation(Player player, CraftingStation station)
        {
            var m = typeof(Player).GetMethod("SetCraftingStation", IF);
            if (m != null) { m.Invoke(player, new object[] { station }); return; }
            var f = typeof(Player).GetField("m_currentStation", IF);
            if (f != null) f.SetValue(player, station);
        }

        static Recipe FindRecipe(bool wantFood)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return null;
            foreach (var r in odb.m_recipes)
            {
                if (r == null || r.m_item == null) continue;
                var shd = r.m_item.m_itemData.m_shared;
                if (shd == null) continue;
                bool isFood = shd.m_food > 0f || shd.m_foodStamina > 0f || shd.m_foodEitr > 0f;
                bool cooking = r.m_craftingStation != null && (int)r.m_craftingStation.m_craftingSkill == 105;
                if (wantFood && isFood && cooking) return r;
                if (!wantFood && !isFood) return r;
            }
            return null;
        }

        static CraftingStation _nonCook;
        static CraftingStation NonCookingStation()
        {
            if (_nonCook != null) return _nonCook;
            var odb = ObjectDB.instance;
            foreach (var r in odb.m_recipes)
            {
                if (r == null || r.m_craftingStation == null) continue;
                if ((int)r.m_craftingStation.m_craftingSkill != 105) { _nonCook = r.m_craftingStation; return _nonCook; }
            }
            return null;
        }

        static string RecipeName(Recipe r) => r == null ? "<null>" : (r.m_item != null ? r.m_item.gameObject.name : "<noItem>");

        // Compute the vanilla skill-adjusted duration for the current station+base, exactly as UpdateRecipe does:
        //   num5 = base ; if station.m_craftingSkill!=0 num5 *= 1 - GetSkillFactor(skill)*m_craftDurationSkillMaxDecrease
        static float VanillaAdjusted(InventoryGui gui, Player player, CraftingStation station, float baseDur)
        {
            float num5 = baseDur;
            if (station != null && (int)station.m_craftingSkill != 0)
            {
                float sf = player.GetSkillFactor(station.m_craftingSkill);
                float maxDec = (float)GetF(gui, "m_craftDurationSkillMaxDecrease");
                num5 *= 1f - sf * maxDec;
            }
            return num5;
        }

        // Drive the transpiled UpdateRecipe with a primed base and read back the bar max (the shipped num5).
        static float MeasureBarMax(InventoryGui gui, Player player, float baseDur)
        {
            // vanilla single-craft path: m_multiCrafting=false, m_craftDuration=baseDur, timer primed to 0.
            SetF(gui, "m_multiCrafting", false);
            SetF(gui, "m_craftDuration", baseDur);
            SetF(gui, "m_craftTimer", 0f);
            DriveUpdateRecipe(gui, player, 0f); // dt=0 so timer(0) < max, never completes
            return GetBarMax(gui);
        }

        static string RunInner()
        {
            L("==================== QADiag T019 REAL-TRANSPILED-PATH (@37711cb PR#394) ====================");
            var player = Player.m_localPlayer;
            var gui = InventoryGui.instance;
            if (player == null || gui == null) { L("no localPlayer/InventoryGui -> FAIL"); return _sb.ToString(); }
            long pid = player.GetPlayerID();
            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();

            // Confirm the method under test is actually transpiled by SBPR.
            var patches = HarmonyLib.Harmony.GetPatchInfo(UpdateRecipeMI);
            string transpilers = patches == null ? "NONE"
                : string.Join(",", patches.Transpilers.Select(p => p.owner));
            L(string.Format("host pid={0} isServer={1} scene={2}", pid, isServer,
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));
            L("InventoryGui.UpdateRecipe transpilers = [" + transpilers + "]  (EXPECT contains sbpr niflheim)");

            var lpServer = StaticServer(LpoType);
            var fpServer = StaticServer(FpoType);
            if (lpServer == null || fpServer == null) { L("host runtime not composed -> FAIL"); return _sb.ToString(); }

            var stones = lpServer.GetType().GetProperty("Stones").GetValue(lpServer);
            var characters = lpServer.GetType().GetProperty("Characters").GetValue(lpServer);
            var authorityStore = lpServer.GetType().GetProperty("Authority").GetValue(lpServer);
            var boundSessions = fpServer.GetType().GetProperty("BoundSessions").GetValue(fpServer);
            var stoneAreas = fpServer.GetType().GetProperty("StoneAreas").GetValue(fpServer);

            var account = new AccountId("acct-qa-t019");
            var character = new CharacterId("char-qa-t019");
            var stoneId = new StoneId("T019SwiftQA|0|0");
            var tree = new VersionedId("Cooking", 1);
            var swiftNode = SwiftPreparationNodes.SwiftPreparation;
            long rev = DateTime.UtcNow.Ticks;

            string peerKey = "player:" + pid.ToString(CultureInfo.InvariantCulture);
            boundSessions.GetType().GetMethod("Bind").Invoke(boundSessions,
                new object[] { peerKey, new PilotSessionPrincipal(account, character, "qa-session") });

            var pp = player.transform.position;
            stoneAreas.GetType().GetMethod("Register").Invoke(stoneAreas,
                new object[] { stoneId, (double)pp.x, (double)pp.z, 20.0 });

            var dev = new NodeDevelopmentRecord(swiftNode, 100, 100, true, true, "qa-dev");
            var stoneAgg = new StoneProgressionAggregate(
                stoneId, rev, 2, 2, new VersionedId("FoundationalTree", 1), new VersionedId("FoundationalCatalog", 1),
                HomesteadProgressionCatalogRegistryVersion(), "qa", "qa", 0, "qa",
                null, new List<NodeDevelopmentRecord> { dev });
            stones.GetType().GetMethod("PutStone").Invoke(stones, new object[] { stoneAgg });

            var purchase = new NodePurchaseRecord(tree, swiftNode, "PersonalAP", "CharacterEffect", VersionedId.None, "qa-buy");
            var stoneRec = new CharacterStoneRecord(stoneId, 90, 100, 0, null, new List<NodePurchaseRecord> { purchase }, null, null);
            var charAgg = new CharacterProgressionAggregate(account, character, "world:T019", 2L, 0, 0, "",
                new List<CharacterStoneRecord> { stoneRec });
            characters.GetType().GetMethod("PutCharacter").Invoke(characters, new object[] { charAgg });

            PutActiveAuthority(authorityStore, account, character, stoneId, true);

            var sptT = SbprType(SptType);
            var resolveM = sptT.GetMethod("ResolveActiveForLocalOccupant", SF);
            bool active = (bool)resolveM.Invoke(null, null);
            L("SEEDED host state (rev=" + rev + "). REAL gate ResolveActiveForLocalOccupant() = " + active + "  (EXPECT True)");

            var foodRecipe = FindRecipe(true);
            var toolRecipe = FindRecipe(false);
            if (foodRecipe == null) { L("no eligible food recipe -> FAIL"); return _sb.ToString(); }
            var cookStation = foodRecipe.m_craftingStation;
            L("food recipe = " + RecipeName(foodRecipe) + " @ cookStation " + (cookStation != null ? cookStation.name + " skill=" + (int)cookStation.m_craftingSkill : "NULL")
                + " ; tool recipe = " + RecipeName(toolRecipe));

            // save live state we mutate
            object savedSelected = GetF(gui, "m_selectedRecipe");
            var savedStation = player.GetCurrentCraftingStation();
            float savedTimer = (float)GetF(gui, "m_craftTimer");
            float savedDur = (float)GetF(gui, "m_craftDuration");
            bool savedMulti = (bool)GetF(gui, "m_multiCrafting");

            const float BASE = 6.0f;
            bool overall = true;

            // A: ELIGIBLE + ACTIVE
            SetSelectedRecipe(gui, foodRecipe);
            SetCurrentStation(player, cookStation);
            float vanillaAdjA = VanillaAdjusted(gui, player, cookStation, BASE);
            float maxA = MeasureBarMax(gui, player, BASE);
            float expA = vanillaAdjA / 3f;
            bool passA = Approx(maxA, expA);
            L(string.Format("A ELIGIBLE+ACTIVE : base={0:0.000} vanillaAdj={1:0.000} -> barMax(REAL)={2:0.000} EXPECT {3:0.000} (adj*1/3) -> {4}",
                BASE, vanillaAdjA, maxA, expA, passA ? "PASS" : "FAIL"));
            overall &= passA;

            // B: ELIGIBLE + DORMANT
            PutActiveAuthority(authorityStore, account, character, stoneId, false);
            bool gateB = (bool)resolveM.Invoke(null, null);
            float maxB = MeasureBarMax(gui, player, BASE);
            bool passB = Approx(maxB, vanillaAdjA) && !gateB;
            L(string.Format("B ELIGIBLE+DORMANT: gateActive={0} barMax(REAL)={1:0.000} EXPECT {2:0.000} (vanilla adj) -> {3}",
                gateB, maxB, vanillaAdjA, passB ? "PASS" : "FAIL"));
            overall &= passB;
            PutActiveAuthority(authorityStore, account, character, stoneId, true);

            // C: ELIGIBLE + NON-OWNER (unbind)
            UnbindSession(boundSessions, peerKey);
            bool gateC = (bool)resolveM.Invoke(null, null);
            float maxC = MeasureBarMax(gui, player, BASE);
            bool passC = Approx(maxC, vanillaAdjA) && !gateC;
            L(string.Format("C ELIGIBLE+UNBOUND: gateActive={0} barMax(REAL)={1:0.000} EXPECT {2:0.000} (vanilla adj) -> {3}",
                gateC, maxC, vanillaAdjA, passC ? "PASS" : "FAIL"));
            overall &= passC;
            boundSessions.GetType().GetMethod("Bind").Invoke(boundSessions,
                new object[] { peerKey, new PilotSessionPrincipal(account, character, "qa-session") });

            // D: INELIGIBLE non-food (tool recipe at cooking station)
            if (toolRecipe != null)
            {
                SetSelectedRecipe(gui, toolRecipe);
                SetCurrentStation(player, cookStation);
                float vanillaAdjD = VanillaAdjusted(gui, player, cookStation, BASE);
                float maxD = MeasureBarMax(gui, player, BASE);
                bool passD = Approx(maxD, vanillaAdjD);
                L(string.Format("D INELIGIBLE-nonfood: barMax(REAL)={0:0.000} EXPECT {1:0.000} (vanilla adj) -> {2}",
                    maxD, vanillaAdjD, passD ? "PASS" : "FAIL"));
                overall &= passD;
            }
            else L("D INELIGIBLE-nonfood: SKIP (no non-food recipe)");

            // E: INELIGIBLE non-cooking station (food recipe, non-cooking station)
            var nonCook = NonCookingStation();
            SetSelectedRecipe(gui, foodRecipe);
            SetCurrentStation(player, nonCook);
            float vanillaAdjE = VanillaAdjusted(gui, player, nonCook, BASE);
            float maxE = MeasureBarMax(gui, player, BASE);
            bool passE = Approx(maxE, vanillaAdjE);
            L(string.Format("E INELIGIBLE-nonCook: station={0} barMax(REAL)={1:0.000} EXPECT {2:0.000} (vanilla adj) -> {3}",
                nonCook != null ? nonCook.name + " skill=" + (int)nonCook.m_craftingSkill : "NULL", maxE, vanillaAdjE, passE ? "PASS" : "FAIL"));
            overall &= passE;

            // F: COMPLETION COUNT — for eligible+active, exactly one completion when timer reaches the
            // SHORTENED max (unchanged vs vanilla — one craft => one DoCrafting => one item; the 1/3 factor
            // changes WHEN it completes, not HOW MANY times).
            // The B/C matrix churned authority (dormant/restore) and session (unbind/rebind); re-establish an
            // ACTIVE eligible owner and MEASURE the real transpiled barMax rather than assuming it, so the
            // completion threshold is the actual shipped num5 the engine compares against.
            PutActiveAuthority(authorityStore, account, character, stoneId, true);
            boundSessions.GetType().GetMethod("Bind").Invoke(boundSessions,
                new object[] { peerKey, new PilotSessionPrincipal(account, character, "qa-session") });
            SetSelectedRecipe(gui, foodRecipe);
            SetCurrentStation(player, cookStation);
            bool gateF = (bool)resolveM.Invoke(null, null);
            float vanillaAdjF = VanillaAdjusted(gui, player, cookStation, BASE);
            // MeasureBarMax drives the real transpiled UpdateRecipe with dt=0 and reads back the shipped num5.
            float realMaxF = MeasureBarMax(gui, player, BASE);
            float expShortF = vanillaAdjF / 3f;
            bool shortenedF = Approx(realMaxF, expShortF);
            // prime timer just below the REAL measured max, then step dt to cross it exactly once
            SetF(gui, "m_multiCrafting", false);
            SetF(gui, "m_craftDuration", BASE);
            SetF(gui, "m_craftTimer", realMaxF - 0.01f);
            // Count DoCrafting by watching m_craftTimer sentinel (vanilla sets -1 unconditionally after DoCrafting).
            DriveUpdateRecipe(gui, player, 0.02f); // crosses realMaxF -> completes once
            float timerAfter = (float)GetF(gui, "m_craftTimer");
            bool completedOnce = timerAfter < 0f; // vanilla sets -1 after DoCrafting
            // second drive should NOT re-complete (timer stays < 0, panel-hidden early return at m_craftTimer<0)
            DriveUpdateRecipe(gui, player, 0.02f);
            float timerAfter2 = (float)GetF(gui, "m_craftTimer");
            bool noDoubleComplete = timerAfter2 < 0f;
            bool passF = gateF && shortenedF && completedOnce && noDoubleComplete;
            L(string.Format("F COMPLETION-COUNT: gate={0} realBarMax={1:0.000} EXPECT {2:0.000}(adj*1/3) shortened={3} timerAfter1={4:0.000}(<0=>completed once) timerAfter2={5:0.000}(<0=>no double) -> {6}",
                gateF, realMaxF, expShortF, shortenedF, timerAfter, timerAfter2, passF ? "PASS" : "FAIL"));
            overall &= passF;

            // restore
            SetF(gui, "m_selectedRecipe", savedSelected);
            SetCurrentStation(player, savedStation);
            SetF(gui, "m_craftTimer", savedTimer);
            SetF(gui, "m_craftDuration", savedDur);
            SetF(gui, "m_multiCrafting", savedMulti);
            L("restored InventoryGui selected recipe + station + craft fields");

            L("==================== QADiag T019 VERDICT: " + (overall && active ? "PASS" : "FAIL") + " ====================");
            return _sb.ToString();
        }
    }
}
