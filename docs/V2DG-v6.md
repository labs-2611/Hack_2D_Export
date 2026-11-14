# V2DG v6 binary format

V2DG stores the 2D drawing callbacks from one native Revit view, grouped by source element. `View2dExport` writes one `.v2dg` artifact and one line-pattern JSON sidecar per view. The stream is little-endian, and strings use .NET `BinaryWriter.Write(string)`: UTF-8 bytes prefixed by a 7-bit encoded byte length. Revit coordinates are decimal **feet**. A consumer chooses its own display scale and units.

## Stream layout

| Field | Type | Meaning |
| --- | --- | --- |
| Magic | 4 ASCII bytes | `V2DG` |
| Version | uint16 | `6` |
| Document title | string | Revit source document containing the view |
| View ID | int64 | Revit `ElementId.Value` |
| View name, view type | string, string | Revit view metadata |
| View scale | int32 | Denominator of the drawing scale |
| Detail level | string | Effective level at capture time |
| Origin, right, up, view direction | 4 sets of 3 float64 | View basis in model feet |
| Row count | int32 | Number of element rows |
| Rows | repeated | Layout below |

Each row contains: `sourceDocument` (string), `linkInstanceId` (int64), `elementId` (int64), `uniqueId` (string), `category` (string), `categoryKind` (string), `familyName` (string), `typeName` (string), `primitiveCount` (int32), and that many primitives. For a host element, `linkInstanceId` is `-1`. A linked element uses its visible link instance ID, so the stable occurrence key is **view ID + source document + link instance ID + element ID**. Revit `UniqueId` is also retained when available.

Each primitive contains: global callback `sequence` (int64), `kind` (string), `text` (string, empty for non-text), `dataCount` (int32), that many float64 values, RGB (3 uint8), raw Revit `LineProperties.LineWidth` (float64), line-pattern ID (int64), transparency (int32), `hasTextStyle` (bool), then optional text style: horizontal alignment (string), vertical alignment (string), font name (string), bold (bool), height (float64). The sequence retains callback draw order across element rows.

## Geometry payloads

| Kind | Data values |
| --- | --- |
| `curve:Line`, `faceEdge:Line` | start XYZ, end XYZ |
| `*:Arc` | center XYZ, X direction XYZ, Y direction XYZ, radius, start angle, end angle |
| `*:Ellipse` | center XYZ, X direction XYZ, Y direction XYZ, X radius, Y radius, start angle, end angle |
| `*:HermiteSpline` | periodic flag, control count, tangent count, parameter count, then control XYZs, tangent XYZs, parameters |
| `*:NurbSpline` | degree, knot count, weight count, control count, then knots, weights, control XYZs |
| `polyline`, `tessellatedLine`, `tessellatedPolyline`, `tessellatedFilledPolyline` | concatenated XYZ vertices |
| `text` | position XYZ, base direction XYZ, up direction XYZ, width, font height; glyph string and style are in the primitive fields |

Other Revit curve types use the suffix `:tessellated` and concatenated XYZ vertices. Consumers should preserve unknown kinds or report them; silently treating every `data` array as a polyline is unsafe.

In hidden-line views, early analytic curves and face edges can include geometry later hidden by Revit. The tessellated model callbacks represent resolved visible linework. Annotation curves and text are retained separately. The raw line width is not a CSS pixel width. The line-pattern sidecar lists `{sourceDocument,id,segments,resolved}`; segment lengths are Revit internal feet on paper.

## Incremental captures

With `elementIds`, the exporter writes only rows that Revit reports for those IDs in the selected view. The report lists requested IDs absent from the capture. A downstream stage decides whether such an absence means removal, invisibility, or a view setting change. The V2DG stream itself is a snapshot, not a changeset or USD layer; composition by element key is an external operation.

The `sequence` values of a filtered capture start with the emitted callbacks. They do **not** retain the absolute sequence positions from a full-view capture. When replacing a row in a layered view, use the base row's sequence position as its draw-order anchor, or recapture the full view when ordering changes matter. The native add-in clears any UI selection during capture so Revit's selection highlight color is not written as the element style.

The pure Python decoder is `v2dg/inspect.py`. It accepts V2DG versions 1 through 6 for existing captures and rejects truncated streams, unsupported versions, and trailing bytes. New output uses version 6.
