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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Cartography
{
    public class SurveyorTableTag : MonoBehaviour, Hoverable, Interactable, ICartographyPinEditor
    {
        // Hard 1000 m survey radius (impl spec §1.3 / requirements §1). Fixed by design.
        public const float SurveyRadiusMeters = 1000f;

        // The RPC the owner answers to persist a freshly-merged survey blob into the ZDO.
        // Named distinctly from vanilla MapTable's "MapData" so the two never collide on a
        // shared world. Registered in Awake (same pattern as MapTable.Start).
        private const string RpcSurveyData = "SBPR_SurveyData";

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
            return piece != null && !string.IsNullOrEmpty(piece.m_name) ? piece.m_name : "Surveyor's Table";
        }

        public string GetHoverText()
        {
            // Ward-gate the affordance text just like vanilla MapTable.GetReadHoverText.
            // $piece_noaccess is a VANILLA token (localizes); the rest is plain English +
            // the vanilla $KEY_Use keybind token (CairnInteractable's proven pattern — a
            // CUSTOM $piece_* token would leak as a literal, the 2026-06-05 sign bug).
            if (!PrivateArea.CheckAccess(transform.position, 0f, flash: false))
                return Localize(GetHoverName() + "\n$piece_noaccess");

            // [Use] contributes the local survey + opens the (shared, editable) Table view.
            return Localize(
                GetHoverName() +
                "\n[<color=yellow><b>$KEY_Use</b></color>] Survey here / review the shared map");
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
            ContributeLocalSurvey(user);

            // 1b) IMPRINT (§2A.5): if the surveyor carries a Local Map, snapshot THIS Table's
            //     shared survey + bound-origin onto it (a snapshot, not a live link). Done
            //     after contribute so the imprint includes what this player just added.
            ImprintCarriedLocalMaps(user);

            // 2) OPEN the forked viewer on the SHARED data in pin-removal Table mode (D4).
            //    The render is the downstream card; this degrades gracefully without it.
            var shared = ReadSharedSurvey() ?? new SurveyData();
            CartographyViewer.Open(new MapViewRequest
            {
                Survey = shared,
                BoundOrigin = transform.position,
                RadiusMeters = SurveyRadiusMeters,
                Mode = MapViewerMode.TableEdit,
                PinEditor = this,   // Table view edits; field Local-Map view is handed null
            });
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ── ICartographyPinEditor (pin-removal backend; AT-TABLE-PINEDIT) ──────────────

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
        /// Imprint every blank/old Local Map the surveyor carries with a SNAPSHOT of THIS
        /// Table's shared survey + its bound-origin world coord (§2A.5 — a snapshot, not a
        /// live link). Imprints ALL carried Local Maps so a player can stock several. No-ops
        /// silently if the player carries none or the Table has no survey yet. Ward access is
        /// already gated by the caller (Interact).
        /// </summary>
        private void ImprintCarriedLocalMaps(Humanoid user)
        {
            var player = user as Player;
            var inv = player != null ? player.GetInventory() : null;
            if (inv == null) return;

            var shared = ReadSharedSurvey();
            if (shared == null || shared.IsEmpty)
                return; // nothing surveyed here yet — leave carried maps blank

            int imprinted = 0;
            foreach (var it in inv.GetAllItems())
            {
                if (it?.m_dropPrefab == null) continue;
                bool isMap = it.m_dropPrefab.GetComponent<LocalMapItemTag>() != null
                             || it.m_dropPrefab.name == LocalMap.LocalMapName;
                if (!isMap) continue;

                if (LocalMap.Imprint(it, shared, transform.position))
                    imprinted++;
            }

            if (imprinted > 0)
            {
                user.Message(MessageHud.MessageType.Center,
                    imprinted == 1 ? "Local Map imprinted" : $"{imprinted} Local Maps imprinted");
                Plugin.Log.LogInfo(
                    $"[Trailborne/Cartography] Imprinted {imprinted} Local Map(s) @({transform.position.x:F0},{transform.position.z:F0}) " +
                    $"| survey window {shared.Size}x{shared.Size} | pins={shared.Pins.Count}.");
            }
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
