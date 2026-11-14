"""Compare two V2DG captures by source, link instance, view, and element.

Reports exact primitive-stream changes. A stable stream is evidence for
element-level replacement, not proof that Revit's unsaved model never changed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from .inspect import decode


def indexed(path: Path):
    capture = decode(path)
    rows = {}
    duplicates = 0
    for row in capture["elements"]:
        key = (row["sourceDocument"], row["linkInstanceId"], row["elementId"])
        if key in rows:
            duplicates += 1
            rows[key]["primitives"].extend(row["primitives"])
        else:
            rows[key] = row
    digests = {
        key: hashlib.sha256(json.dumps(
            row["primitives"], separators=(",", ":"), sort_keys=True
        ).encode()).hexdigest()
        for key, row in rows.items()
    }
    return capture["view"], rows, digests, duplicates


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    args = parser.parse_args()
    va, ra, da, duplicates_a = indexed(args.before)
    vb, rb, db, duplicates_b = indexed(args.after)
    if va["id"] != vb["id"] or va["name"] != vb["name"]:
        raise ValueError("captures have different views")
    added = sorted(db.keys() - da.keys())
    removed = sorted(da.keys() - db.keys())
    changed = sorted(key for key in da.keys() & db.keys() if da[key] != db[key])
    print(json.dumps({
        "view": va["name"],
        "beforeRows": len(ra),
        "afterRows": len(rb),
        "duplicateRowsBefore": duplicates_a,
        "duplicateRowsAfter": duplicates_b,
        "addedElements": len(added),
        "removedElements": len(removed),
        "changedElements": len(changed),
        "changedExamples": changed[:10],
    }, indent=2))


if __name__ == "__main__":
    main()
