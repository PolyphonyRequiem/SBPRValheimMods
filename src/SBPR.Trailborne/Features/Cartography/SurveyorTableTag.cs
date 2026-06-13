// ============================================================================
//  Trailborne v2 cartography — Surveyor's Table runtime behaviour
// ----------------------------------------------------------------------------
//  SurveyorTableTag is the custom MonoBehaviour on the additively-built Surveyor's
//  Table piece (impl spec §1). It owns:
//    • the SHARED, cumulative, windowed survey of the Table's own 1000 m disc,
//      persisted compressed in the Table ZDO byte array ZDOVars.s_data — exactly
//      the vanilla MapTable storage shape (decomp MapTable :114014), so save/load
//      across a dedicated-server restart is inherited (AT-TABLE-PERSIST);
//    • the CONTRIBUTE-on-use path: any surveyor using the Table merges THEIR
//      in-disc explored fog + in-disc shareable pins into the shared record,
//      owner-authoritatively via InvokeRPC (same routing vanilla MapTable uses
//      for its "MapData" RPC), cumulative OR-merge, beyond-1000 m dropped (C5,
//      AT-TABLE-SHARED);
//    • ward gating (PrivateArea.CheckAccess) on every read/contribute/edit
//      (AT-TABLE-WARD), reproduced from vanilla MapTable;
//    • the pin-removal BACKEND (ICartographyPinEditor) the forked viewer calls in
//      TableEdit mode (AT-TABLE-PINEDIT);
//    • opening the forked viewer via the CartographyViewer seam (the render itself
//      is the downstream card t_7b616020; this card degrades gracefully without it).
//
//  Owner-write discipline (valheim-mod-development skill): we go through the public
//  ZNetView path — GetComponent<ZNetView>(), IsOwner()/ClaimOwnership(), and an
//  owner-routed InvokeRPC — never poke m_nview (private) or hand-roll ownership.
//
//  Clean-side (ADR-0001): vanilla MapTable read as a blueprint; our wire format +
//  windowing are our own (SurveyData). The personal fog read uses reflection on the
//  private Minimap.m_explored / m_pins (the spike-established idiom for private
//  vanilla fields); these are stable vanilla fields.
//
//  ── ZDO field contracts (save/wire — LOCK on first ship, NEVER rename) ──────────
//    • ZDOVars.s_data (byte[]) — the compressed windowed SurveyData blob (the vanilla
//      MapTable storage slot; documented above).
//    • "SBPR_TableName" (string) — the Table's player-given name (issue 10, §1.6).
//      Owner-write, empty/absent = unnamed. Renaming this key orphans every named
//      Table in a live world (same rule as SBPR_Ink* / SBPR_MarkerType) — DO NOT.
//      Set via the vanilla TextInput rename dialog (this component is the TextReceiver,
//      the Tameable/Sign/Portal mechanism — decomp Tameable :27163, TextInput :54895).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Cartography
{
    public class SurveyorTableTag : MonoBehaviour, Hoverable, Interactable, ICartographyPinEditor, TextReceiver
    {
        // Hard 1000 m survey radius (impl spec §1.3 / requirements §1). Fixed by design.
        public const float SurveyRadiusMeters = 1000f;

        // The RPC the owner answers to persist a freshly-merged survey blob into the ZDO.
        // Named distinctly from vanilla MapTable's "MapData" so the two never collide on a
        // shared world. Registered in Awake (same pattern as MapTable.Start).
        private const string RpcSurveyData = "SBPR_SurveyData";

        // ZDO string key for the Table's player-given name (issue 10, §1.6). Save/wire
        // contract — LOCK, never rename (a rename orphans every named Table). Empty/absent
        // = unnamed. Owner-write via the same ClaimOwnership shape MarkerSignTag.WritePinned
        // uses. Distinct from vanilla MapTable storage; reuses no vanilla key.
        public const string ZdoTableName = "SBPR_TableName";

        // Char limit for the rename dialog — room for a place name (Tameable uses 10 for a
        // pet, Sign uses its own limit; 32 reads comfortably for "Northern Outpost" etc.).
        private const int TableNameCharLimit = 32;

        // Hover-text radius for the use-range guard (cartography table is a big piece).
        private const float UseDistance = 5.0f;

        private ZNetView nview = null!;   // Unity-injected in Awake via GetComponent
        private Piece piece = null!;      // Unity-injected in Awake via GetComponent

        // Reflected once, lazily, from the live Minimap (private vanilla fields).
        private static FieldInfo? _fiExplored;
        private static FieldInfo? _fiPins;

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            piece = GetComponent<Piece>();
            // Register the owner-side persistence RPC exactly as vanilla MapTable.Start does
            // (Register<ZPackage>("MapData", RPC_MapData)). Only the ZDO owner acts on it.
            if (nview != null && nview.IsValid())
                nview.Register<ZPackage>(RpcSurveyData, RPC_SurveyData);
        }

        // ── Hoverable ───────────────────────────────────────────────────────────────

        public string GetHoverName()
        {
            // Plain English (the repo convention — no custom $piece tokens exist; only
            // vanilla tokens like $KEY_Use / $piece_noaccess get localized). Mirrors
            // CairnInteractable / the piece.m_name set in SurveyorsTable.RegisterPrefabs.
            //
            // Issue 10 §1.6.2: when the Table has been named, surface that name + the base
            // type in parens so the hover still reads as a Table (e.g. "Northern Outpost
            // (Surveyor's Table)"). Unnamed → the plain base name.
            string baseName = piece != null && !string.IsNullOrEmpty(piece.m_name) ? piece.m_name : "Surveyor's Table";
            string tableName = GetTableName();
            return string.IsNullOrEmpty(tableName) ? baseName : $"{tableName} ({baseName})";
        }

        public string GetHoverText()
        {
            // Ward-gate the affordance text just like vanilla MapTable.GetReadHoverText.
            // $piece_noaccess is a VANILLA token (localizes); the rest is plain English +
            // the vanilla $KEY_Use keybind token (CairnInteractable's proven pattern — a
            // CUSTOM $piece_* token would leak as a literal, the 2026-06-05 sign bug).
            if (!PrivateArea.CheckAccess(transform.position, 0f, flash: false))
                return Localize(GetHoverName() + "\n$piece_noaccess");

            // Issue 10 §1.6.2/§1.6.4: an UNNAMED Table states the naming gate in its [Use]
            // line, so the player understands binding is blocked until they name it. A named
            // Table shows the survey/review affordance as before.
            if (string.IsNullOrEmpty(GetTableName()))
                return Localize(
                    GetHoverName() +
                    "\n[<color=yellow><b>$KEY_Use</b></color>] Name this table");

            // [Use] contributes the local survey + opens the (shared, editable) Table view.
            // The [1-8] line advertises the §2I.3 imprint gesture (look at the Table + press the
            // hotbar number of the Local Map slot to imprint). Plain English with literal bracketed
            // digits — NOT a $KEY_* token: the hotbar keys are vanilla Hotbar1..8, not a single
            // rebindable Trailborne action, so there is no one keybind token to localize.
            return Localize(
                GetHoverName() +
                "\n[<color=yellow><b>$KEY_Use</b></color>] Survey here / review the shared map" +
                "\n[<color=yellow><b>1-8</b></color>] Imprint that Local Map");
        }

        // ── Interactable ──────────────────────────────────────────────────────────────

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (user == null || user != Player.m_localPlayer) return false;
            if (nview == null || !nview.IsValid()) return false;
            if (Vector3.Distance(user.transform.position, transform.position) > UseDistance + 1.0f) return false;

            // Ward gate — a Table in a ward is read/write-locked to those with access
            // (AT-TABLE-WARD; vanilla MapTable.OnWrite does CheckAccess). flash:true so a
            // denied player sees the ward shield flash, matching vanilla.
            if (!PrivateArea.CheckAccess(transform.position))
            {
                user.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                return true;
            }

            // 1) CONTRIBUTE: merge this surveyor's in-disc fog + pins into the shared record,
            //    persisted owner-authoritatively. Cumulative; beyond-1000 m dropped (C5).
            //    Surveying is NOT name-gated (§1.6.4.3) — an unnamed Table still accumulates
            //    the shared survey; only BINDING maps to the item is gated below.
            ContributeLocalSurvey(user);

            // 1b) NAME GATE (§1.6.4, issue 10): a Table MUST be named before it will bind/
            //     imprint maps. Using an UNNAMED Table launches the vanilla rename dialog
            //     (§1.6.3) instead of opening the viewer — and the §2I.3 hotbar-imprint gesture
            //     is independently name-gated in TryImprintSlot — so imprint NEVER happens while
            //     the name is empty (the hard requirement). This matches the unnamed hover
            //     affordance ("[Use] Name this table", §1.6.2). Once named, a subsequent Use
            //     opens the viewer normally (imprint is the separate hotbar gesture, §2I.4).
            //     (Implementer choice, spec-sanctioned: we always prompt-to-name an unnamed Table
            //     on Use — it keeps the §1.6.2 hover literally true. TryImprintSlot ALSO hard-
            //     guards on the empty name as the per-slot backstop.)
            if (string.IsNullOrEmpty(GetTableName()))
            {
                RequestRename(user);
                return true;
            }

            // 1c) IMPRINT MOVED OFF THE USE PATH (§2I.4, issue 6). Imprint no longer rides Use —
            //     it is now the explicit "hover the Table + press the target map's hotbar number"
            //     gesture (SurveyorTableHotbarImprintPatch → TryImprintSlot), which imprints THAT
            //     ONE map instead of every carried map. Using (E) a named Table still contributes
            //     the survey (above) + opens the viewer (below), but does NOT imprint anything
            //     (AT-IMPRINT-HOTBAR-4). ImprintCarriedLocalMaps is retired; its per-map core is
            //     reused one-slot-at-a-time by TryImprintSlot.

            // 2) OPEN the forked viewer on the SHARED data in pin-removal Table mode (D4),
            //    threading the Table name as the on-screen title (§2B.1). The render is the
            //    downstream viewer; this degrades gracefully without it.
            var shared = ReadSharedSurvey() ?? new SurveyData();
            CartographyViewer.Open(new MapViewRequest
            {
                Survey = shared,
                BoundOrigin = transform.position,
                RadiusMeters = SurveyRadiusMeters,
                Mode = MapViewerMode.TableEdit,
                PinEditor = this,   // Table view edits; field Local-Map view is handed null
                Title = GetTableName(),  // §2B.1 — the Table's name shows as the viewer title
            });
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ── Table naming (§1.6, issue 10) — ZDO name read/write + vanilla rename dialog ──

        /// <summary>
        /// The Table's player-given name from the ZDO, or "" if unnamed / on the ghost.
        /// Censored on read (CensorShittyWords.FilterUGC) exactly as vanilla Tameable.GetText
        /// does (decomp :27181), so a named Table can never display unfiltered UGC even if
        /// the stored bytes predate a censor change. Empty/absent = unnamed.
        /// </summary>
        public string GetTableName()
        {
            if (nview == null || !nview.IsValid()) return string.Empty;
            var zdo = nview.GetZDO();
            if (zdo == null) return string.Empty;
            string stored = zdo.GetString(ZdoTableName, string.Empty);
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            return CensorShittyWords.FilterUGC(stored, UGCType.Text, 0L);
        }

        /// <summary>
        /// Owner-write the Table name (§1.6.1). Claims ownership first — the exact shape
        /// MarkerSignTag.WritePinned / SignTag.WriteColors use — never a raw m_nview poke.
        /// Censors before persisting so the stored bytes are already clean. No-op on the
        /// ghost / no ZDO. Empty input clears the name (back to unnamed).
        /// </summary>
        private void WriteTableName(string name)
        {
            if (nview == null || !nview.IsValid()) return;
            var zdo = nview.GetZDO();
            if (zdo == null) return;
            string clean = string.IsNullOrEmpty(name)
                ? string.Empty
                : CensorShittyWords.FilterUGC(name.Trim(), UGCType.Text, 0L);
            if (!nview.IsOwner()) nview.ClaimOwnership();
            zdo.Set(ZdoTableName, clean);
        }

        /// <summary>
        /// Launch the vanilla rename dialog for this Table (§1.6.3) — the same TextInput
        /// path Tameable/Sign/Portal use (decomp Tameable :27163 → TextInput.RequestText
        /// :54895). This component is the TextReceiver: GetText() feeds the current name into
        /// the field, SetText() owner-writes the typed name + refreshes the hover. Topic uses
        /// the vanilla $hud_rename token (localizes; a custom $piece_* token would leak as a
        /// literal — the 2026-06-05 sign bug). Client act only (TextInput.instance is null on
        /// the dedicated server); a Center message tells the player why binding is blocked.
        /// </summary>
        private void RequestRename(Humanoid user)
        {
            if (TextInput.instance == null) return; // headless / no UI — nothing to show
            user?.Message(MessageHud.MessageType.Center, "Name this table before binding maps");
            TextInput.instance.RequestText(this, "$hud_rename", TableNameCharLimit);
        }

        // ── TextReceiver (the vanilla rename dialog contract; §1.6.3) ─────────────────

        /// <summary>TextReceiver: the current name shown in the rename field (the censored
        /// ZDO value, or "" when unnamed). Mirrors Tameable.GetText.</summary>
        public string GetText() => GetTableName();

        /// <summary>TextReceiver: persist the typed name owner-side. Called by TextInput on
        /// confirm. Mirrors the Tameable/Sign rename-commit shape (owner-write + censor). The
        /// hover refreshes on its own — Hoverable.GetHoverName/GetHoverText are re-read every
        /// frame by the look-at poll, so the renamed Table reads correctly immediately.</summary>
        public void SetText(string text)
        {
            WriteTableName(text);
        }



        /// <summary>
        /// Remove the shared pin closest to <paramref name="worldPos"/> within
        /// <paramref name="radius"/> and persist the edit owner-authoritatively. Re-checks
        /// ward access (never trusts the UI). Returns true if one was removed. Called by the
        /// forked viewer in TableEdit mode; the field Local-Map view never gets this object.
        /// </summary>
        public bool RemovePinNear(Vector3 worldPos, float radius)
        {
            if (nview == null || !nview.IsValid()) return false;
            if (!PrivateArea.CheckAccess(transform.position)) return false;

            var survey = ReadSharedSurvey();
            if (survey == null || survey.Pins.Count == 0) return false;

            if (!survey.RemovePinNear(worldPos, radius)) return false;

            PersistSurvey(survey);
            return true;
        }

        /// <summary>
        /// Re-read the Table's CURRENT shared survey (post-edit) for the viewer to re-render
        /// against after a pin removal (ICartographyPinEditor). Just the existing ZDO read.
        /// </summary>
        public SurveyData? ReadCurrentSurvey() => ReadSharedSurvey();

        // ── Contribute: window the player's fog + pins to the disc, merge, persist ────

        private void ContributeLocalSurvey(Humanoid user)
        {
            var mm = Minimap.instance;
            if (mm == null)
            {
                // Headless/dedicated server has no Minimap; contribution is a client act.
                // (The owner-side persistence still runs via RPC when a client contributes.)
                return;
            }

            bool[]? explored = ReadExplored(mm);
            if (explored == null)
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/Cartography] Could not read Minimap.m_explored; survey contribution skipped this use.");
                return;
            }

            int textureSize = mm.m_textureSize;   // public (:46692)
            float pixelSize = mm.m_pixelSize;     // public (:46694)
            Vector3 origin = transform.position;

            long ownerId = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            var pins = CollectShareablePins(mm, ownerId);

            var contribution = SurveyData.CaptureWindow(
                explored, textureSize, pixelSize, origin.x, origin.z, SurveyRadiusMeters,
                pins, out int exploredInDisc, out int discCells);

            // Merge into whatever the Table already holds (cumulative), then persist.
            var merged = ReadSharedSurvey() ?? new SurveyData();
            merged.MergeFrom(contribution);
            PersistSurvey(merged);

            Plugin.Log.LogInfo(
                $"[Trailborne/Cartography] Survey contributed @({origin.x:F0},{origin.z:F0}) | " +
                $"window {contribution.Size}x{contribution.Size} | exploredInDisc={exploredInDisc}/{discCells} | " +
                $"pins(in-disc)={contribution.Pins.Count} | merged pins={merged.Pins.Count}.");

            user.Message(MessageHud.MessageType.Center, "Survey recorded");
        }

        /// <summary>
        /// §2I.4 (issue 6, Part B): imprint ONE carried Local Map — the item in the hotbar
        /// slot the player pressed while hovering this Table — with a SNAPSHOT of THIS Table's
        /// shared survey + bound-origin + name (§2A.5/§2A.6, mechanism unchanged). Replaces the
        /// retired Use-path <c>ImprintCarriedLocalMaps</c> (which imprinted ALL carried maps at
        /// once and rode the Use press — the auto-imprint ambiguity that fed the Part A latch).
        ///
        /// Called only by <see cref="SurveyorTableHotbarImprintPatch"/>, which has already gated
        /// "local player" + "hovering this Table". Every reached path returns <c>true</c>
        /// (HANDLED) so the caller consumes the hotbar press: while looking at a Table the hotbar
        /// keys ARE the imprint gesture, so the slot item is never also used/equipped by vanilla
        /// (AT-IMPRINT-HOTBAR-2). Each refusal still gives Center-message feedback — no silent
        /// no-op. Returns <c>false</c> only if there is no local player (defensive; the caller
        /// won't reach here on a dedicated server).
        ///
        /// Refusals (each a Center message): no ward access → vanilla <c>$piece_noaccess</c>;
        /// unnamed Table → "Name this table before binding maps" (the §1.6.4 bind gate, now
        /// enforced per-slot); empty / non-Local-Map slot → "Hold a Local Map in that slot to
        /// imprint it"; nothing surveyed yet → "This table has nothing surveyed yet". Success →
        /// "Local Map imprinted: &lt;table name&gt;".
        /// </summary>
        public bool TryImprintSlot(ItemDrop.ItemData? item)
        {
            var user = Player.m_localPlayer;
            if (user == null) return false; // no local player (dedicated server) — let vanilla run
            if (nview == null || !nview.IsValid()) return false;

            // Ward RE-CHECK — never trust the gesture/UI to have gated (the hover text is
            // advisory). flash:true so a denied player sees the ward shield flash (vanilla parity).
            if (!PrivateArea.CheckAccess(transform.position))
            {
                user.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                return true;
            }

            // §1.6.4 NAME GATE (hard, unchanged): never bind/imprint while the Table name is
            // empty. Previously a backstop inside ImprintCarriedLocalMaps; now the per-slot gate.
            string tableName = GetTableName();
            if (string.IsNullOrEmpty(tableName))
            {
                user.Message(MessageHud.MessageType.Center, "Name this table before binding maps");
                return true;
            }

            // The pressed slot must hold one of OUR Local Maps (blank or already imprinted).
            // An empty slot resolves to null upstream (Inventory.GetItemAt) → same refusal.
            if (!IsLocalMap(item))
            {
                user.Message(MessageHud.MessageType.Center, "Hold a Local Map in that slot to imprint it");
                return true;
            }

            // The Table must actually have something surveyed to imprint.
            var shared = ReadSharedSurvey();
            if (shared == null || shared.IsEmpty)
            {
                user.Message(MessageHud.MessageType.Center, "This table has nothing surveyed yet");
                return true;
            }

            // Success — snapshot THIS Table's survey + bound-origin + name onto the one map
            // (§2A.5/§2A.6 — identical Imprint call the retired all-maps path used).
            if (LocalMap.Imprint(item!, shared, transform.position, tableName))
            {
                user.Message(MessageHud.MessageType.Center, $"Local Map imprinted: {tableName}");
                Plugin.Log.LogInfo(
                    $"[Trailborne/Cartography] Local Map imprinted @({transform.position.x:F0},{transform.position.z:F0}) " +
                    $"| table '{tableName}' | survey window {shared.Size}x{shared.Size} | pins={shared.Pins.Count}.");
            }
            else
            {
                // LocalMap.Imprint already logged the serialize failure; surface it rather than
                // swallow, and still consume the press (we're in the imprint gesture context).
                user.Message(MessageHud.MessageType.Center, "Could not imprint that Local Map");
            }
            return true;
        }

        /// <summary>
        /// True if the item is one of OUR Local Maps — a component tag on its drop prefab, or the
        /// locked prefab name (rename-proof). Mirrors LocalMapController.IsLocalMap /
        /// LocalMapEquipPatch.IsLocalMap (the same idiom; each cartography class keeps its own
        /// 3-line copy rather than introducing a shared util for one predicate).
        /// </summary>
        private static bool IsLocalMap(ItemDrop.ItemData? item)
        {
            if (item?.m_dropPrefab == null) return false;
            return item.m_dropPrefab.GetComponent<LocalMapItemTag>() != null
                   || item.m_dropPrefab.name == LocalMap.LocalMapName;
        }

        /// <summary>
        /// Snapshot the player's saveable, non-Death pins as SurveyPins (the same subset
        /// vanilla GetSharedMapData serializes: m_save && type != Death). Disc-clipping
        /// happens in SurveyData.CaptureWindow, so we pass all candidates here.
        /// </summary>
        private static List<SurveyPin> CollectShareablePins(Minimap mm, long ownerId)
        {
            var result = new List<SurveyPin>();
            var pins = ReadPins(mm);
            if (pins == null) return result;

            foreach (var pin in pins)
            {
                if (pin == null) continue;
                if (!pin.m_save) continue;
                if (pin.m_type == Minimap.PinType.Death) continue;
                long pinOwner = pin.m_ownerID != 0L ? pin.m_ownerID : ownerId;
                result.Add(new SurveyPin(pin.m_name, (int)pin.m_type, pin.m_pos, pin.m_checked, pinOwner));
            }
            return result;
        }

        // ── ZDO persistence (compressed blob in ZDOVars.s_data, vanilla MapTable shape) ─

        /// <summary>Read + decompress + deserialize the Table's shared survey, or null if none.</summary>
        public SurveyData? ReadSharedSurvey()
        {
            if (nview == null || !nview.IsValid()) return null;
            var zdo = nview.GetZDO();
            if (zdo == null) return null;

            byte[] stored = zdo.GetByteArray(ZDOVars.s_data);
            if (stored == null || stored.Length == 0) return null;
            try
            {
                byte[] raw = Utils.Decompress(stored);
                return SurveyData.Deserialize(raw);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] ReadSharedSurvey decompress/deserialize failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Persist a survey to the Table ZDO owner-authoritatively. If we already own the
        /// ZDO we write directly; otherwise we route the compressed blob to the owner via
        /// InvokeRPC (vanilla MapTable.OnWrite → InvokeRPC("MapData", …) pattern), and the
        /// owner's RPC_SurveyData does the actual ZDO.Set. Either way exactly one owner write
        /// lands, so concurrent surveyors don't clobber each other's merge.
        /// </summary>
        private void PersistSurvey(SurveyData survey)
        {
            if (nview == null || !nview.IsValid()) return;
            byte[] compressed = Utils.Compress(survey.Serialize());

            if (nview.IsOwner())
            {
                nview.GetZDO().Set(ZDOVars.s_data, compressed);
            }
            else
            {
                // Route to the owner; RPC_SurveyData (owner-only) commits it. This is the
                // vanilla MapTable cross-client write path, windowed to our blob.
                nview.InvokeRPC(RpcSurveyData, new ZPackage(compressed));
            }
        }

        /// <summary>
        /// Owner-side persistence RPC. Mirrors vanilla MapTable.RPC_MapData: only the ZDO
        /// owner writes, so a non-owner contributor's blob lands through exactly one owner.
        /// The blob arrives already compressed (as PersistSurvey sent it), so we store it verbatim.
        /// </summary>
        private void RPC_SurveyData(long sender, ZPackage pkg)
        {
            if (nview == null || !nview.IsOwner()) return;
            if (pkg == null) return;
            byte[] compressed = pkg.GetArray();
            if (compressed == null || compressed.Length == 0) return;
            nview.GetZDO().Set(ZDOVars.s_data, compressed);
        }

        // ── Reflection helpers for the private vanilla Minimap fog/pin fields ──────────

        private static bool[]? ReadExplored(Minimap mm)
        {
            if (_fiExplored == null)
                _fiExplored = typeof(Minimap).GetField("m_explored", BindingFlags.Instance | BindingFlags.NonPublic);
            return _fiExplored?.GetValue(mm) as bool[];
        }

        private static List<Minimap.PinData>? ReadPins(Minimap mm)
        {
            if (_fiPins == null)
                _fiPins = typeof(Minimap).GetField("m_pins", BindingFlags.Instance | BindingFlags.NonPublic);
            return _fiPins?.GetValue(mm) as List<Minimap.PinData>;
        }

        private static string Localize(string raw)
            => Localization.instance != null ? Localization.instance.Localize(raw) : raw;
    }
}
