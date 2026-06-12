# Cartography preview harness (§2E.2)

A **throwaway** BepInEx plugin that renders the shipped `CartographyComposer` against the
**real** `WorldGenerator` inside a dedicated-server Unity runtime and writes preview PNGs.
NOT part of the shipped mod — it exists only to produce the AT-RENDER-PREVIEW images Daniel
signs off on before an in-game render change ships.

## Why P2 (real WorldGenerator) and not P1 (headless port)

A standalone .NET probe proved P1 is a dead end: `WorldGenerator.GetBiome`/`GetBiomeHeight`
bottom out in `DUtils.PerlinNoise` → `UnityEngine.Mathf.PerlinNoise`, a Unity **native ECall**.
Bare .NET throws `SecurityException: ECall methods must be packaged into a system module`.
A faithful port would have to reimplement Unity's exact Perlin tables — the drift trap the spec
warns against. So we run the real `WorldGenerator` inside the server's Unity runtime instead.

Because this plugin **links the shipped composer source** (`CartographyComposer.cs` +
`BoundedMapMath.cs` via `<Compile Include .../>`), the preview is byte-identical to the in-game
render: preview == ship by construction.

## Build

```sh
ValheimManaged=/path/to/valheim_server_Data/Managed \
BepInExCore=/path/to/BepInEx/core \
dotnet build SBPR.CartographyPreview.csproj -c Release
```

## Run (isolated, does NOT touch a live server)

1. Copy a BepInEx server install (doorstop libs + `BepInEx/core`) to a throwaway dir; clear
   `BepInEx/plugins` so ONLY `SBPR.CartographyPreview.dll` loads.
2. Generate the target world's `.fwl` from its seed
   (`tools/gen_world.py <name> <seedName>` in the worldgen-spike repo).
3. Launch the dedicated-server binary under doorstop with `-savedir` pointed at the throwaway
   world dir, `-nographics -batchmode`, on a non-conflicting port.
4. The plugin fires on `WorldGenerator.Initialize`, renders the windows from
   `SBPR_PREVIEW_WINDOWS` (env: `name,x,z,radius;...`) to `SBPR_PREVIEW_OUTDIR`, and logs
   `[SBPR/Preview] DONE`. Stop the server.

Env vars:
- `SBPR_PREVIEW_WINDOWS` — `spawn,0,0,1000;coast,1500,500,1000;...` (default: spawn/coast/north)
- `SBPR_PREVIEW_OUTDIR` — where PNGs are written (default `/config/preview`)

The PNGs are encoded by a pure-C# writer (`PngWriter.cs`) — no Unity Texture / GPU, so it works
on a headless dedicated server.
