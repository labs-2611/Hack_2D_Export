"""Inspect an Agentulator View2dExport V2DG capture (versions 1 through 6).

The stream is grouped by (source document, link instance, element) within one
native Revit view. Values are raw Revit feet and the stroke width is the raw
CustomExporter value; no paper-unit conversion is assumed.
"""
from __future__ import annotations

import argparse
import collections
import json
import struct
from pathlib import Path


class Reader:
    def __init__(self, data: bytes):
        self.data = data
        self.offset = 0

    def read(self, fmt: str):
        size = struct.calcsize(fmt)
        if self.offset + size > len(self.data):
            raise ValueError(f"truncated V2DG at byte {self.offset}")
        values = struct.unpack_from(fmt, self.data, self.offset)
        self.offset += size
        return values[0] if len(values) == 1 else values

    def raw(self, size: int) -> bytes:
        if self.offset + size > len(self.data):
            raise ValueError(f"truncated V2DG at byte {self.offset}")
        value = self.data[self.offset:self.offset + size]
        self.offset += size
        return value

    def string(self) -> str:
        shift = 0
        length = 0
        while True:
            part = self.read("<B")
            length |= (part & 127) << shift
            if part < 128:
                break
            shift += 7
            if shift > 28:
                raise ValueError("invalid BinaryWriter string length")
        return self.raw(length).decode("utf-8")


def decode(path: Path):
    r = Reader(path.read_bytes())
    if r.raw(4) != b"V2DG":
        raise ValueError("not a V2DG capture")
    version = r.read("<H")
    if version not in (1, 2, 3, 4, 5, 6):
        raise ValueError(f"unsupported V2DG version {version}")
    document = r.string()
    view_id = r.read("<q")
    view_name = r.string()
    view_type = r.string()
    scale = r.read("<i")
    detail_level = r.string() if version >= 5 else ""
    axes = [r.read("<3d") for _ in range(4)]
    rows = []
    for _ in range(r.read("<i")):
        source_document = r.string()
        link_instance_id = r.read("<q")
        element_id = r.read("<q")
        unique_id = r.string() if version >= 2 else ""
        category = r.string() if version >= 2 else ""
        category_kind = r.string() if version >= 5 else ""
        family_name = r.string() if version >= 3 else ""
        type_name = r.string() if version >= 3 else ""
        primitives = []
        for _ in range(r.read("<i")):
            sequence = r.read("<q") if version >= 4 else None
            kind = r.string()
            text = r.string()
            count = r.read("<i")
            if count < 0:
                raise ValueError("negative primitive data length")
            data = list(r.read(f"<{count}d")) if count > 1 else ([r.read("<d")] if count == 1 else [])
            red, green, blue = r.read("<3B")
            width = r.read("<d")
            pattern_id = r.read("<q")
            transparency = r.read("<i")
            primitive = {
                "kind": kind,
                "text": text,
                "data": data,
                "stroke": {
                    "color": [red, green, blue],
                    "widthRaw": width,
                    "patternId": pattern_id,
                    "transparency": transparency,
                },
            }
            if sequence is not None:
                primitive["sequence"] = sequence
            if version >= 6 and r.read("<?"):
                primitive["textStyle"] = {
                    "horizontalAlignment": r.string(),
                    "verticalAlignment": r.string(),
                    "fontName": r.string(),
                    "isBold": r.read("<?"),
                    "height": r.read("<d"),
                }
            primitives.append(primitive)
        rows.append({
            "sourceDocument": source_document,
            "linkInstanceId": link_instance_id,
            "elementId": element_id,
            "uniqueId": unique_id,
            "category": category,
            "categoryKind": category_kind,
            "familyName": family_name,
            "typeName": type_name,
            "primitives": primitives,
        })
    if r.offset != len(r.data):
        raise ValueError(f"{len(r.data) - r.offset} trailing bytes")
    return {
        "format": "V2DG",
        "version": version,
        "document": document,
        "view": {
            "id": view_id,
            "name": view_name,
            "type": view_type,
            "scale": scale,
            "detailLevel": detail_level,
            "origin": axes[0],
            "right": axes[1],
            "up": axes[2],
            "direction": axes[3],
        },
        "elements": rows,
    }


def summary(capture):
    rows = capture["elements"]
    kinds = collections.Counter(p["kind"] for row in rows for p in row["primitives"])
    widths = collections.Counter(p["stroke"]["widthRaw"] for row in rows for p in row["primitives"])
    sources = collections.Counter(row["sourceDocument"] for row in rows)
    return {
        "view": capture["view"],
        "elementRows": len(rows),
        "primitives": sum(kinds.values()),
        "primitiveKinds": kinds,
        "strokeWidthsRaw": widths,
        "sourceDocuments": sources,
        "emptyElementRows": sum(not row["primitives"] for row in rows),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("capture", type=Path)
    parser.add_argument("--dump-json", type=Path, help="Write the full decoded capture")
    args = parser.parse_args()
    capture = decode(args.capture)
    print(json.dumps(summary(capture), indent=2))
    if args.dump_json:
        args.dump_json.write_text(json.dumps(capture, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
