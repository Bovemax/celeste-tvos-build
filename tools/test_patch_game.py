import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from patch_game import normalize_lf, apply_patch

class PatchTests(unittest.TestCase):
    def test_windows_line_endings_and_strict_conflict(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "source"
            source.mkdir()
            target = source / "Example.cs"
            target.write_bytes(b"one\r\n    old\r\nthree\r\n")
            diff = root / "change.patch"
            diff.write_bytes(b"diff --git a/Example.cs b/Example.cs\r\n--- a/Example.cs\r\n+++ b/Example.cs\r\n@@ -1,3 +1,3 @@\r\n one\r\n-    old\r\n+    new\r\n three\r\n")
            normalize_lf(target)
            normalize_lf(diff)
            subprocess.run(["git","init","-q",str(source)],check=True)
            apply_patch(source,diff)
            self.assertEqual(target.read_bytes().replace(b"\r\n",b"\n"),b"one\n    new\nthree\n")
            apply_patch(source,diff)
            target.write_bytes(b"one\n    incompatible\nthree\n")
            with self.assertRaises(subprocess.CalledProcessError):
                apply_patch(source,diff)
            self.assertIn(b"incompatible",target.read_bytes())
