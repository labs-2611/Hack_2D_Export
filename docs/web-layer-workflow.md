# Add web data as a new drawing layer

This describes a web integration around V2DG v6. The current Level 4 atlas already **reads and composes** one `V2DGElementOverlay/1` JSON file. Its server currently serves that file from disk; it does not yet expose the upload API below. Implement the API in the web service that owns the drawing dataset, using this contract so every uploaded layer is reviewable and replayable.

## 1. Publish an immutable base

Decode the Revit V2DG snapshot and publish a compact per-view atlas of element rows. Store the exact atlas bytes and their SHA-256. Publish `viewId`, source document identity, view basis and scale, pattern definitions, and the SHA in `GET /api/views/{viewId}`. Keep the binary V2DG artifact as provenance. A new base requires a new hash; it must not silently replace bytes beneath existing layers.

## 2. Author a layer in the browser

The browser should let a user draw a curve, write text, or edit an existing element row. Convert pointer positions into view coordinates, then to Revit model XYZ using `point = origin + right*u + up*v` (all values in feet). Save the element identity and style with the geometry. Use a stable web namespace such as `sourceDocument="web:<project-id>"`, `linkInstanceId=-1`, a **negative int64 element ID**, and a UUID in `uniqueId` for new web elements. Existing Revit elements keep their Revit source document, link instance, and element IDs. Negative IDs avoid collision with positive Revit IDs.

Each new row uses the compact atlas fields `key`, `r`, `s`, `c`, `k`, `f`, `t`, and `p`. The key is `${sourceDocument}\u0000${linkInstanceId}\u0000${elementId}`. A primitive in `p` is `[kind, data, rgb, text, sequence, transparency, widthRaw, patternId, textStyle]`. See the real [example layer](../examples/web-annotation-layer.json). Web-created strokes should use an explicit paper width in the editor and convert it to feet (`millimetres / 304.8`) for `widthRaw`; keep the original unit in application metadata. Reserve an integer `sequence` range above the base view's maximum for web annotations so they draw on top. Browser code must reject non-finite coordinates and unsupported primitive kinds before submission.

## 3. Submit and persist an immutable layer

Proposed endpoint: `POST /api/views/{viewId}/layers` with `Content-Type: application/json`. The body is one `V2DGElementOverlay/1` object:

```json
{
  "format": "V2DGElementOverlay/1",
  "viewId": 1350701,
  "baseSha256": "1c4a77555835f2232ff176e3e391326a6cdd0c8c2e5c3deb222b10f0a584c1da",
  "sourceKind": "web-authored",
  "changes": [
    { "op": "add", "key": "web:review-01\u0000-1\u0000-1", "element": { "key": "web:review-01\u0000-1\u0000-1", "uniqueId": "7a6d8a65-32d0-4c17-9bdb-f6da37c8be82", "r": -1, "s": "web:review-01", "c": "Review notes", "k": "Annotation", "f": "", "t": "Markup", "p": [["curve:Line", [-23, 8, 32, -21, 8, 32], [189, 0, 199], "", 1000000000, 0, 0.00082, -1, null]] } }
  ]
}
```

The server should validate the base SHA, view ID, unique operation keys, row/key agreement, finite coordinates, primitive arity, allowed styles, user identity, and size limits. `replace` and `delete` must target a row that exists after composing earlier layers; `add` must use an unused key. It then writes the submitted bytes under a content hash, records `layerId`, parent layer hash, author, timestamp, and purpose in a separate manifest, and returns those identifiers. Do not overwrite an earlier layer file. A replacement layer references the same element key; a removal uses `{ "op": "delete", "key": "..." }` without an element row.

Browser submission can use the standard Fetch API after the editor has formed the validated layer object:

```js
const response = await fetch(`/api/views/${layer.viewId}/layers`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(layer)
});
if (!response.ok) throw new Error(await response.text());
const { layerId, layerSha256 } = await response.json();
```

The server response should include a durable `layerId` and content hash. The layer must be linked into the view's ordered manifest only after it has been validated and stored. Fetching that manifest again lets all connected browsers compose the same result. Authorization, upload-size limits, and revision conflicts belong in the server, not in the drawing JSON.

## 4. Compose and view

On load, fetch the immutable base and the ordered layer manifest. Verify the base SHA, then apply each layer in order. Within a layer, apply each operation once per key; the strongest layer wins for that element. Draw the resulting primitives by `sequence`. The current web atlas implements one base plus one overlay with this behavior. A production viewer needs an ordered layer list and conflict handling when the base changes: reject stale layers, rebase them against the new base, or expose the conflict to the author. Keep the source V2DG binary and all layers so the result can be reconstructed later.

For a `replace` built from a filtered Revit capture, re-anchor its primitive sequence values to the replaced base row before writing the overlay. A filtered capture restarts callback numbering at zero; using those numbers directly can move the updated element behind the rest of the drawing. A web author can deliberately assign a new sequence range when changing draw order. The reference composer validates and applies the supplied values; it does not infer the intended ordering.

The reference composer in this package is `python -m v2dg.layer examples/two-door-tags-atlas.json examples/web-annotation-layer.json --output composed.json`. It validates the base hash, view, operations, keys, finite coordinates, and basic styling. A web API can invoke the same library function after authentication and before persisting a layer. The example hash and view ID in this document are for the included Snowdon L4 **two-tag atlas**; replace them when using another base.

## 5. Export the web-authored result

The current overlay is JSON and remains a companion to V2DG v6. To create a new full `.v2dg` snapshot, compose all layers and encode the resulting element rows with the V2DG v6 writer while preserving the view header, unique element keys, and sequence values. A true binary *delta* would need a new version or magic value with explicit add/replace/delete operations and parent hashes; V2DG v6 has no such fields. Keep the JSON layer as the interchange format until that binary extension is specified and implemented.
