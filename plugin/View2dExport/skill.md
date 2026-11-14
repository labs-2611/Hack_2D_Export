# View2dExport

Use this Agentulator Revit tool to capture the drawing primitives of a native 2D view as V2DG v6. It groups curves, tessellated strokes and text by source document, link instance and element ID. It also writes a JSON sidecar for referenced line-pattern definitions. The tool does not save the RVT.

Set `viewId` for one view, or use `listOnly`, `maxViews`, and name filters to discover and capture views. `scope=host` exports the active document's views, including linked content visible in those views. `scope=source` with a loaded `linkInstanceId` exports the linked document's own views. Sheets are unsupported by `CustomExporter.Export(view)`; use the placed drawing views instead.

For an incremental capture, pass `viewId` and `elementIds` from that selected source document. The binary then contains only matching element rows. The exporter still traverses the view internally, so the small payload does not imply proportional Revit CPU savings. The report includes `missingSelectedElementIds`: a requested element absent from the view's callbacks may have become invisible and should be considered for deletion in the downstream overlay.

Use `includePatternLines=true` for face hatch strokes, `includeAnnotations=true` for text and annotation linework, and `detailLevel=view` to retain the view's saved detail. A temporary detail override is rolled back. The output format and its exact binary layout are in `../../docs/V2DG-v6.md`; decode with `python -m v2dg.inspect <artifact.v2dg>` from the package root. In Agentulator Test Mode, call with both `testing:true` and `test:true`; Revit model transactions roll back while artifacts persist.
