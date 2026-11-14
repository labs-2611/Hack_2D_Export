import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from v2dg.compare import indexed
from v2dg.atlas import compact
from v2dg.inspect import decode
from v2dg.layer import compose


SAMPLES = Path(__file__).resolve().parents[1] / "samples"
BASE = SAMPLES / "two-door-tags-base.v2dg"
MOVED = SAMPLES / "two-door-tags-moved.v2dg"
EXAMPLES = SAMPLES.parent / "examples"


class V2DGFormatTest(unittest.TestCase):
    def test_real_revit_sample(self):
        capture = decode(BASE)
        self.assertEqual(capture["format"], "V2DG")
        self.assertEqual(capture["version"], 6)
        self.assertEqual(capture["view"]["id"], 1350701)
        self.assertEqual({row["elementId"] for row in capture["elements"]}, {2242835, 2242836})
        self.assertEqual(sum(len(row["primitives"]) for row in capture["elements"]), 10)
        self.assertEqual(sum(p["kind"] == "text" for row in capture["elements"] for p in row["primitives"]), 2)
        self.assertTrue(all(p["textStyle"]["fontName"] for row in capture["elements"] for p in row["primitives"] if p["kind"] == "text"))

    def test_truncated_capture_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "broken.v2dg"
            path.write_bytes(BASE.read_bytes()[:-3])
            with self.assertRaises(ValueError):
                decode(path)

    def test_two_element_change(self):
        before_view, before_rows, before_hashes, before_duplicates = indexed(BASE)
        after_view, after_rows, after_hashes, after_duplicates = indexed(MOVED)
        self.assertEqual(before_view["id"], after_view["id"])
        self.assertEqual(before_rows.keys(), after_rows.keys())
        self.assertEqual(before_duplicates, 0)
        self.assertEqual(after_duplicates, 0)
        changed = {key[2] for key in before_hashes if before_hashes[key] != after_hashes[key]}
        self.assertEqual(changed, {2242835, 2242836})

    def test_web_layer_composes_with_exact_base(self):
        base_bytes = (EXAMPLES / "two-door-tags-atlas.json").read_bytes()
        expected = json.dumps(compact(decode(BASE)), separators=(",", ":")).encode()
        self.assertEqual(base_bytes, expected)
        layer = json.loads((EXAMPLES / "web-annotation-layer.json").read_text(encoding="utf-8"))
        self.assertEqual(layer["baseSha256"], hashlib.sha256(base_bytes).hexdigest())
        composed = compose(base_bytes, [layer])
        self.assertEqual(len(composed["elements"]), 3)
        self.assertEqual(composed["elements"][-1]["r"], -1)
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            compose(base_bytes + b" ", [layer])

    def test_layer_delete_after_add(self):
        base_bytes = (EXAMPLES / "two-door-tags-atlas.json").read_bytes()
        added = json.loads((EXAMPLES / "web-annotation-layer.json").read_text(encoding="utf-8"))
        deleted = {"format": added["format"], "viewId": added["viewId"],
                   "baseSha256": added["baseSha256"],
                   "changes": [{"op": "delete", "key": added["changes"][0]["key"]}]}
        self.assertEqual(len(compose(base_bytes, [added, deleted])["elements"]), 2)


if __name__ == "__main__":
    unittest.main()
