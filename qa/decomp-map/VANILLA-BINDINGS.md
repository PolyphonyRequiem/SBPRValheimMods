---
title: "QA-SPIKE — Vanilla method/decomp map for safe harness bindings (ADR-0009 M2–M4)"
status: accepted
---

# Vanilla method / decomp map for safe harness bindings

**Card:** `t_6783ea57` ([QA-SPIKE] DECIDE, no implementation/runtime).
**Scope:** map the exact vanilla Valheim methods/fields/signatures the
`SBPR.QaHarness.T022` helper needs for ADR-0009 milestones **M2** (fixtures +
cleanup), **M3** (actions + observation + transfer + tamper), and **M4**
(adversarial + evidence hardening). Vanilla decompiled source is allowed
(ADR-0001: read/adapt the game we mod); no other-mod source was read or copied;
**no decompiled game source is committed** — only behavioral signatures and
natural-language notes are recorded here.

This is a DIRTY-side static RE artifact. It states **what the vanilla code does**
and **which public/private seams exist**, so the CLEAN side can build bounded
adapters from this description without reading IronGate source. It is a *binding
pin*, not implementation: every named binding is authorized only to the extent
ADR-0009 already authorizes the corresponding milestone card.

---

## 1. Assembly identity (the pins)

Two distinct Valheim builds matter, because the same `SBPR.QaHarness.T022`
assembly loads role=`Client` on the two GUI clients and role=`Server` on the
headless dedicated server (ADR-0009 §2). GUI-only types (`InventoryGui`,
tooltip surfaces) exist in **both** managed assemblies at the metadata level
(the server build ships the same `assembly_valheim.dll` type set), but the
GUI-driven *runtime* seams (`InventoryGui.instance`, `Player.m_localPlayer`) are
only live in a GUI process — see §3 threading/lifecycle notes.

| Build | Path (local, this host) | SHA-256 | MVID | Game version | net |
|---|---|---|---|---|---|
| **Client (Trailborne-Modded GUI)** | `~/.local/share/Trailborne/Valheim-Modded/valheim_Data/Managed/assembly_valheim.dll` | `ae98afc3a65ccb2e6c744397bb692287cf2c1527877d002e90307a33f3d917ee` | `23db560f-3f87-4454-8fe1-c434da4f936a` | `0.221.12` (net `36`) | net48 |
| **Server (dedicated, niflheim dl)** | `~/valheim/niflheim/data/dl/server/valheim_server_Data/Managed/assembly_valheim.dll` | `f26465c6c5b8d1883deac13a1d001054a5f5aedd84fb54644d3fbb36550564ba` | `62393fbd-383b-447c-9ae7-7ae16afa654f` | `0.221.12` (net `36`) | net48 |

- **Marketing/file version vs. runtime string.** The `0.221.12` above is the
  `GameVersion.CurrentVersion` compile-time constant. At **runtime**, vanilla
  `Version.GetVersionString()` prepends a *platform prefix* from
  `Version.GetPlatformPrefix()` (`"l"` SteamLinux, `"dl"` SteamDeckNative,
  `"dw"` SteamDeckProton, `"ms"` MicrosoftStore; empty otherwise) — the branch
  is resolved from `IDistributionPlatform.DistributionPlatform` at runtime, so
  decompiling the method reveals only the *algorithm*, not which prefix a given
  process emits. The **dedicated server** on this host was **observed live** to
  report **`l-0.221.12`** in its own log
  (`Valheim version: l-0.221.12 (network version 36)`), so `AssemblyDriftGuard`
  pins that platform-prefixed runtime string explicitly (row
  `server-dedicated-niflheim-dl-linux`) rather than stripping the prefix inside
  the fail-closed gate. **No equivalent client pin is authorized**: no launched
  client `Player.log` exists on this host, so the client's runtime prefix has
  not been observed — inferring it from the decompiled algorithm would be
  inference minted into a fail-closed gate. The client-linux pin is added later,
  for free, from the first live M6 run's own client log.

- **Version constants** (`Version` type, both builds identical):
  `GameVersion.CurrentVersion = new GameVersion(0, 221, 12)`,
  `m_networkVersion = 36u`, `m_playerVersion = 43`, `m_worldVersion = 37`.
- The client and server DLLs are **different bytes / different MVID** but expose
  the **same signatures** for every binding below (verified per-binding in §3).
  Signature drift, not byte identity, is the thing the harness must pin — see
  the drift probe in §6.
- A third publicized copy exists at
  `~/valheim/mcp-harness/ValBridgeServer/obj/Release/publicized/assembly_valheim.dll`
  (`6c2df08236dea53dda4647bdd353f115e72dcfab8ccdc9a33f1d15e81a76b822`). It is a
  **publicized** variant (private members exposed) used by ValBridge; the harness
  must **not** depend on a publicized DLL for its own build — it links the real
  SDK and reaches privates (if ever needed) via Harmony/reflection, not a
  publicizer. Recorded here only so a reviewer can tell the copies apart.

