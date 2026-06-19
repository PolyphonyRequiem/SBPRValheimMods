# cartography-ui-preview

Throwaway **GPU verification harness** for the cartography map UI. NOT part of the
shipped modpack — it exists so a real GPU client can eyeball the disc + modal render
(the shader appearance the headless CI box cannot judge — see
`docs/v2/planning/cartography-impl-spec.md` §2E.5.3 "logs-green ≠ playable").

## What it does
A BepInEx plugin that hooks `Minimap.Start`, synthesizes a `SurveyData` window,
builds the SBPR disc + full-screen modal via the real `MapViewer`/`MapSurface`
path, then `ReadPixels` → PNG so the actual shader-composited output can be viewed
off-machine (e.g. delivered to Discord).

## Build
```sh
# Needs the fix under test built first:
dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release
# Then the harness (references that DLL + Valheim/BepInEx managed assemblies via
# $(ValheimManaged)/$(BepInExCore) from the repo-root Directory.Build.props):
dotnet build tools/cartography-ui-preview/SBPR.CartographyUiPreview.csproj -c Release
```
Drop both `SBPR.Trailborne.dll` and `SBPR.CartographyUiPreview.dll` into a GPU
client's `BepInEx/plugins/`, launch into a world, and collect the captured PNGs.

## Why it's in-repo
Reproducible: the disc's fixed zoom / fog-cloud / circular-clip correctness is
eyeball-judged on Daniel's RTX client, and this harness is how those captures get
made deterministically instead of by hand each playtest. `bin/`/`obj/` are
gitignored; only the source (`PreviewPlugin.cs` + `.csproj`) is tracked.
