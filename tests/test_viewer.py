import hashlib
import json
import subprocess
import sys
import unittest
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import urlopen

from viewer.__main__ import build_routes


ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "samples" / "two-door-tags-base.v2dg"
REVISION = ROOT / "samples" / "two-door-tags-moved.v2dg"


class ViewerTest(unittest.TestCase):
    def test_revision_replaces_rows_at_original_draw_positions(self):
        routes = build_routes([BASE], REVISION)
        base = routes["/atlas-view/1350701.json"][0]
        overlay = json.loads(routes["/atlas-overlay.json"][0])
        self.assertEqual(overlay["baseSha256"], hashlib.sha256(base).hexdigest())
        self.assertEqual([change["op"] for change in overlay["changes"]], ["replace", "replace"])
        self.assertEqual([[p[4] for p in change["element"]["p"]] for change in overlay["changes"]],
                         [[0, 1, 2, 3, 4], [5, 6, 7, 8, 9]])

    def test_server_serves_viewer_and_capture(self):
        process = subprocess.Popen(
            [sys.executable, "-m", "viewer", "--no-browser", "--port", "0"],
            cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        )
        try:
            address = process.stdout.readline().strip().split(": ", 1)[1]
            with urlopen(address, timeout=5) as response:
                self.assertIn(b"V2DG DRAWING VIEWER", response.read())
            with urlopen(address + "atlas-manifest.json", timeout=5) as response:
                self.assertEqual(json.load(response)["views"][0]["elements"], 2)
            with self.assertRaises(HTTPError) as error:
                urlopen(address + "missing.json", timeout=5)
            self.assertEqual(error.exception.code, 404)
        finally:
            process.terminate()
            process.communicate(timeout=5)


if __name__ == "__main__":
    unittest.main()
