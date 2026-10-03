# B75 measured acceptance — 2026-10-03

Windows x64 player built successfully with Unity **6000.6.3f1**, Mono, Gamma colour space and Direct3D 11. GPU: **NVIDIA GeForce RTX 4070 Ti SUPER**, Windows driver **32.0.15.9649**. Python verification used numpy 2.4.4 and Pillow 12.2.0. Repeatability claims apply to this build/GPU/driver combination.

| Check | Measured result |
|---|---|
| Strict settings/catalog, dependencies, cycles, supplied precedence, packing, input protection and publication | 33 CPU contracts passed |
| Complete preset/schema catalog matches upstream constructors | 7 generated JSON files passed the drift check |
| CLI vs XML project-loading GUI `SetValues` reference, three 1024-square fixtures with nondefault controls | Maximum channel delta **0** on all ten outputs for each fixture |
| Alignment, overlap tiling, splat tiling vs frozen upstream transform shaders | Maximum RGB channel delta **0** for all output maps |
| Repeat each fixture and each transform/alpha combination | Byte-identical PNGs; fixture metadata also byte-identical |
| Preserve/opaque on transparent atlas; compare RGB across modes | Preserve retained transparency; opaque alpha was 255; RGB delta **0** |
| Rectangular supplied height, OpenGL normal calibration | 1024×512 dimensions retained; expected negative X / positive Y / positive Z signs |
| Supplied normal/height | Authoritative normal exported byte-identically |
| Invalid dimensions/flag, output reuse, protected input directory and timeout | Expected nonzero exits; no partial directory; timeout retained existing output hash |
| Five presets, in one sequential process | Passed in **3.099 s**; four nonmetal presets exported all-zero metallic; three atlas presets retained the original RGBA |
| 100 sequential jobs, rotating the three fixed 1024-square inputs, `ground_tile`, exporting normal+ORM | **84.686 s** total, **0.8469 s/job**, including process startup; repeated inputs agreed across job boundaries |

Single-process cold runs, including startup, requested all ten map outputs:

| Fixture | Seconds |
|---|---:|
| ground | 5.850 |
| wall | 4.674 |
| transparent atlas | 4.355 |

The first run overlapped an editor contract check, so these are measured observations rather than a hardware performance promise. An earlier complete run measured the same 100-job workload at 81.758 seconds.

## Independent reference and limits

Production assigns the typed filter objects directly. The reference serializes and reloads an upstream `ProjectObject` through XML and calls the GUI's original `SetValues`. Both execute the original processing coroutines and map shaders; there is no substitute map algorithm. Nondefault height/normal contrast, smoothness and AO depth exercise settings transfer. All GUI processing fields are covered by the typed catalog check.

Transform reference shaders are frozen from upstream `366579f`, changing only Shader names and trailing whitespace. This checks the new alpha work against original RGB computation rather than comparing two modes of the same modified shader. RGB comparisons cover every exported map. Alignment/tiling alpha is tested separately because preserving it is an intentional extension of upstream behavior.

Reference runs use the migrated Unity 6 engine. This evidence proves GUI-component/project-loading parity on that engine, not Unity 2017-versus-6 hardware parity or a human-driven GUI screenshot baseline. The original map shader mathematics remain unchanged. `settings.used.json`'s Unity `driver` string is the graphics API descriptor; the actual Windows display driver version is recorded above and in the evidence JSON.

## Load-bearing negative evidence

Before implementation, the contract suite failed at the intended red stub. Input-directory protection was then added from a real failing refusal test (`contracts-boundary-red2.log`: `Expected refusal did not occur`). Deliberately replacing ORM's `255-smoothness` with `smoothness` made the suite fail at `ORM channel contract` (`contracts-orm-fault.log`). The fault was restored, and `contracts-restored.log` passed all 33 checks. No injected fault remains.

## Reproduce and identify the player

Run the README build instructions, `AgentContractTests.Run`, `python tools/settings_catalog.py --check`, and `python tests/acceptance.py --benchmark`. Raw logs and PNG outputs are under `build/evidence/`; the committed [GPU report](evidence/gpu-report.json) records channel deltas and output hashes. Fixtures are deterministic, authored by the test generator and committed in `tests/fixtures/`.

Pinned tested player SHA-256:

| File | SHA-256 |
|---|---|
| materialize.exe | `7c97473f814fb098656bb6a33f585ec3e40908fee1f012467d7af48fb89f9791` |
| UnityPlayer.dll | `7c8127007131559a3daa04aa3a3e8d1477975fe449f59917c5c75d86fa1f904f` |
| materialize_Data/Managed/Assembly-CSharp.dll | `a8f5853a65ddb0e5d35b5a26fcf94ec86b5728feccab7aede40dac7d2e5920d9` |

`python tools/package.py` adds documentation, editable JSON settings, the GPL license and a file-hash/source-commit manifest, then produces `build/materialize-agent-windows-x64.zip`. The complete source stays in the separate public GPL fork. HTTP and game invocation are deferred.
