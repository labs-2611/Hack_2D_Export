# Hack 2D Export

Standalone Revit 2026/2027 add-in, Agentulator test adapter, and open decoder for the V2DG 2D drawing format. The exporter captures native Revit view graphics, including element IDs, linked-element occurrence keys, curves, resolved linework, text styling, and line-pattern references. All source needed to build either variant lives here; the original geometry-export repository is not required.

## Contents

- `plugin/View2dExport/`: installable Agentulator test adapter, schema, usage instructions, and test case.
- `addin/`: native Revit add-in, built from the same capture callbacks and V2DG writer without Agentulator references.
- `installer/` and `package-2026.ps1`: self-contained payload installer source and packaging script for Revit 2026.0–2026.4.
- `docs/V2DG-v6.md`: exact binary layout, coordinate units, primitive kinds, and incremental capture semantics.
- `docs/capabilities.md`: what the exporter and format support today, and their limits.
- `docs/web-layer-workflow.md`: an actionable web upload and layer composition contract.
- `docs/validation.md`: live Agentulator checks, build results, and remaining UI verification.
- `docs/revit-2026.md`: 2026 build and installation commands, runtime selection, and live test checklist.
- `v2dg/`: Python standard-library decoder and element-stream comparison CLI.
- `viewer/`: standalone browser viewer and local server for full V2DG views and small revision captures.
- `samples/two-door-tags-base.v2dg` and `samples/two-door-tags-moved.v2dg`: two 1,738-byte real Revit captures that differ by two temporary tag moves.

## Run the drawing viewer

Install Python 3, then open PowerShell in this repository (`D:\Dev\Hack_2D_Export`). To try the included base drawing and two-element revision, double-click `open-viewer.cmd` or run:

```powershell
python -m viewer
```

To open a full view exported from Revit, pass its `.v2dg` file. The viewer automatically reads a matching `.line-patterns.json` sidecar beside it:

```powershell
python -m viewer "C:\captures\floor-plan.v2dg"
```

Pass multiple full-view captures to overlay plans, or add a small second capture for an element-level revision:

```powershell
python -m viewer "C:\captures\plan.v2dg" "C:\captures\rcp.v2dg"
python -m viewer "C:\captures\plan.v2dg" --revision "C:\captures\changed-elements.v2dg"
```

The viewer opens `http://127.0.0.1:4179/` in a browser. Keep its terminal open and press Ctrl+C to stop it. If that port is occupied, add `--port 4180`. The viewer uses Python's standard library and a browser; it does not need Node, iModel, Agentulator, or Revit to display an existing capture. See the [viewer guide](viewer/README.md) for controls and revision behavior.

## Install and use the exporter

Run `./install-testing.ps1` in PowerShell. It copies `plugin/View2dExport` into the configured Agentulator testing tool root; it does not invoke Revit. Agentulator Test Mode loads the tool, usually after its watcher finishes recompiling. Call it through Agentulator MCP with **both** `testing: true` and `test: true`:

Run `./build.ps1` to compile the Agentulator adapter **from this repository** against the installed Revit 2027 and Agentulator assemblies. It writes the DLL under `plugin/View2dExport/bin/Release/net10.0-windows/` and does not build or reference the original geometry-export source tree.

For Revit 2026.0–2026.4, distribute [the installer ZIP](dist/Hack2DExport-Revit2026.zip) or [the installer EXE](dist/Hack2DExport-Revit2026-Installer.exe). Close Revit, double-click the EXE, then start Revit and use **Add-Ins > Hack 2D Export**. The installer needs no administrator rights, Python, or .NET SDK. Rebuild it with `./package-2026.ps1` on a machine with Revit 2026.0–2026.4 and the .NET 8 SDK. For a source install, `./build-native.ps1 -RevitVersion 2026` and `./install-addin.ps1 -RevitVersion 2026` remain available. See [Revit 2026 build and test](docs/revit-2026.md).

## Native Revit add-in

Run `./build-native.ps1` to generate the native capture core from the local test adapter and build `artifacts/addin/<Revit year>/<target framework>/Hack2DExport.dll`. The native project references **only Revit API assemblies**. Run `./install-addin.ps1` to copy that DLL and place an `.addin` manifest in the user's Revit Addins folder; restart Revit to load the ribbon panel. The panel has **Export 2D view** and **Export selected elements** commands. Both write a `.v2dg` file and a `.line-patterns.json` sidecar using the active native 2D view. The selection command filters to the selected host element IDs.

`scripts/generate_native_core.py` takes the callback and binary writer portion of the local Agentulator adapter and generates `addin/View2dCore.g.cs`. This keeps the two variants on the same V2DG implementation while keeping Agentulator out of the native assembly.

The current native add-in exports the active view or selected host elements. The Agentulator adapter also supports batches of host views and loaded linked documents' own views. The native project can use the same capture core for a future batch command; see [capabilities](docs/capabilities.md) for the exact current scope.

The Revit 2026 native DLL and manifest are installed. Both ribbon commands passed a live L4 floor-plan export test; see [validation](docs/validation.md). The 2027 native ribbon still needs a live UI check.

```json
{
  "hostId": "<agent-owned Revit host>",
  "tool": "View2dExport",
  "arguments": {
    "scope": "host",
    "viewId": 1350701,
    "includeAnnotations": true,
    "includePatternLines": true
  },
  "testing": true,
  "test": true
}
```

For a small capture after a model edit, add `"elementIds": [2242835, 2242836]`. Those IDs are an example from the Snowdon Towers Architectural sample. A source link's own views use `scope: "source"` and its `linkInstanceId`. `listOnly: true` discovers views without exporting geometry. The tool does not edit or save the model. In Test Mode, transactions are rolled back while artifact files persist.

Decode an artifact from this directory with `python -m v2dg.inspect samples/two-door-tags-base.v2dg --dump-json sample.json`. Compare the included captures with `python -m v2dg.compare samples/two-door-tags-base.v2dg samples/two-door-tags-moved.v2dg`. Run `python -m unittest discover -s tests -v` for the portable format checks.

For web-authored data, first run `python -m v2dg.atlas capture.v2dg --output base-atlas.json`, optionally passing `--patterns` with the line-pattern sidecar. Then `python -m v2dg.layer base-atlas.json layer.json --output composed.json` validates the exact base hash and add/replace/delete operations and writes the composed view. The [web layer workflow](docs/web-layer-workflow.md) specifies the request and persistence steps for a web API. The repository does not yet host that API.

The element filter shrinks the artifact, but Revit's `CustomExporter` still traverses the whole view. On the Snowdon L4 example, the warm full export took 1.64 seconds and produced a 3.40 MB V2DG file; the warm two-element export took 1.25 seconds and produced 1,738 bytes. Those figures describe this sample and do not include model-open time.
