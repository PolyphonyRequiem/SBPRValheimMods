// ============================================================================
//  Trailborne v2 cartography — the windowed survey payload + serialization
// ----------------------------------------------------------------------------
//  SurveyData is the in-memory form of the windowed-fog blob (impl spec §2C/§4):
//  a Size×Size bool fog window at the native pixel resolution + the bound-origin
//  world coordinate + the in-disc shareable pin list. ONE format serves the
//  Surveyor's Table (this card, the SHARED cumulative survey persisted in the
//  Table ZDO), the Local Map snapshot, and the forked viewer (both t_7b616020).
//
//  Persistence shape mirrors vanilla MapTable exactly (decomp MapTable :114014):
//  the Table stores `Utils.Compress(Serialize())` in its ZDO byte array
//  ZDOVars.s_data, and the owner round-trips it on write — so save/load across a
//  dedicated-server restart is inherited, just like vanilla. The DIFFERENCE from
//  vanilla GetSharedMapData/AddSharedMapData (:48754/:48823) is the WINDOW: vanilla
//  serializes the full 256² world array; we serialize only the ~33×33 disc window
//  (impl spec C5 — a Table is a locally-bounded survey, not a global map).
//
//  Clean-side (ADR-0001): wire format is our own; the cell math is vanilla-faithful
//  (BoundedMapMath). No vanilla blob is fed to AddSharedMapData (it expects 256²).
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// A single shareable surveyed pin, in world space. Captures the subset of
    /// vanilla <see cref="Minimap.PinData"/> that is persisted in a shared survey
    /// (name / type / world pos / checked / owner), per vanilla's own saved-pin set
    /// (GetSharedMapData :48754 writes ownerID, name, pos, type, checked, author).
    /// Death pins and non-saved pins are excluded at capture time, matching vanilla.
    /// </summary>
    public struct SurveyPin
    {
        public string Name;
        public int Type;        // (int)Minimap.PinType — stored as int for forward-safety
        public Vector3 Pos;     // world position (X,Z used for disc clip; Y carried as authored)
        public bool Checked;
        public long OwnerId;    // pin author's player id (0 = unknown)

        public SurveyPin(string name, int type, Vector3 pos, bool isChecked, long ownerId)
        {
            Name = name ?? string.Empty;
            Type = type;
            Pos = pos;
            Checked = isChecked;
            OwnerId = ownerId;
        }
    }

    /// <summary>
    /// The windowed survey of one Surveyor's Table's 1000 m disc: a fog window at
    /// native resolution + its bound-origin world coord + the in-disc pin list.
    /// Cumulative — <see cref="MergeFrom"/> OR-merges another survey's fog and unions
    /// its pins (AT-TABLE-SHARED). Serializes to a versioned, compact byte[] suitable
    /// for <c>Utils.Compress</c> into the Table ZDO (the §2C windowed format).
    /// </summary>
    public class SurveyData
    {
        // ── Wire format version (bump + branch on read if the layout ever changes) ──
        public const int WireVersion = 1;

        // Bound-origin world coordinate (the Table's position at imprint/first-write).
        public float OriginX;
        public float OriginZ;

        // The native-resolution grid this window was cut from. Stored so a consumer
        // (viewer / merge) can detect a grid-mismatch instead of silently mis-aligning
        // if a future game patch ever changed m_pixelSize / m_textureSize.
        public float PixelSize;
        public int   TextureSize;
        public float RadiusMeters;

        // Square fog window: Size×Size, row-major (index = wy*Size + wx). true = lit.
        public int    Size;
        public bool[] Fog;

        // In-disc shareable pins (world space).
        public List<SurveyPin> Pins;

        public SurveyData()
        {
            Pins = new List<SurveyPin>();
            Fog = Array.Empty<bool>();
        }

        /// <summary>True if no fog cells and no pins — an unwritten Table.</summary>
        public bool IsEmpty => (Fog == null || Fog.Length == 0) && (Pins == null || Pins.Count == 0);

        /// <summary>
        /// Build a fresh windowed survey from the live full-world fog at the given bound
        /// origin. This is the "contribute" capture: window <paramref name="explored"/>
        /// to the disc (BoundedMapMath) and clip <paramref name="candidatePins"/> to the
        /// disc. Returns a self-contained SurveyData ready to merge into the Table's record.
        /// </summary>
        public static SurveyData CaptureWindow(bool[] explored, int textureSize, float pixelSize,
                                               float originX, float originZ, float radiusMeters,
                                               IEnumerable<SurveyPin> candidatePins,
                                               out int exploredInDisc, out int discCells)
        {
            var w = BoundedMapMath.ComputeWindow(originX, originZ, radiusMeters, pixelSize, textureSize);
            var fog = BoundedMapMath.BuildWindowedFog(
                explored, textureSize, w, originX, originZ, radiusMeters, pixelSize,
                out exploredInDisc, out discCells);

            var sd = new SurveyData
            {
                OriginX = originX,
                OriginZ = originZ,
                PixelSize = pixelSize,
                TextureSize = textureSize,
                RadiusMeters = radiusMeters,
                Size = w.Size,
                Fog = fog,
            };

            if (candidatePins != null)
            {
                foreach (var p in candidatePins)
                {
                    if (BoundedMapMath.InDisc(p.Pos.x, p.Pos.z, originX, originZ, radiusMeters))
                        sd.Pins.Add(p);
                }
            }
            return sd;
        }

        /// <summary>
        /// Cumulatively OR-merge another survey of the SAME disc into this one
        /// (AT-TABLE-SHARED). Fog cells become lit if EITHER survey has them lit (never
        /// cleared). Pins are unioned with de-duplication by (Type, ~1 m proximity, Name)
        /// so two surveyors writing the same landmark don't stack duplicates. Beyond-1000 m
        /// is impossible here because both windows are already disc-clipped at capture (C5).
        ///
        /// Grid/origin must match (same Table). If they don't (different bound origin or a
        /// grid-resolution change), the merge is refused and logged — silently OR-ing
        /// mismatched windows would corrupt the survey.
        ///
        /// This overload discards the change flag; it preserves the original signature for the
        /// Table contribute path (<see cref="SurveyorTableTag"/>), which always persists.
        /// </summary>
        public bool MergeFrom(SurveyData other) => MergeFrom(other, out _);

        /// <summary>
        /// Change-reporting overload (live-update-cartography-impl-spec §2.3): merges as above
        /// AND sets <paramref name="changed"/> true iff anything actually flipped — a fog cell
        /// going false→true, a NEW pin added, or the empty-adopt path running. This is the
        /// DIRTY-CHECK the live field WRITE axis (§2/§3) uses to skip the reserialize+rewrite of a
        /// carried map's blob when re-covering known ground (or standing still): the steady state
        /// then costs zero writes. Return value is "did the merge RUN" (grid matched); the out-param
        /// is "did the survey CONTENT change."
        /// </summary>
        public bool MergeFrom(SurveyData other, out bool changed)
        {
            changed = false;
            if (other == null || other.IsEmpty) return false;

            // If THIS is empty, adopt the other's grid wholesale (first write to a blank record).
            if (IsEmpty)
            {
                OriginX = other.OriginX; OriginZ = other.OriginZ;
                PixelSize = other.PixelSize; TextureSize = other.TextureSize;
                RadiusMeters = other.RadiusMeters;
                Size = other.Size;
                Fog = (bool[])other.Fog.Clone();
                Pins = new List<SurveyPin>(other.Pins);
                changed = true;   // adopted everything — that's a change (§2.3)
                return true;
            }

            // Both non-empty: the windows must describe the same disc at the same grid. Grid-CELL
            // equality (BoundedMapMath.SameOriginCell) — NOT raw-coordinate proximity — is the
            // OR-merge-alignment invariant (§5): two origins in the same native cell yield identical
            // window geometry (same OriginCellX/Y + Size via ComputeWindow), so the fog arrays align
            // index-for-index. This is what lets a Surveyor's Table rebuilt a few metres off but in
            // the SAME 64 m cell re-adopt its bound maps (AT-INGEST-REBUILD) where the prior raw
            // 0.5 m proximity test would have refused. The contribute path is unaffected (it always
            // re-captures at the exact same Table transform, so the origins are bit-identical).
            if (Size != other.Size ||
                TextureSize != other.TextureSize ||
                Mathf.Abs(PixelSize - other.PixelSize) > 1e-3f ||
                !BoundedMapMath.SameOriginCell(OriginX, OriginZ, other.OriginX, other.OriginZ, PixelSize, TextureSize))
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/Cartography] SurveyData.MergeFrom refused: window mismatch " +
                    $"(this {Size}px @({OriginX:F0},{OriginZ:F0}) grid {TextureSize}/{PixelSize}; " +
                    $"other {other.Size}px @({other.OriginX:F0},{other.OriginZ:F0}) grid {other.TextureSize}/{other.PixelSize}). " +
                    "Surveys of different discs/grids are never OR-merged.");
                return false;
            }

            if (Fog == null || Fog.Length != Size * Size) Fog = new bool[Size * Size];
            // Flip-detecting OR via the pure BoundedMapMath helper (§2.3) — the shipped dirty-check
            // IS the headless-tested one (no second copy to drift).
            BoundedMapMath.OrMergeFog(Fog, other.Fog, out bool fogChanged);
            if (fogChanged) changed = true;

            foreach (var p in other.Pins)
                if (AddOrUpdatePin(p)) changed = true;   // a NEW pin (folded near-dupes don't dirty)

            return true;
        }

        /// <summary>
        /// Add a pin to the survey, or fold it into an existing near-duplicate (same type,
        /// within ~1 m, same name) so repeated writes don't stack. A merge keeps the
        /// "checked" state sticky (checked wins) so a surveyor marking a landmark done
        /// propagates. Returns true if a NEW pin was added (false = folded into existing).
        /// </summary>
        public bool AddOrUpdatePin(SurveyPin pin)
        {
            const float proximity2 = 1.0f * 1.0f;
            for (int i = 0; i < Pins.Count; i++)
            {
                var e = Pins[i];
                if (e.Type != pin.Type) continue;
                float dx = e.Pos.x - pin.Pos.x, dz = e.Pos.z - pin.Pos.z;
                if (dx * dx + dz * dz > proximity2) continue;
                if (!string.Equals(e.Name ?? "", pin.Name ?? "", StringComparison.Ordinal)) continue;
                // Near-duplicate: keep sticky checked state, leave position/owner as first-writer.
                if (pin.Checked && !e.Checked) { e.Checked = true; Pins[i] = e; }
                return false;
            }
            Pins.Add(pin);
            return true;
        }

        /// <summary>
        /// Remove the pin closest to <paramref name="worldPos"/> within
        /// <paramref name="radius"/> (Table-view pin editing, AT-TABLE-PINEDIT). Operates
        /// on the Table's SHARED pin list, NOT the player's global m_pins — the field
        /// Local-Map view is read-only and never calls this. Returns true if one was removed.
        /// </summary>
        public bool RemovePinNear(Vector3 worldPos, float radius)
        {
            int best = -1;
            float bestD2 = radius * radius;
            for (int i = 0; i < Pins.Count; i++)
            {
                if (IsSystemPin(Pins[i].Type)) continue;  // §2K.8: system pins (Boss/Hildir) are non-deletable
                float dx = Pins[i].Pos.x - worldPos.x, dz = Pins[i].Pos.z - worldPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 <= bestD2) { bestD2 = d2; best = i; }
            }
            if (best < 0) return false;
            Pins.RemoveAt(best);
            return true;
        }

        /// <summary>
        /// Group-1 system pins (vanilla Boss + Hildir1-3) are pulled into the survey but must NOT be
        /// player-deletable (Daniel, 2026-06-24 / card t_5c3944cd, §2K.8). They are skipped as deletion
        /// CANDIDATES in <see cref="RemovePinNear"/> so the Table-edit eraser still removes the nearest
        /// *player* pin instead of being blocked by an adjacent system pin. PinType is a collision-free
        /// discriminator: vanilla pin-UI only ever places Icon0-4, so a Boss/Hildir type is unambiguous.
        /// Promoted private→internal (card t_2110193e / §2N.3) so the live system-pin collector
        /// (SystemPins.Collect) reuses this SINGLE discriminator — the live-derive filter and the §2K.8
        /// delete-guard can never drift on "what is a system pin." No logic change.
        /// </summary>
        internal static bool IsSystemPin(int type)
        {
            var pt = (Minimap.PinType)type;
            return pt == Minimap.PinType.Boss
                || pt == Minimap.PinType.Hildir1
                || pt == Minimap.PinType.Hildir2
                || pt == Minimap.PinType.Hildir3;
        }

        // ── Serialization (ZPackage; compressed by the caller into the Table ZDO) ───

        /// <summary>
        /// Serialize to a versioned byte[] via ZPackage. Layout:
        ///   int    WireVersion
        ///   float  OriginX, OriginZ
        ///   float  PixelSize ; int TextureSize ; float RadiusMeters
        ///   int    Size
        ///   int    fogLen ; (fogLen × bool) row-major fog
        ///   int    pinCount ; per pin: long ownerId, string name, Vector3 pos, int type, bool checked
        /// The caller wraps this in Utils.Compress before the ZDO write (vanilla MapTable
        /// pattern), so the on-wire blob is compact even for a fully-lit 1089-cell disc.
        /// </summary>
        public byte[] Serialize()
        {
            var pkg = new ZPackage();
            pkg.Write(WireVersion);
            pkg.Write(OriginX);
            pkg.Write(OriginZ);
            pkg.Write(PixelSize);
            pkg.Write(TextureSize);
            pkg.Write(RadiusMeters);
            pkg.Write(Size);

            int fogLen = Fog?.Length ?? 0;
            pkg.Write(fogLen);
            for (int i = 0; i < fogLen; i++)
                pkg.Write(Fog![i]);

            int pinCount = Pins?.Count ?? 0;
            pkg.Write(pinCount);
            for (int i = 0; i < pinCount; i++)
            {
                var p = Pins![i];
                pkg.Write(p.OwnerId);
                pkg.Write(p.Name ?? string.Empty);
                pkg.Write(p.Pos);
                pkg.Write(p.Type);
                pkg.Write(p.Checked);
            }
            return pkg.GetArray();
        }

        /// <summary>
        /// Deserialize a survey blob produced by <see cref="Serialize"/> (after the caller
        /// has Utils.Decompress'd it). Returns null on a version we don't understand or a
        /// malformed package, so the caller can treat it as "no survey yet" rather than crash.
        /// </summary>
        public static SurveyData? Deserialize(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return null;
            try
            {
                var pkg = new ZPackage(raw);
                int ver = pkg.ReadInt();
                if (ver != WireVersion)
                {
                    Plugin.Log.LogWarning(
                        $"[Trailborne/Cartography] SurveyData.Deserialize: unknown wire version {ver} " +
                        $"(expected {WireVersion}); treating as no survey.");
                    return null;
                }

                var sd = new SurveyData
                {
                    OriginX = pkg.ReadSingle(),
                    OriginZ = pkg.ReadSingle(),
                    PixelSize = pkg.ReadSingle(),
                    TextureSize = pkg.ReadInt(),
                    RadiusMeters = pkg.ReadSingle(),
                    Size = pkg.ReadInt(),
                };

                int fogLen = pkg.ReadInt();
                sd.Fog = new bool[fogLen];
                for (int i = 0; i < fogLen; i++)
                    sd.Fog[i] = pkg.ReadBool();

                int pinCount = pkg.ReadInt();
                for (int i = 0; i < pinCount; i++)
                {
                    long ownerId = pkg.ReadLong();
                    string name = pkg.ReadString();
                    Vector3 pos = pkg.ReadVector3();
                    int type = pkg.ReadInt();
                    bool isChecked = pkg.ReadBool();
                    sd.Pins.Add(new SurveyPin(name, type, pos, isChecked, ownerId));
                }
                return sd;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] SurveyData.Deserialize failed: {e.Message}");
                return null;
            }
        }
    }
}
