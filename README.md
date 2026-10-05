# Materialize Agent

Windows x64 GPU CLI fork of [Bounding Box Software's Materialize](https://github.com/BoundingBoxSoftware/Materialize), based on upstream `366579f`. Licensed under GPL-3.0; see [LICENSE](LICENSE). Source and Unity project remain public. The fork reuses the original map filters and GUI processing routines.

Requires Windows and a Direct3D 11 GPU. Build with **Unity 6000.6.3f1**, Windows Build Support (Mono). GPU batch mode is required: do not pass `-nographics`. No mouse or window interaction is needed.

## Build

From the repository root in PowerShell:

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe'
$process = Start-Process $unity -ArgumentList '-batchmode','-quit','-projectPath',"$PWD",'-executeMethod','AgentBuild.Build','-logFile',"$PWD/build/build.log" -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode) { throw 'Build failed; inspect build/build.log' }
```

The output is `build/windows/`. Keep its executable, Data directory and runtime DLLs together. `materialize.cmd` supplies the Unity engine arguments and the `--` separator. When invoking the executable directly, use `materialize.exe -batchmode -force-d3d11 -logFile logfile.txt -- <CLI arguments>`. The editor must be closed for batch builds.

## CLI

```powershell
build/windows/materialize.cmd --in albedo.png --preset ground_tile --maps normal,orm --out generated/stone
build/windows/materialize.cmd --in albedo.png --settings settings.json --maps height,normal,metallic,smoothness,roughness,edge,ao,orm --out generated/stone
build/windows/materialize.cmd --in albedo.png --preset atlas --map-in height=height.png --map-in normal=normal.png --maps normal,orm --out generated/atlas
build/windows/materialize.cmd --batch jobs.json
```

Choose exactly one `--preset` or `--settings`. `--maps` is required and accepts `albedo,diffuse,height,normal,metallic,smoothness,roughness,edge,ao,orm`. `albedo` is the original colour input, including requested transforms; `diffuse` is the adjusted diffuse filter output. Optional supplied maps are `diffuse,height,normal,metallic,smoothness,edge,ao`, each with the same dimensions as the input and OpenGL convention for normals. Supplied maps take precedence; missing dependencies are computed. A height-from-normal cycle requires a supplied dependency and otherwise fails.

Options: `--alpha preserve|opaque` overrides the settings (default preserve); `--timeout SECONDS` limits each job (default 300, maximum 3600); `--overwrite` replaces the entire output directory after success. Use a dedicated output directory. Directories containing input/settings files are rejected, including across batch jobs; nested batch outputs are rejected. Exit codes: 0 success, 2 invalid command/batch, 1 job failure. Batch stops at the first failure; previously completed jobs remain. Each job publishes a complete directory from staging; failure/timeout preserves existing outputs.

All exports are 8-bit PNG at input dimensions unless tiling changes them. Normal maps use OpenGL (+Y/green). ORM is R=AO, G=255-smoothness, B=metallic, with opaque alpha. Normal/scalar maps have data channels independent of albedo transparency. `settings.used.json` records every resolved setting, source/generated provenance, input SHA-256 hashes, output list and GPU/Unity environment. To reuse that record, extract its `settings` object into a settings file.

## Settings, alignment and tiling

Copy one of the five complete [presets](Assets/Resources/Agent/presets) or [GUI defaults](Assets/Resources/Agent/gui_defaults.json), then edit it. The [schema](Assets/Resources/Agent/settings.schema.json) describes every processing control and its effective GUI default/range. Complete files are mandatory: unknown, duplicate or missing keys, invalid ranges, non-finite values and contradictory source toggles fail. Sample colours are explicit `[r,g,b]` values in 0..1. Preview-only controls are excluded.

Presets: `ground_tile`, `wall_tile`, `atlas`, `foliage_atlas`, `decal_atlas`. Ground, wall, foliage and decal start nonmetallic. These are conservative starting settings, not material recognition. Both transforms default off.

In a copied complete settings file:

```json
"alignment": {
  "enabled": true,
  "pointTL": [0, 1], "pointTR": [1, 1],
  "pointBL": [0, 0], "pointBR": [1, 0],
  "LensDistort": 0.15, "PerspectiveX": 0.25, "PerspectiveY": 0
},
"tiling": {
  "enabled": true, "technique": "splat", "width": 512, "height": 1024,
  "Falloff": 0.1, "OverlapX": 0.2, "OverlapY": 0.2,
  "SplatRotation": 0, "SplatRotationRandom": 0.25,
  "SplatScale": 1, "SplatWobble": 0.2, "SplatRandomize": 0
}
```

Alignment runs on loaded inputs before generating dependencies and preserves dimensions. Tiling (`overlap` or `splat`) runs on completed maps, sharing geometry, before roughness/ORM packing. Width and height independently accept 512, 1024, 2048 or 4096 (GUI default 2048 each). Tiling needs a height map even for albedo-only jobs; zero overlap is refused. Enabled transforms also export `albedo.png`. Preserve carries colour alpha through the original warp/blend weights; splat uses a separate alpha accumulator because the original alpha buffer holds height-selection state. Opaque follows upstream colour-alpha behavior. RGB math is unchanged.

Upstream quirks remain: PerspectiveY is ineffective; serialized sample UVs, AO BlendAmount, and smoothness third-sample/isolation controls do not affect exported pixels. Explicit sample colours do. Actual constructor defaults win over stale annotations (AO Spread is 50, not the annotation's 5). Alignment precedes generated half-float height, avoiding the upstream GUI's broken internal height-alignment path. The legacy OBJ examples require absent upstream OBJData/OBJLoader sources and are disabled with `MATERIALIZE_LEGACY_OBJ`; the missing authentication component is disconnected. Preview-only legacy material construction was updated for Unity 6. Existing DLLs and upstream license notices are retained.

## Batch

Paths resolve relative to the manifest. Each job uses fresh textures, filter settings and fixed normal-convention preferences in the same GPU process.

```json
{
  "version": 1,
  "jobs": [
    {"in": "stone.png", "preset": "ground_tile", "maps": ["normal", "orm"], "out": "generated/stone"},
    {"in": "leaves.png", "settings": "foliage.json", "alpha": "preserve", "timeout": 300,
     "map_inputs": {"height": "leaves-height.png"}, "maps": ["normal", "ao"], "out": "generated/leaves"}
  ]
}
```

## Verification

```powershell
python tools/settings_catalog.py --check
# Same Unity batch invocation as Build, with -executeMethod AgentContractTests.Run
python -m pip install -r tests/requirements.txt
python tests/acceptance.py --benchmark
```

CPU contracts cover strict settings, the complete GUI field catalog, dependency order/cycles/supplied precedence, numeric representation, input protection, packing and atomic publication. GPU acceptance uses three deterministic 1024-square fixtures, nondefault controls, repeats, transforms, both alpha policies, supplied maps, rectangular normal sign calibration, failure/timeout cleanup and 100 sequential jobs. Logs, output hashes and timings go to `build/evidence/`.

`--reference` is a verification mode: settings take the independent upstream XML project-load/GUI `SetValues` path; transforms use frozen upstream shaders with their Shader names changed and trailing whitespace normalized. The reference shares the original processing coroutines, rather than reproducing the algorithms. Transform parity compares RGB separately from the intentionally added alpha behavior. It runs on the migrated Unity 6 engine; it is not a Unity 2017-versus-6 baseline or a human-driven GUI screenshot comparison. See [measured evidence](docs/ACCEPTANCE.md).

Byte-identical repeatability is scoped to the same build, input, settings, GPU and driver. HTTP service, game pipeline integration, automatic sample picking and higher-precision height export are deferred. The game invokes this external tool; no Materialize implementation is copied into it.

`tools/settings_catalog.py` owns the generated settings files. `tools/freeze_reference.py` owns the frozen upstream transform references. The other Python migration/port scripts record the initial mechanical migration and are not build steps.
