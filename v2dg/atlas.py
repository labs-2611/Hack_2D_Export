"""Convert a V2DG capture to the compact JSON consumed by the drawing atlas."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from .inspect import decode


def compact(capture: dict, patterns: list | None = None) -> dict:
    elements = []
    for source in capture["elements"]:
        primitives = [
            p for p in source["primitives"]
            if p["kind"].startswith("tessellated") or p["kind"] == "text"
            or source["categoryKind"] == "Annotation" and
            (p["kind"].startswith("curve:") or p["kind"] == "polyline")
        ]
        if not primitives:
            continue
        key = f'{source["sourceDocument"]}\0{source["linkInstanceId"]}\0{source["elementId"]}'
        elements.append({
            "key": key, "r": source["elementId"], "s": source["sourceDocument"],
            "c": source["category"] or "Uncategorized", "k": source["categoryKind"],
            "f": source["familyName"], "t": source["typeName"],
            "p": [[p["kind"], p["data"], p["stroke"]["color"], p["text"], p["sequence"],
                   p["stroke"]["transparency"], p["stroke"]["widthRaw"],
                   p["stroke"]["patternId"], p.get("textStyle")]
                  for p in primitives]
        })
    return {"view": capture["view"], "patterns": patterns or [], "elements": elements}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--patterns", type=Path, help="Line-pattern JSON sidecar")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    patterns = json.loads(args.patterns.read_text(encoding="utf-8")) if args.patterns else []
    atlas = compact(decode(args.capture), patterns)
    args.output.write_text(json.dumps(atlas, separators=(",", ":")), encoding="utf-8")
    print(json.dumps({"viewId": atlas["view"]["id"], "elements": len(atlas["elements"]), "output": str(args.output)}))


if __name__ == "__main__":
    main()
