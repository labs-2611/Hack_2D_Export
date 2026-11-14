"""Validate and compose V2DGElementOverlay/1 JSON layers over a compact atlas.

This module uses only Python's standard library and has no Revit dependency.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path


def _valid_row(row: dict, key: str) -> None:
    if not isinstance(row, dict) or row.get("key") != key:
        raise ValueError(f"row/key mismatch for {key!r}")
    parts = key.split("\0")
    if len(parts) != 3 or row.get("s") != parts[0] or row.get("r") != int(parts[2]):
        raise ValueError(f"invalid element identity for {key!r}")
    if not isinstance(row.get("p"), list):
        raise ValueError(f"missing primitives for {key!r}")
    for primitive in row["p"]:
        if not isinstance(primitive, list) or len(primitive) != 9:
            raise ValueError(f"invalid primitive in {key!r}")
        kind, data, rgb, _text, sequence, transparency, width, _pattern, _style = primitive
        if not isinstance(kind, str) or not isinstance(data, list) or not data:
            raise ValueError(f"invalid primitive data in {key!r}")
        if not all(isinstance(n, (int, float)) and math.isfinite(n) for n in data):
            raise ValueError(f"non-finite coordinate in {key!r}")
        if not isinstance(rgb, list) or len(rgb) != 3 or not all(type(c) is int and 0 <= c <= 255 for c in rgb):
            raise ValueError(f"invalid RGB in {key!r}")
        if type(sequence) is not int or type(transparency) is not int or not isinstance(width, (int, float)) or not math.isfinite(width):
            raise ValueError(f"invalid primitive style in {key!r}")


def compose(base_bytes: bytes, layers: list[dict]) -> dict:
    """Return the composed atlas; reject a stale or malformed layer."""
    base = json.loads(base_bytes)
    view_id = base["view"]["id"]
    base_sha = hashlib.sha256(base_bytes).hexdigest()
    rows = {row["key"]: row for row in base["elements"]}
    if len(rows) != len(base["elements"]):
        raise ValueError("duplicate element keys in base")
    for layer in layers:
        if layer.get("format") != "V2DGElementOverlay/1" or layer.get("viewId") != view_id:
            raise ValueError("layer format or view mismatch")
        if layer.get("baseSha256") != base_sha:
            raise ValueError("layer base SHA-256 mismatch")
        seen = set()
        for change in layer.get("changes", []):
            key, operation = change.get("key"), change.get("op")
            if not isinstance(key, str) or key in seen:
                raise ValueError("missing or duplicate change key")
            seen.add(key)
            if operation == "delete":
                if key not in rows or "element" in change:
                    raise ValueError(f"invalid deletion: {key!r}")
                del rows[key]
            elif operation in ("add", "replace"):
                if (operation == "add") == (key in rows):
                    raise ValueError(f"invalid {operation}: {key!r}")
                row = change.get("element")
                _valid_row(row, key)
                rows[key] = row
            else:
                raise ValueError(f"unsupported operation: {operation!r}")
    return {**base, "elements": list(rows.values())}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base", type=Path, help="Exact compact atlas JSON used by the viewer")
    parser.add_argument("layers", type=Path, nargs="+", help="Ordered V2DGElementOverlay/1 JSON files")
    parser.add_argument("--output", type=Path, required=True, help="Composed atlas JSON")
    args = parser.parse_args()
    composed = compose(args.base.read_bytes(), [json.loads(path.read_text(encoding="utf-8")) for path in args.layers])
    args.output.write_text(json.dumps(composed, separators=(",", ":")), encoding="utf-8")
    print(json.dumps({"viewId": composed["view"]["id"], "elements": len(composed["elements"]), "output": str(args.output)}))


if __name__ == "__main__":
    main()
