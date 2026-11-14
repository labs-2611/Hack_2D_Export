"""Serve local V2DG captures in the drawing atlas without Node or iModel."""
from __future__ import annotations

import argparse
import hashlib
import json
from collections import Counter
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit
import webbrowser

from v2dg.atlas import compact
from v2dg.inspect import decode

ROOT = Path(__file__).resolve().parents[1]
STATIC = Path(__file__).with_name("static")
STATIC_FILES = {
    "/": ("index.html", "text/html; charset=utf-8"),
    **{f"/{name}": (name, "text/javascript; charset=utf-8" if name.endswith(".js") else "text/css; charset=utf-8")
       for name in ("atlas.js", "atlas.css", "element-overlay.js", "curve-render.js",
                    "view-scale.js", "annotation-text.js")},
}


def encoded(value: dict) -> bytes:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def atlas_for(path: Path) -> dict:
    capture = decode(path)
    pattern_path = path.with_suffix(".line-patterns.json")
    patterns = json.loads(pattern_path.read_text(encoding="utf-8")) if pattern_path.exists() else []
    return compact(capture, patterns)


def manifest_row(atlas: dict) -> dict:
    view = atlas["view"]
    rows = atlas["elements"]
    annotations = [row for row in rows if row["k"] == "Annotation"]
    return {
        "id": view["id"], "name": view["name"], "type": view["type"],
        "scale": view["scale"], "detailLevel": view.get("detailLevel", ""),
        "elements": len(rows), "annotationElements": len(annotations),
        "annotationPrimitives": sum(len(row["p"]) for row in annotations),
        "categories": dict(Counter(row["c"] for row in rows)),
        "families": dict(Counter(row["f"] for row in rows if row["f"])),
        "patterns": len(atlas["patterns"]),
    }


def revision_layer(base: dict, base_bytes: bytes, revision: dict) -> dict:
    if base["view"]["id"] != revision["view"]["id"]:
        raise ValueError("revision capture must come from the same Revit view as its base")
    originals = {row["key"]: row for row in base["elements"]}
    if len(originals) != len(base["elements"]):
        raise ValueError("base contains duplicate element keys")
    next_sequence = max((p[4] for row in base["elements"] for p in row["p"]), default=-1) + 1
    changes = []
    for row in revision["elements"]:
        previous = originals.get(row["key"])
        if previous is not None:
            old_sequences = [p[4] for p in previous["p"]]
            for index, primitive in enumerate(row["p"]):
                # A filtered Revit capture restarts its sequence at zero. Keep the
                # original drawing position for the replacement element.
                primitive[4] = (old_sequences[index] if index < len(old_sequences)
                                else old_sequences[-1] + (index - len(old_sequences) + 1) * 0.000001)
            operation = "replace"
        else:
            for primitive in row["p"]:
                primitive[4] = next_sequence
                next_sequence += 1
            operation = "add"
        changes.append({"op": operation, "key": row["key"], "element": row})
    return {
        "format": "V2DGElementOverlay/1", "viewId": base["view"]["id"],
        "baseSha256": hashlib.sha256(base_bytes).hexdigest(),
        "sourceKind": "local-v2dg-revision", "changes": changes,
    }


def build_routes(captures: list[Path], revision_path: Path | None) -> dict[str, tuple[bytes, str]]:
    routes = {}
    atlases = {}
    manifest = []
    for path in captures:
        atlas = atlas_for(path)
        view_id = atlas["view"]["id"]
        if view_id in atlases:
            raise ValueError(f"duplicate view ID {view_id}; load one capture per view")
        content = encoded(atlas)
        atlases[view_id] = (atlas, content)
        routes[f"/atlas-view/{view_id}.json"] = (content, "application/json; charset=utf-8")
        manifest.append(manifest_row(atlas))
    manifest.sort(key=lambda row: (row["type"], row["name"]))
    routes["/atlas-manifest.json"] = (encoded({"views": manifest}), "application/json; charset=utf-8")
    if revision_path is not None:
        revision = atlas_for(revision_path)
        view_id = revision["view"]["id"]
        if view_id not in atlases:
            raise ValueError(f"revision view {view_id} is missing from the base captures")
        base, base_bytes = atlases[view_id]
        routes["/atlas-overlay.json"] = (encoded(revision_layer(base, base_bytes, revision)),
                                          "application/json; charset=utf-8")
    for route, (name, mime) in STATIC_FILES.items():
        routes[route] = ((STATIC / name).read_bytes(), mime)
    return routes


def serve(routes: dict[str, tuple[bytes, str]], host: str, port: int, open_browser: bool) -> None:
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            route = urlsplit(self.path).path
            if route not in routes:
                self.send_error(404, "Not found")
                return
            content, mime = routes[route]
            self.send_response(200)
            self.send_header("Content-Type", mime)
            self.send_header("Content-Length", str(len(content)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(content)

    with ThreadingHTTPServer((host, port), Handler) as server:
        url = f"http://{host}:{server.server_port}/"
        print(f"V2DG viewer: {url}", flush=True)
        if open_browser:
            webbrowser.open(url)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("captures", type=Path, nargs="*", help="full-view V2DG captures")
    parser.add_argument("--revision", type=Path, help="small second V2DG capture for one loaded view")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=4179)
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    captures = args.captures or [ROOT / "samples" / "two-door-tags-base.v2dg"]
    revision = args.revision or (ROOT / "samples" / "two-door-tags-moved.v2dg" if not args.captures else None)
    routes = build_routes(captures, revision)
    serve(routes, args.host, args.port, not args.no_browser)


if __name__ == "__main__":
    main()
