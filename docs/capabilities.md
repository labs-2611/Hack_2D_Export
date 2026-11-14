# Exporter and format capabilities

## Captured from Revit

`View2dExport` uses Revit's 2D `CustomExporter` callbacks for a native floor, ceiling, area, engineering, section, elevation, detail, drafting, or legend view. A full capture writes one V2DG v6 binary per view. The Agentulator adapter can enumerate many views, capture a loaded linked document's own views, or restrict one view to requested source-document element IDs. The native add-in offers active-view and selected-host-element commands. Neither variant saves the RVT.

Each element row retains its source document title, link instance ID, Revit element ID, UniqueId when available, category, category kind, family, and type. Primitives retain global callback order, raw XYZ coordinates, RGB color, line width, line-pattern ID, transparency, and text. Supported curve payloads include lines, arcs, ellipses, Hermite splines, NURBS, and tessellated fallbacks. Text includes placement, base/up directions, width, font height, alignment, font name, bold flag, and style height. The separate line-pattern JSON artifact supplies dash/dot segment definitions for referenced pattern IDs.

`includeAnnotations=true` requests Revit annotation callbacks. `includePatternLines=true` requests face pattern strokes. The saved view detail level and display style are used by default; the Agentulator adapter can temporarily override them for an experiment. In hidden-line views, early analytic face edges may later be hidden; resolved tessellated callbacks are the safer source for visible model strokes.

## What the format represents

V2DG v6 is a **single-view, element-grouped drawing snapshot**. All coordinates are Revit internal feet in model XYZ; view origin/right/up/direction are in the header. Line width is the raw Revit callback value, so a web renderer must map it to display or paper units. A consumer can select, recolor, replace, or hide a whole element because primitives are grouped by source-qualified ID. The `sequence` field preserves the callback's interleaving of strokes across elements.

The binary format has no embedded textures, fonts, native Revit family definitions, model geometry, sheet placement graph, spatial index, or saved view overrides. It does not itself express a revision, deletion, USD-style layer, or a change hash. A bounding box or unchanged row hash is not proof that dependent geometry has stayed the same. Sheets are not directly accepted by `CustomExporter.Export(view)`; export their placed views and sheet metadata separately.

Filtering to `elementIds` keeps the artifact small. Revit still traverses the view, so CPU time does not fall in proportion to the written row count. Requested IDs without callbacks appear in `missingSelectedElementIds` in the Agentulator report. Downstream logic must decide whether the element was deleted, hidden, moved to another view, or has no drawable primitives.

The native add-in clears the current UI selection while capturing and restores it afterward; otherwise Revit can supply cyan selection-highlight colors instead of the element's drawing style. A filtered export's callback `sequence` starts at zero for its emitted primitives, so downstream replacement layers need to re-anchor draw order to the base view.

## Composable web data

The existing web drawing atlas accepts a **separate** `V2DGElementOverlay/1` JSON object, anchored to the exact base atlas JSON SHA-256. It supports `add`, `replace`, and `delete` operations keyed by `sourceDocument + NUL + linkInstanceId + NUL + elementId`. Later operations for a key replace earlier rows, while untouched rows remain from the base. That is the current working composition mechanism; it is not encoded inside the V2DG v6 binary. See [web-layer-workflow.md](web-layer-workflow.md) for a web upload contract and [V2DG-v6.md](V2DG-v6.md) for the binary layout.