**Tool used:** ICSharpCode ilspycmd 8.2.0.7535 (`DOTNET_ROLL_FORWARD=Major` on the
net8 SDK). MVID/version read via a throwaway `Assembly.LoadFile(...).ManifestModule`
probe. No decompiled output is committed.

---

## 2. Binding-need → milestone map (what each M-card consumes)

| ADR-0009 need | Milestone | Primary vanilla seam(s) | Section |
|---|---|---|---|
| Exact world UID + name (arming gate §5.1) | M2 (used from M0/M1 gate onward) | `ZNet.GetWorldUID()`, `ZNet.GetWorldName()`, `ZNet.World.m_uid/m_name` | §3.1 |
| Main-thread scheduling (single-slot dispatcher §3.2) | M1/M2 | `MonoBehaviour.Update()` pump on the helper's own component; no shared game lock | §3.2 |
| Client loopback handoff boundary (no ScriptTools re-entry §5.2) | M1 | `Terminal.TryRunCommand` = the lock to AVOID; helper uses its own component | §3.3 |
| `ZNet.OnNewConnection` / direct per-peer ZRpc (server channel §2/§3.2) | M1/M2 | `ZNet.OnNewConnection(ZNetPeer)`, `ZNetPeer.m_rpc`, `ZRpc.Register/Invoke` | §3.4 |
| Additive station/material fixture creation + cleanup (M2 §4) | M2 | `ZNetScene.GetPrefab`, `ObjectDB.GetItemPrefab`, `ItemDrop.DropItem`, `CraftingStation` | §3.5 |
| Local craft/upgrade through `InventoryGui.DoCrafting` (M3 §3.1) | M3 | `InventoryGui.DoCrafting` (private), `OnCraftPressed` (private), `SetRecipe`, `Player.GetCurrentCraftingStation` | §3.6 |
| Drop/pickup same-item continuity (M3 transfer) | M3 | `Humanoid.DropItem`, `ItemDrop.DropItem`, `Humanoid.Pickup`, `ItemDrop.Pickup` | §3.7 |
| Tooltip observation without ScriptTools/Terminal lock (M3 §3) | M3 | `ItemDrop.ItemData.GetTooltip(...)` (pure string build) | §3.8 |
| Controlled custom-data tamper (M3 §4) | M3 | `ItemDrop.ItemData.m_customData`, `ItemDrop.SaveToZDO/LoadFromZDO` | §3.9 |
| Observation reads (inventory/item/world) | M3 | `Player.GetInventory`, `Inventory.GetItem`, `ZNet.GetWorldName/Uid` | §3.10 |

Every binding below is a **read/observe or a bounded vanilla action** the game
itself performs when a player acts. **None** of them mint/sign/grant product
state — that firewall (ADR-0009 §4) is unaffected by this map.

---

## 3. Pinned signatures, lifecycle, failure modes, adapters

Line numbers are against the ilspycmd decompile of the **client** build
(`23db560f…`) unless noted; the server build carries the same members at the
lines noted in §3.x where they differ. All signatures verified present in both
builds unless flagged **client-only-live**.

### 3.1 World UID / name (arming gate — the load-bearing pin)

```
// ZNet (MonoBehaviour). client ZNet.cs / server srv_ZNet.cs
public static World World { get; }        // ZNet.World  => private static World m_world
public long   GetWorldUID();              // returns m_world.m_uid      (client 1792 / server 1784)
public string GetWorldName();             // returns m_world.m_name (null-guarded) (client 1798 / server 1789)
public bool   IsServer();                 // returns m_isServer         (client 1956 / server 1948)
public static long GetUID();              // == ZDOMan.GetSessionID()   (client 1787) — SESSION id, NOT world id

// World (plain class). World.cs
public long   m_uid;      // = name.GetStableHashCode() + Utils.GenerateUID()  (field @26)
public string m_name;     // world display/file name                          (field @20)
public string m_seedName; // (@22)   public int m_seed; (@24)
```

- **UID semantics (critical):** `World.m_uid` is
  `name.GetStableHashCode() + Utils.GenerateUID()` — a per-world durable id set
  at world creation, **not** derived from name alone. This is exactly why
  ADR-0009 §5.1 requires **UID *and* name** (name is spoofable/reusable; UID is
  the durable disposable-world identity). `ZNet.GetUID()` is the **session** id
  (`ZDOMan.GetSessionID()`) and must **not** be confused with the world id — do
  not use `GetUID()` for the arming gate.
- **Production deny pins:** ADR-0009 hard-denies Niflheim `2456` / Heistan
  `2466`. Those are **server port** numbers (ADR text), not `World.m_uid`s. The
  gate must therefore deny on **both** axes: (a) exact production world UID+name
  if known, and (b) the production endpoint/port when the helper can see it. This
  map cannot pin the two production worlds' `m_uid` values without reading their
  save headers (out of scope, and reading a production world save is off-limits);
  **BLOCK-worthy only if** the gate design later requires the literal production
  `m_uid` — see §5. The port-based deny is fully specified and needs no UID.
