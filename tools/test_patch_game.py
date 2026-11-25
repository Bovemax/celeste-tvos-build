import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from patch_game import normalize_lf, apply_patch, remove_steam_reference
import xml.etree.ElementTree as ET

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

    def test_remove_only_steam_reference_independent_of_hint_path(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            project = root / "Celeste.csproj"
            project.write_text('<Project><ItemGroup><Reference Include="Steamworks.NET"><HintPath>..\\different\\Steamworks.NET.dll</HintPath></Reference><Reference Include="FNA"><HintPath>FNA.dll</HintPath></Reference></ItemGroup></Project>', encoding="utf-8")
            diff = root / "change.patch"
            diff.write_text('diff --git a/Celeste.csproj b/Celeste.csproj\n--- a/Celeste.csproj\n+++ b/Celeste.csproj\n@@ -1,3 +0,0 @@\n-<Reference Include="Steamworks.NET">\n-<HintPath>../other/Steamworks.NET.dll</HintPath>\n-</Reference>\n', encoding="utf-8")
            self.assertTrue(remove_steam_reference(root, diff))
            self.assertEqual([r.get("Include") for r in ET.parse(project).iter("Reference")], ["FNA"])
            self.assertTrue(remove_steam_reference(root, diff))
            diff.write_text(diff.read_text() + "+<UnexpectedChange />\n")
            self.assertFalse(remove_steam_reference(root, diff))
