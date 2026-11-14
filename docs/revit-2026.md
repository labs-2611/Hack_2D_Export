# Revit 2026 build and test

The V2DG v6 capture code is shared between the Revit 2026 and 2027 builds. Build against the API DLLs from the **installed Revit version**. The scripts choose .NET 8 for Revit 2026.0–2026.4 and .NET 10 for Revit 2026.5 or later by reading the installed `RevitAPI.dll` file version. The resulting DLLs have separate `net8.0-windows` and `net10.0-windows` output folders. The 2027 build stays on .NET 10.

## Packaged installer

For Revit 2026.0–2026.4, close Revit and run `dist/Hack2DExport-Revit2026-Installer.exe`, or share `dist/Hack2DExport-Revit2026.zip` and run the EXE inside it. The package embeds the exporter DLL and writes the manifest to the current user's Addins folder. It needs no administrator rights, Python, or .NET SDK on the recipient's computer. Revit supplies the .NET 8 runtime. Double-clicking shows a success or error dialog; `--quiet` installs without a dialog, and `--check` compares the installed DLL and manifest with the package without changing anything. Revit 2026.5 and later require a separate .NET 10 package.

To rebuild the installer on a development machine with Revit 2026.0–2026.4 and the .NET 8 SDK, run `./package-2026.ps1`. This produces the EXE, ZIP, and README in `dist/`.

For a source install, from this repository in PowerShell, after Revit 2026 is installed:

```powershell
.\build-native.ps1 -RevitVersion 2026
.\install-addin.ps1 -RevitVersion 2026
```

The installer copies the built DLL to `%APPDATA%\Autodesk\Revit\Addins\2026\Hack2DExport` and writes `Hack2DExport.addin` to `%APPDATA%\Autodesk\Revit\Addins\2026`. It does not replace the 2027 manifest. Build output stays under `artifacts/addin/2026`, separate from the loaded DLL. Close Revit before installing an update to a DLL it has loaded. Open a native 2D view in Revit 2026 and use the **Hack 2D Export** ribbon panel. Its commands export the active view or selected host elements to `.v2dg` plus `.line-patterns.json`.

If Agentulator is installed for Revit 2026, compile the test adapter against its 2026 assemblies:

```powershell
.\build.ps1 -RevitVersion 2026
.\install-testing.ps1
```

The Agentulator tool source is version independent, but the installed Agentulator service must be able to connect to a Revit 2026 host. Use MCP Test Mode with both `testing:true` and `test:true`. Do not use Revit 2027 view IDs or its saved RVT as a 2026 test fixture. Discover a view and IDs in a 2026 model first.

## Live validation after installation

1. Confirm the installed Revit version/update and the script-selected .NET runtime; check that the manifest points to the matching output DLL.
2. Start or attach a Revit 2026 host through Agentulator MCP, open a native 2026 model, and list its 2D views.
3. Export a complex floor plan with annotations and pattern lines. Decode the V2DG file and check row count, primitive count, styles, and no partial export.
4. Export two selected element IDs from that view. Check that only those source rows are written, missing IDs are reported, and the smaller binary decodes.
5. If the model has a loaded link, list and export one of the link's own 2D views, then check linked occurrence keys.
6. Restart Revit 2026 and exercise both native ribbon commands. Confirm the saved binary and line-pattern sidecar decode and match the selected view/IDs.
7. Convert one capture to an atlas, add a web overlay, compose it, and inspect the result in the viewer.

Current machine status: Revit 2026.0 is installed. The native 2026 build and both ribbon commands passed live tests on its Snowdon L4 floor plan; the packaged installer also passed a clean-install and hash-check test, as recorded in [validation.md](validation.md). Agentulator MCP still supports only Revit 2027 and Rhino 9, so the 2026 adapter and linked-document-own-view MCP checks remain pending. Autodesk documents the [original Revit 2026 .NET 8 requirement](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Introduction/Getting_Started/Welcome_to_the_Revit_Platform_API/Revit_API_Revit_API_Developers_Guide_Introduction_Getting_Started_Welcome_to_the_Revit_Platform_API_Development_Requirements_html.html) and the [2026.5 .NET 10 transition](https://help.autodesk.com/view/RVT/2025/ENU/?caas=caas%2Fsfdcarticles%2Fsfdcarticles%2FRevit--Requirements-for-products-affected-by-the-Microsoft--NET-10-transition.html).