- **Lifecycle:** `m_world` is non-null only after `ZNet` has loaded/received a
  world. On a joining client it is populated in `RPC_PeerInfo` (client reads
  `m_world.m_name/m_seed/m_uid` from the PeerInfo package, ZNet.cs ~934). On the
  server it is set at world load. **Arming must therefore happen after world
  load**, not at `Awake` — the M0 skeleton correctly does nothing at `Awake`.
- **Failure modes:** `GetWorldName()` is null-guarded (returns safely when
  `m_world == null`); `GetWorldUID()` is **not** guarded and will NRE if called
  before world load. Adapter must null-check `ZNet.instance` and `ZNet.World`
  before reading UID.
- **Recommended adapter:** `bool TryReadWorldIdentity(out long uid, out string name)`
  — returns false unless `ZNet.instance != null && ZNet.World != null`; reads
  `GetWorldUID()`/`GetWorldName()`. Public API only; no reflection needed.

### 3.2 Main-thread scheduling (single-slot dispatcher)

- Valheim has **no general-purpose main-thread task queue** exposed publicly. The
  vanilla pattern is: work runs inside a `MonoBehaviour`'s `Update`/`FixedUpdate`/
  coroutine on the Unity main thread. `ZNet` itself is
  `public class ZNet : MonoBehaviour` with private `Update()` (ZNet.cs 1055),
  `FixedUpdate()` (1050), `LateUpdate()` (1084), and uses
  `StartCoroutine(...)` (e.g. ZNet.cs 542, 1210).
- **Adapter (ADR-0009 §3.2 single-slot dispatcher):** the helper owns **its own**
  `MonoBehaviour` (the BepInEx `Plugin` is already one) with its **own** `Update()`
  that drains a **single-slot** queue: one primitive in flight, `poll`/`cancel`/
  `deadline`, `BUSY`/`TIMEOUT`/`CANCELLED`. It must **not** piggyback on any
  vanilla component's `Update`, and must **not** call vanilla APIs off the Unity
  main thread. The loopback TCP listener runs on a background thread and only
  **enqueues**; execution happens on the helper's own `Update` tick. This is the
  structural fix for the ValBridge/ScriptTools deadlock class.
- **Failure mode:** calling Unity/game APIs (`Instantiate`, `ZNetScene`, `Player`)
  from the socket thread → undefined behavior / crash. The dispatcher boundary is
  the mitigation; `AT-QA-NO-SCRIPTTOOLS-LOCK` proves non-reentry.

### 3.3 The lock to AVOID (console / ScriptTools re-entry)

```
// Terminal (abstract MonoBehaviour). Terminal.cs
public void TryRunCommand(string text, bool silentFail = false, bool skipAllowedCheck = false);  // @2245
// Chat/Console derive from Terminal; console command execution flows through TryRunCommand.
```

- **This is the surface ADR-0009 §5.2 forbids the harness from touching.** The
  cancelled T022 run wedged because probes rode the in-game console/`ScriptTools`
  and shared `Terminal`'s main-thread execution path. The harness control surface
  is a **dedicated loopback TCP/JSON channel + its own dispatcher** (§3.2), never
  a `Terminal`/console command. `Console.IsVisible()`/`Chat.instance` are read by
  `InventoryGui` (see §3.6) but the harness must not gate on or drive them.
- **Adapter:** none — this is a **do-not-bind** entry. Listed so the reviewer and
  the `AT-QA-NO-SCRIPTTOOLS-LOCK` proof know the exact symbol the dispatcher must
  share **no lock** with.

### 3.4 `ZNet.OnNewConnection` + direct per-peer ZRpc (server channel)

```
// ZNet.cs  (private — Harmony target)
private void OnNewConnection(ZNetPeer peer);              // @687

// ZNetPeer.cs (all public fields)
public class ZNetPeer : IDisposable {
    public ZRpc  m_rpc;            // @7   per-peer RPC channel
    public ISocket m_socket;       // @9
    public long  m_uid;            // @11  peer/session uid (0 until ready)
    public bool  m_server;         // @13
    public string m_playerName;    // @23
    public ZDOID m_characterID;    // @19
    public bool  IsReady();        // @38  => m_uid != 0
}

// ZRpc.cs (public)
public void Register(string name, RpcMethod.Method f);               // @233
public void Register<T>(string name, Action<ZRpc,T> f);             // @240
public void Register<T,U>(string name, Action<ZRpc,T,U> f);         // @247
public void Register<T,U,V>(string name, Action<ZRpc,T,U,V> f);     // @254
public void Register<T,U,V,W>(string name, RpcMethod<T,U,V,W>.Method f); // @261
public void Invoke(string method, params object[] parameters);      // @274
public ISocket GetSocket();      // @134   (identifies the DELIVERING peer)
public bool IsConnected();       // @206

// ZNet peer accessors (public)
public ZNetPeer GetPeer(long uid);          // @1483 (client) / @1472 (server)
public List<ZNetPeer> GetPeers();           // @2399 / @2344
public ZNetPeer GetServerPeer();            // @2371 (client-side: the server peer)
public ZRpc     GetServerRPC();             // GetServerPeer()?.m_rpc
public ZNetPeer GetPeer(ZRpc rpc);          // used internally (GetPeer(rpc)) to map an inbound rpc→peer
```

