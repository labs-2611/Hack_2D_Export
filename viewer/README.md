# V2DG drawing viewer

The viewer runs from this repository with Python 3 and a modern browser. It uses only Python's standard library and the repository's `v2dg` decoder; Node, iModel, Agentulator, Revit, and the original geometry-export repository are not needed for viewing.

Double-click `open-viewer.cmd` from the repository root to see the included two-tag base and revision sample. To view a full Revit export, open a terminal in this repository and run:

```powershell
python -m viewer "C:\path\to\floor-plan.v2dg"
```

The server prints a local URL (by default `http://127.0.0.1:4179/`) and opens it in your browser. Keep the terminal open while viewing. Use Ctrl+C to stop it. The matching `.line-patterns.json` sidecar is picked up automatically when it sits beside the V2DG file. You can load multiple views together:

```powershell
python -m viewer "C:\captures\plan.v2dg" "C:\captures\rcp.v2dg"
```

Add a small second capture to highlight element replacements over one of the loaded full views:

```powershell
python -m viewer "C:\captures\plan.v2dg" --revision "C:\captures\changed-elements.v2dg"
```

The revision must have the same Revit view ID as its base. Its captured rows replace matching source-document/link-instance/element IDs, and new IDs are added. Other base rows remain. The viewer restores the replaced element's draw sequence from the base; the small Revit capture starts its own sequence at zero. A partial revision capture does not convey deletions. The overlay is created in memory and checked against the exact base atlas SHA-256 in the browser.

Controls include view overlays, annotations-only/model-only modes, category and family filtering, click-to-highlight with a chosen color, line patterns, smooth curves, zoom, and true view scale. The local server binds to `127.0.0.1` by default and serves only its fixed viewer files and the captures supplied at startup. Use `--port` to change the port or `--no-browser` to avoid opening a tab.

The viewer renders the V2DG compact atlas used by the earlier Canvas demo. It currently draws the linework and text represented by that atlas; it does not add raster fills, precise font matching, or a Revit visibility engine. The same [V2DG format](../docs/V2DG-v6.md) and [layer contract](../docs/web-layer-workflow.md) remain the data interfaces.
