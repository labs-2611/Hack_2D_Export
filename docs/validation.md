# Validation, 2026-11-14

The source package is `D:\Dev\Hack_2D_Export`. The Agentulator testing tool was installed from `plugin/View2dExport` and evaluated through Agentulator MCP. Its structure was valid with zero diagnostics, and it compiled against the installed Revit 2027 SDK with zero errors or warnings. All live calls used the agent-owned Revit host and `testing:true, test:true`; no model changes were saved.

| Live MCP check | Result |
| --- | --- |
| L4 view listing | Found view 1350701, FloorPlan, scale 1:96, Coarse detail |
| L4 `elementIds=[2242835,2242836]` | 2 rows, 10 primitives, 1,738 bytes; no missing IDs or partial export |
| Full L4 view | 1,119 rows, 26,174 primitives, 3,401,446 bytes; no partial export |
| Structural link's own view 579516 | 359 rows, 18,636 primitives, 2,644,055 bytes; no partial export |

The two-ID run was `01a1daf3-ff20-7f53-8658-d10d4a69661e`. Its binary is `samples/two-door-tags-base.v2dg`. The earlier moved-tag binary is `samples/two-door-tags-moved.v2dg`. The standalone `v2dg.compare` reader identifies exactly the two tag IDs as changed between them.

`build.ps1` compiled the Agentulator adapter from this repository with zero warnings and errors. `build-native.ps1` generated the shared capture core and built `Hack2DExport.dll` with only Revit API references, also with zero warnings and errors. The native `.addin` manifest was installed into the current user's Revit 2027 Addins folder. Its ribbon commands will load on the next Revit start; their UI and file dialog have not yet been exercised in a live Revit window.

The Python format suite passes five tests covering real V2DG decoding, truncation rejection, two-element comparison, exact-base web layer composition, and deletion across ordered layers. The example web layer also composed successfully with the existing Canvas viewer's `composeElementOverlay` module. The web upload endpoint in `web-layer-workflow.md` is a proposed integration contract, not an implemented server.

## Revit 2026 preparation, 2026-11-14

The build and install scripts now accept `-RevitVersion 2026`. They read the installed 2026 API DLL version to select .NET 8 for updates before 2026.5 or .NET 10 for 2026.5 and later. The native build writes to a staging folder, separate from the DLL loaded by Revit. The installed API is version `26.0.4.409`, so the actual 2026 native build targeted .NET 8; it passed with zero warnings/errors. The installed manifest points to the deployed DLL, and its hash matches the built DLL. The 2027 native and Agentulator adapter builds passed with zero warnings/errors, and the five Python format tests pass.

Agentulator MCP rejected a Revit 2026 launch because its service supports only Revit 2027 and Rhino 9. With the user's direction, live 2026 validation used the native Revit UI and the installed 2026 Snowdon Towers Architectural sample. The **L4 floor plan** (`viewId=1350701`, scale 1:96, Coarse) exported to `artifacts/validation/2026-l4.v2dg`: **1,181 element rows, 26,456 primitives, 3,441,321 bytes**. The independent decoder read it without truncation/trailing data, including 316 text primitives, 199 linked occurrence rows, and seven source documents. The line-pattern sidecar contains nine entries. The atlas converter produced 972 drawable element rows in `artifacts/validation/2026-l4-atlas.json`.

The native **Export selected elements** command first exposed a real issue: Revit reported cyan selection-highlight strokes for a selected grid. The add-in now clears the UI selection during `CustomExporter.Export` and restores it in `finally`. After rebuilding, installing, and restarting Revit 2026, a selected dimension exported as one row and 10 primitives to `artifacts/validation/2026-l4-dimension-selection-fixed.v2dg` (1,371 bytes). Its kind, text, data, and stroke styles match the corresponding row of the full-view capture exactly. Only its `sequence` values differ: they are 0–9 in the filtered capture versus 25,404–25,413 in the full capture. Downstream replacement layers must preserve or re-anchor the base row's sequence to retain draw order. The earlier cyan grid artifact remains in the ignored validation folder as failure evidence. No RVT was saved.

## Revit 2026 installer

`package-2026.ps1` rebuilt the native DLL and published `dist/Hack2DExport-Revit2026-Installer.exe` with the DLL embedded. The final EXE is 200,410 bytes; the ZIP with its README is 86,285 bytes and contains exactly those two files. Packaging and native build completed with zero warnings/errors. With Revit 2026 closed, the packaged EXE installed the DLL and manifest into the current user's Addins folder; `--check` returned success and its SHA-256 matched the embedded payload (`8919FEC6B6278615D54E8F48E3EDEF869AAE2A4047502A0F229E5C4146E31F0D`). The installer also refused an installation attempt while Revit 2026 was running. Revit 2026.5 and later are rejected by version check and need a separate .NET 10 package. The final package's double-click dialog has not been visually exercised.