- **How vanilla registers per-peer RPCs:** inside `OnNewConnection(ZNetPeer)` the
  game calls `peer.m_rpc.Register<...>("Name", handler)` for each verb, and the
  server/client asymmetry is branched on `m_isServer` (ZNet.cs 693). This is the
  exact seam the harness server-channel mirrors: **register QA fixture verbs on
  a peer's `m_rpc`**, no host listener socket added.
- **Delivering-peer binding (ADR-0009 §5.1 "actual delivering peer"):** the
  handler receives `ZRpc rpc`; the server maps it to the real peer via
  `GetPeer(rpc)` (ZNet.cs 729/820 pattern) and reads `rpc.GetSocket()` for the
  socket identity. The harness **binds the delivering peer from `rpc`**, ignoring
  any claimed identity in the envelope — `peer_unbound`/`peer substitution`
  rejects fall out of comparing `GetPeer(rpc)` to the claim.
- **Hook strategy:** `OnNewConnection` is **private** → Harmony **postfix** on
  `ZNet.OnNewConnection(ZNetPeer)` to register the harness verbs on
  `peer.m_rpc` after vanilla registration. Only arms when the gate (§3.1) passes.
- **Failure modes:** registering a verb name that collides with a vanilla RPC
  name overrides routing — the harness **must namespace** its verbs (e.g.
  `SBPRQA.<verb>`); `m_uid == 0` until the peer handshake completes, so bind
  logic must wait for `IsReady()`. A verb invoked before `IsConnected()` throws.
- **Adapter:** `RegisterQaPeerVerbs(ZNetPeer peer)` called from the postfix,
  guarded by role==Server + armed; each verb handler resolves the delivering peer
  via `GetPeer(rpc)` and validates envelope before acting.

### 3.5 Additive fixture creation + cleanup (M2)

```
// ZNetScene.cs
public static ZNetScene instance { get; }        // @31
public GameObject GetPrefab(int hash);            // @136   (read blueprint — no Awake fired)
public GameObject GetPrefab(string name);         // @145 => GetPrefab(name.GetStableHashCode())
public void Destroy(GameObject go);               // @115   (network-aware despawn for cleanup)

// ObjectDB.cs
public static ObjectDB instance { get; }          // @18
public GameObject GetItemPrefab(string name);     // @61
public GameObject GetItemPrefab(int hash);        // @66
public bool TryGetItemPrefab(string name, out GameObject prefab); // @84

// ItemDrop.cs — the vanilla "spawn a real world item" seam
public static ItemDrop DropItem(ItemData item, int amount, Vector3 position, Quaternion rotation); // @1646
//   body: Instantiate(item.m_dropPrefab, pos, rot).GetComponent<ItemDrop>(); component.m_itemData = item.Clone(); ...

// CraftingStation.cs (station discovery / range)
public string m_name;                                                    // @6
public float  m_rangeBuild;                                              // @12 (=10f)
public static CraftingStation GetCraftingStation(Vector3 point);         // @241
public static CraftingStation HaveBuildStationInRange(string name, Vector3 point);  // @262
public static CraftingStation FindClosestStationInRange(string name, Vector3 point, float range); // @290
public int GetLevel(bool checkExtensions = true);                        // @362
public bool InUseDistance(Humanoid human);                               // @382
```

- **ADR-0006 compliance:** materials use the vanilla `DropItem` spawn seam. For
  stations/anchors, the harness reads the vanilla prefab only as a **blueprint**
  via `ZNetScene.GetPrefab` (fires no `Awake`), constructs an inactive
  `new GameObject()` shell with only `ZNetView`, `BoxCollider`, and category-required
  `CraftingStation`, copies only `m_name` and `m_useDistance`, and refuses a station
  blueprint missing that component or valid required values before it registers that
  shell, and instantiates the shell — never the vanilla donor. No path performs
  subtractive clone-and-strip. Materials granted are ordinary allowlisted
  vanilla items only (§4 firewall).
- **Owned-resource ledger:** every spawn returns a `GameObject`/`ItemDrop` whose
  `ZNetView`/`ZDOID` the ledger records; **cleanup** calls
  `ZNetScene.instance.Destroy(go)` per ledger entry, then verifies no ledger
  ZDO survives. `Destroy` (not `GameObject.Destroy`) is the network-aware path
  that removes the ZDO so the world save carries no harness spawn
  (`AT-QA-CLEANUP-NO-LEAK`).
- **Failure modes:** `GetItemPrefab`/`GetPrefab` return `null` for an unknown id
  → the allowlist must be validated against a **live** `ObjectDB`/`ZNetScene`
  before arm (drift). Spawning before `ZNetScene.instance`/`ObjectDB.instance`
  are ready NREs — fixtures only run after world load on the server role.
