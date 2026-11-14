Hack 2D Export for Revit 2026.0-2026.4

1. Close Revit 2026.
2. Double-click Hack2DExport-Revit2026-Installer.exe and read the result dialog.
3. Start Revit 2026 and open a floor plan or other native 2D view.
4. Use Add-Ins > Hack 2D Export > Export 2D view or Export selected elements.

The installer adds the DLL and .addin manifest to the current Windows user's
Revit 2026 Addins folder. Administrator rights, Python, and the .NET SDK are
not required. Revit 2026 supplies the .NET 8 runtime used by this installer.

The package checks the installed Revit API update and rejects Revit 2026.5
or later, which uses .NET 10. It does not change or save any Revit model.

For a read-only check after installation, run the installer with --check
from a terminal. The command returns 0 if the installed DLL and manifest
match the package.