- **Adapters:** `SpawnAllowlistedStation(string prefab, Vector3 pos)`,
  `GrantVanillaMaterials(string itemId, int qty)` (loops `DropItem` or
  `Inventory.AddItem` on the target — see §3.10), `CleanupLedger()`. All bounded
  by exact ids/counts/radius per ADR-0009 §3.1.
- **M3R realized bindings (card t_1572d041).** The real net48 seam
  (`Runtime/ZNetVanillaFixtureSeam.cs`) implements this section against these exact,
  now-probe-pinned members (see `probe_vanilla_bindings.py`):
  - **Materials** → `ObjectDB.GetItemPrefab(name)` → the item prefab's `ItemDrop` →
    `ItemDrop.DropItem(m_itemData, amount, pos, rot)` (the vanilla clone-onto-world-drop
    grant seam; §3.5/§3.7).
  - **Stations / anchors** → `ZNetScene.GetPrefab(name)` as a read-only blueprint (no
    Awake), then construct an inactive shell from `new GameObject()` with only
    `ZNetView`, `BoxCollider`, and a `CraftingStation` required only for station-category
    requests. The seam copies only `CraftingStation.m_name` and `m_useDistance`, refuses
    missing/invalid required station data, registers the shell in `ZNetScene`, and
    instantiates **that shell** at the requested position. It never instantiates or strips
    the vanilla donor prefab (ADR-0006).
  - **Bounded marker discovery** → `ZoneSystem.GetZone(Vector3)` +
    `ZDOMan.FindSectorObjects(...)`, then `ZDO.GetPrefab()` allowlist filtering and
    `ZDO.GetPosition()` exact-radius filtering before `ZDO.GetString(...)` reads the marker.
    Additive registration's `ZNetScene.m_prefabs` / `m_namedPrefabs`, the required
    `CraftingStation.m_name` / `m_useDistance` blueprint fields, and marker
    `ZDO.Set(string,string)` / ownership methods are all drift-probe pinned.
  - **Owned handle** = the spawned object's full stable `ZNetView.GetZDO().m_uid`
    (`ZDOID(UserID, ID)`), serialized as `"UserID:ID"` — never a truncated numeric.
  - **Despawn / reconcile** → `ZNetScene.FindInstance(ZDOID)` → `ZNetView.ClaimOwnership()`
    + `ZNetView.Destroy()` when instanced locally, else `ZDOMan.GetZDO(ZDOID)` +
    `ZDOMan.DestroyZDO(zdo)` (the network-replicated despawn). `IsLiveInstance` uses the
    same lookup for crash reconcile. Exactly the idiom the product's
    `WarriorTwigDedicatedIngressObserver.UndoInstance` already uses.
  - **Verified:** the net48 Release helper build (0w/0e, `<TreatWarningsAsErrors>`) compiles
    against every member above, which resolves them in the live `assembly_valheim` more
    strictly than a signature grep. **No in-game execution is claimed** (M6 is separate).

### 3.6 Local craft / upgrade through `InventoryGui` (M3) — **client-only-live**

```
// InventoryGui.cs
public static InventoryGui instance { get; }        // @299 (m_instance)
private Recipe m_craftRecipe;                        // @261
private ItemDrop.ItemData m_craftUpgradeItem;        // @263
private RecipeDataPair m_selectedRecipe;             // @253
private float m_craftTimer;                          // @281  (-1 idle; 0 starts craft)
private void OnCraftPressed();                        // @ (button handler; sets m_craftRecipe/m_craftUpgradeItem/m_craftTimer=0)
private void SetRecipe(int index, bool center);       // @1186 (selects a recipe by index)
private void SetupCrafting();                         // @886
private void UpdateRecipe(Player player, float dt);   // @1212 (drives the craft timer; calls DoCrafting when done @1361)
private void DoCrafting(Player player);               // @1500 (THE issuance seam — private)
public static bool SetupRequirement(Transform,Piece.Requirement,Player,bool,int,int=1); // @1454 (public helper)

private struct RecipeDataPair { public Recipe Recipe {get;} public ItemDrop.ItemData ItemData {get;} } // @18

// Player.cs
public static Player m_localPlayer;                   // @147
public void SetCraftingStation(CraftingStation station);   // @4029
public CraftingStation GetCurrentCraftingStation();        // @4043 (=> m_currentStation)
public Inventory GetInventory();                            // Humanoid @833 (inherited by Player)
```

- **Craft flow (verified):** `OnCraftPressed()` copies the selected recipe into
  `m_craftRecipe`/`m_craftUpgradeItem`, sets `m_craftTimer = 0`. `UpdateRecipe`
  (per-frame, @1212) counts the timer and, when complete, calls
  `DoCrafting(player)` (@1361). `DoCrafting` (@1500) performs the actual craft/
  upgrade: computes target quality (`m_craftUpgradeItem == null ? 1 :
  m_craftUpgradeItem.m_quality + 1`), checks requirements/station, and adds/
  upgrades the item. **Upgrade == craft with a non-null `m_craftUpgradeItem`.**
- **Binding constraint (important):** `DoCrafting`, `OnCraftPressed`, `SetRecipe`,
  `SetupCrafting`, `UpdateRecipe` are all **private instance** methods. The
  harness Action verb `Craft{recipeName, station}` / `UpgradeItem{...}` therefore
  cannot call a public "craft this" API — there is none. Two clean options:
  1. **Preferred (drives the real GUI seam):** set the selection then simulate the
     press — via Harmony **reverse-patch**/`AccessTools` to invoke the private
     `OnCraftPressed()` after setting `m_selectedRecipe` (through `SetRecipe` by
     index found from `m_availableRecipes`), letting `UpdateRecipe`→`DoCrafting`
     run naturally on the next frames. This exercises the exact product issuance
     path (`AT-QA-CRAFT-THROUGH-PRODUCT-SEAM`), because DoCrafting is the seam the
     product's Workmanship issuance hooks.
  2. Set `m_craftRecipe`/`m_craftUpgradeItem`/`m_craftTimer=0` directly and let
     `UpdateRecipe` fire `DoCrafting`. Also valid but bypasses the button guardrails.
  Both require **reaching private members** — do so via **Harmony/`AccessTools`**
  in the QA assembly (allowed: clean-room applies to *other mods*, not to
  reflecting on the game we mod). **Do not** add a publicized game DLL to the
  build (§1).
- **Threading/lifecycle:** all of this must run on the Unity main thread when
  `InventoryGui.instance != null`, `Player.m_localPlayer != null`, and the crafting
  panel is open with a valid `GetCurrentCraftingStation()`. The single-slot
  dispatcher (§3.2) is the correct execution context. `InventoryGui.instance` is
  **null on the dedicated server** — this verb is **Client-role only** (matches
  ADR-0009 §3.1 Action = Client).
- **Failure modes:** calling `DoCrafting` with no open station / unmet
  requirements silently no-ops (guards at 1502/1515) → the receipt must observe
  the *result* (item present + quality) rather than assume success; `m_craftTimer`
  must be honored or the craft effect double-fires. Selecting the wrong
  `RecipeDataPair` upgrades the wrong item.
- **Adapter:** `CraftSelected(recipeName, station)` /
  `UpgradeItem(itemSlot, targetQuality)` — resolve the recipe/item into
  `m_selectedRecipe` via the existing `m_availableRecipes` list + `SetRecipe`,
  then invoke `OnCraftPressed` and await `m_craftTimer < 0` again; observe result.

### 3.7 Drop / pickup same-item continuity (M3 transfer)

```
// Humanoid.cs
public bool DropItem(Inventory inventory, ItemDrop.ItemData item, int amount);  // @767
//   -> ItemDrop.DropItem(item, amount, transform.position + forward + up, rotation) (@812)
public bool Pickup(GameObject go, bool autoequip = true, bool autoPickupDelay = true); // @588

// ItemDrop.cs
public void Pickup(Humanoid character);          // @1392 (Load(); ... Save())
public bool CanPickup(bool autoPickupDelay = true); // @1490
public static ItemDrop DropItem(ItemData item, int amount, Vector3 position, Quaternion rotation); // @1646 (Clone()s item)
```

- **Continuity:** `Humanoid.DropItem` → `ItemDrop.DropItem` **Clone()s** the
  `ItemData` onto the world drop (ItemDrop.cs 1648), and `Pickup` `Load()`s it
  back. `m_customData` is deep-copied in `Clone()` (`obj.m_customData = new
  Dictionary(m_customData)`, @412), and persisted through the ZDO
  save/load (§3.9). So a Masterwork stamp carried in `m_customData` **survives**
  drop→pickup — this is what `AT-QA-TRANSFER-PRESERVES` observes on the receiving
  client. The transfer is genuine (real world item hop), not a synthetic copy.
- **Client-only-live**, both roles are GUI clients (giver + receiver). Runs on
  the dispatcher main-thread tick.
- **Failure modes:** `CanPickup` false during the auto-pickup delay → receiver
  must retry within the bounded FSM (no sleeps; poll on the dispatcher). Dropping
  an equipped item requires `UnequipItem` first (see `DoCrafting`'s upgrade path
  @1548 for the pattern). Amount > stack silently clamps.
- **Adapters:** `DropItem(itemSlot)` (giver, role Client A),
  `PickUpNearest(itemName, radius<=Rmax)` (receiver, role Client B) — resolve the
  world `ItemDrop` by proximity and call `Humanoid.Pickup(go)`.

### 3.8 Tooltip observation (M3) — **client-only-live**

```
// ItemDrop.ItemData (nested)
public string GetTooltip(int stackOverride = -1);   // @622 -> GetTooltip(this, m_quality, false, m_worldLevel, stackOverride)
public static string GetTooltip(ItemData item, int qualityLevel, bool crafting, float worldLevel, int stackOverride = -1); // @677
```

- `GetTooltip` is a **pure string builder** over the item's own fields (uses a
  static `m_stringBuilder`; no UI/Terminal/console dependency). Reading it does
  **not** touch the `Terminal`/`ScriptTools` lock (§3.3) — this is precisely why
  ADR-0009 picks tooltip text as the observation seam. It surfaces the in-world
  Workmanship line a human sees (`AT-QA-TOOLTIP-OBSERVE`).
- **Threading:** `GetTooltip` uses a **static** `StringBuilder` shared with the
  UI — call it only on the main thread (dispatcher tick) to avoid interleaving
  with the game's own tooltip rendering. Observation receipt records the returned
  string verbatim (descriptive fact only, never a verdict).
- **Failure mode:** static `m_stringBuilder` reuse means a background-thread call
  can corrupt an in-flight UI tooltip → main-thread only. `UITooltip` itself lives
  in `assembly_guiutils`, **not** `assembly_valheim` (confirmed: ilspycmd could not
  find `UITooltip` in the module) — the harness reads the **text** via
  `ItemData.GetTooltip`, not the `UITooltip` component, which keeps the binding in
  the game assembly the harness already references.
- **Adapter:** `ReadTooltip(itemSlot)` → `inventory.GetItem(slot).GetTooltip()`.

### 3.9 Controlled custom-data tamper (M3 §4)

```
// ItemDrop.cs / ItemData
public Dictionary<string,string> m_customData;    // @392 (the tamperable surface)
// clone deep-copies it: obj.m_customData = new Dictionary<string,string>(m_customData); // @412
public static void SaveToZDO(int index, ItemData itemData, ZDO zdo);   // writes data_i / data__i pairs (@1581,1618)
//   zdo.Set(ZDOVars.s_dataCount, itemData.m_customData.Count); then per-entry data_{i}/data__{i}
public static void LoadFromZDO(...);   // itemData.m_customData.Clear(); reads back data_{i}/data__{i} (@1601-1604)
```

- **Tamper scope (ADR-0009 §4, firewall):** tamper may **replace or remove an
  existing allowlisted key** in `m_customData` on an **exact tracked throwaway
  item** only. It may **never add or copy a valid signature field**. `m_customData`
  is the correct, minimal surface — it is the free-form per-item string map the
  product uses for its Workmanship data; removing/garbling a key proves *degrade*
  (`AT-QA-TAMPER-DEGRADES`) without forging anything.
- **Persistence:** custom data round-trips through `SaveToZDO`/`LoadFromZDO`
  (keys `data_{i}` / `data__{i}`, count in `ZDOVars.s_dataCount`). A tamper on a
  live item must be followed by the item's normal save path to persist; the
  throwaway item is destroyed at cleanup so no store/journal is touched.
- **Failure modes:** editing `m_customData` without re-saving leaves the ZDO copy
  stale (tamper appears to "not stick"); editing a **product-store** copy is
  forbidden — tamper only the throwaway inventory item's in-memory `m_customData`.
- **Adapter:** `TamperField(itemSlot, field)` — asserts `field` ∈ static
  tamperable allowlist AND item ∈ ledger throwaway set; performs remove/replace;
  never inserts a signature key. Reviewed like code (static allowlist in helper).

### 3.10 Observation reads (inventory/item/world)

```
// Humanoid (Player inherits it)
public Inventory GetInventory();                 // Humanoid @833  (NOT declared on Player; inherited)
// Inventory.cs
public ItemDrop.ItemData GetItem(int index);     // @447
public ItemDrop.ItemData GetItem(string name, int quality = -1, bool isPrefabName = false); // @452
public ItemDrop.ItemData GetItemAt(int x, int y);// @590
public bool AddItem(GameObject prefab, int amount);       // @88
public ItemDrop.ItemData AddItem(string name,int stack,int quality,int variant,long crafterID,string crafterName,bool pickedUp=false); // @842
public bool RemoveItem(ItemDrop.ItemData item);  // @320
// ZNet.cs
public string GetWorldName(); public long GetWorldUID();   // §3.1
```

- **Observation-only** reads for `ReadInventory`/`ReadItem`/`ReadWorldName`/
  `ReadWorldUid`. `Inventory.AddItem(string,...,long crafterID,string crafterName,...)`
  (@842) is the **material-grant** primitive for §3.5's `GrantVanillaMaterials`
  when granting straight into an inventory rather than dropping in-world — note it
  takes `crafterID`/`crafterName`, which for **vanilla materials** are ordinary
  values, **not** a product signature (the firewall is about product-authored
  state, not the vanilla crafter stamp).
- **Failure mode:** `GetInventory` on a null `Player.m_localPlayer` (server / not
  spawned) NREs — Client-role, post-spawn only.

---

## 4. Firewall re-statement (what these bindings must NOT become)

None of the seams above cross ADR-0009 §4. Explicitly:

- The **craft/upgrade** binding (§3.6) drives the *real* `DoCrafting` seam so the
  Masterwork stamp appears **only** because product issuance ran — the harness
  does not write the stamp.
- The **tamper** binding (§3.9) only removes/replaces an allowlisted
  `m_customData` key on a throwaway item; it has **no** path that adds/copies a
  signature.
- The **fixture** bindings (§3.5) spawn ordinary vanilla items/stations via the
  game's own spawn seams; no product identity/entitlement/AP/BP/ownership/
  signature/snapshot/journal/cache is minted.
- The **observation** bindings (§3.8/§3.10) read product-rendered tooltip text and
  raw field keys — **no reflection into verdict caches** (threat T4).

Reaching **private** game members via Harmony/`AccessTools` (§3.4, §3.6) is a
**clean-room-permitted** operation: ADR-0001's wall is around *other developers'
mod code*, not the game we mod. The QA assembly may reflect on
`assembly_valheim`; it may **not** copy Jotunn/other-mod source and must not
commit decompiled game source. **`AT-QA-CLEANROOM`** sign-off applies to the
implementer, who works from *this description*, not from the decompile.

---

## 5. Pinning verdict — can every required method be pinned?

**Yes, with one bounded caveat.** Every M2–M4 seam ADR-0009 names is pinned to a
concrete public/private member with a verified signature in both the client and
server `0.221.12` builds:

| Need | Pinned? | Note |
|---|---|---|
| World UID + name arming | ✅ | `ZNet.GetWorldUID/GetWorldName`, `World.m_uid/m_name` |
| Main-thread dispatcher | ✅ | own `MonoBehaviour.Update`; no shared game lock |
| No-ScriptTools re-entry | ✅ | `Terminal.TryRunCommand` = documented do-not-bind |
| `OnNewConnection` + per-peer ZRpc | ✅ | private `OnNewConnection(ZNetPeer)` (Harmony postfix); `ZNetPeer.m_rpc`; `ZRpc.Register/Invoke`; `GetPeer(rpc)` for delivering-peer bind |
| Fixture spawn + cleanup | ✅ | `ZNetScene.GetPrefab/Destroy`, `ObjectDB.GetItemPrefab`, `ItemDrop.DropItem`, `CraftingStation.*` |
| Craft/upgrade via DoCrafting | ✅* | seam is **private** — bind via Harmony/`AccessTools`, not a public API |
| Drop/pickup continuity | ✅ | `Humanoid.DropItem/Pickup`, `ItemDrop.DropItem` (Clone preserves `m_customData`) |
| Tooltip observation | ✅ | `ItemData.GetTooltip` (pure, lock-free); `UITooltip` lives in `assembly_guiutils` |
| Custom-data tamper | ✅ | `ItemData.m_customData` + `SaveToZDO/LoadFromZDO` |

**The one caveat (not a blocker):** the arming gate's *production deny* is fully
specified by **port/endpoint** (`2456`/`2466`) and by exact disposable-world
**UID+name** supplied in the run manifest. The literal `World.m_uid` integers of
the two production worlds (Niflheim/Heistan) are **not** pinned here — obtaining
them means reading a production world save header, which is off-limits and
unnecessary (the port deny + disposable-world allowlist already fail-closed cover
it). If a future gate card insists on denying by production `m_uid` value, that is
the one item that would need an authorized read of a production `.fwl` — flag it
then. Nothing in M2–M4 requires it, so **no BLOCK is warranted**.

`AT-QA-CLEANROOM` gate: this document is the behavioral description the CLEAN-side
implementer builds from; it contains **no decompiled source**, only signatures and
behavior. Binding pins are the §1 SHA-256/MVID/version + §3 signatures.

---

## 6. Metadata drift probe (documentation/tooling only)

To keep these bindings honest against a game update, a **read-only** drift probe
is provided at `qa/decomp-map/probe_vanilla_bindings.py`. It shells `ilspycmd`
(or falls back to a signature grep of a provided decompile) to assert each pinned
member still exists with the pinned signature, and re-checks the assembly
SHA-256/version constants. It is **tooling only** — it launches no game, deploys
nothing, mutates nothing, and is not wired into the product build. Run manually:

```
python3 qa/decomp-map/probe_vanilla_bindings.py \
  --assembly /path/to/assembly_valheim.dll
```

A non-zero exit means a binding drifted → the corresponding M-card must re-pin
before it can rely on this map. This satisfies the card's "metadata drift probe"
authorization (docs/tooling only; no helper/product implementation).

---

## Provenance

- Decompiler: ICSharpCode `ilspycmd` 8.2.0.7535 on .NET SDK 8.0.129
  (`DOTNET_ROLL_FORWARD=Major`). MVID/version via a throwaway
  `Assembly.LoadFile().ManifestModule` reflection probe.
- Assemblies inspected: the two `0.221.12` builds pinned in §1 (client Modded GUI
  + niflheim dedicated-server download). No decompiled source committed
  (ADR-0001); no other-mod source read.
- Line numbers are against the ilspycmd decompile of MVID `23db560f…` (client);
  server equivalents noted inline where they differ.
