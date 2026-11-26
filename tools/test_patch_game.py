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


class TvOSCompatibilityTests(unittest.TestCase):
    def test_gc_fix_and_audit_idempotent(self):
        from tvos_compat import repair_source
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            engine = root / "Monocle" / "Engine.cs"
            engine.parent.mkdir()
            engine.write_bytes(b"\xef\xbb\xbfvoid Start() {\r\n  GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;\r\n  GC.Collect();\r\n  Process.Start(\"example\");\r\n}\r\n")
            logs = []
            self.assertEqual(repair_source(root, logs.append), 1)
            result = engine.read_bytes()
            self.assertTrue(result.startswith(b"\xef\xbb\xbf"))
            self.assertIn(b"; // tvOS:", result)
            self.assertIn(b"\r\n  GC.Collect();", result)
            self.assertIn(b"Process.Start", result)
            self.assertTrue(any("Process / desktop APIs" in line for line in logs))
            self.assertEqual(repair_source(root, logs.append), 0)
            self.assertEqual(engine.read_bytes(), result)

    def test_unrecognized_assignment_fails_without_modification(self):
        from tvos_compat import repair_source
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            engine = root / "Monocle" / "Engine.cs"
            engine.parent.mkdir()
            text = "GCSettings.LatencyMode = GCLatencyMode.LowLatency;\nGCSettings.LatencyMode = ComputeMode();\n"
            engine.write_text(text, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Unreviewed GC latency"):
                repair_source(root)
            self.assertEqual(engine.read_text(encoding="utf-8"), text)


    def test_error_log_repair_and_entry_assembly_warning(self):
        from tvos_compat import repair_source
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            engine = root / "Monocle" / "Engine.cs"
            engine.parent.mkdir()
            engine.write_text("void Start() {}\n", encoding="utf-8")
            errorlog = engine.parent / "ErrorLog.cs"
            errorlog.write_text("void Open() {\n  Process.Start(Filename);\n}\n", encoding="utf-8")
            other = root / "Example.cs"
            other.write_text("var a = Assembly.GetEntryAssembly();\nProcess.Start(Filename);\n", encoding="utf-8")
            log = []
            self.assertEqual(repair_source(root, log.append), 1)
            self.assertIn("Console.Error.WriteLine", errorlog.read_text())
            self.assertIn("Process.Start", other.read_text())
            self.assertTrue(any("REVIEW loader entry-assembly" in line for line in log))
            self.assertEqual(repair_source(root, log.append), 0)

    def test_native_audit_reports_unmapped_missing_and_unrecognized(self):
        from tvos_compat import audit_native
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "Imports.cs").write_text(
                '[DllImport("fmod", EntryPoint = "FMOD_Actual")]\nprivate static extern int Alias();\n'
                '[DllImport("fmodstudio")]\nprivate static extern int Missing();\n'
                '[DllImport("fmod_SDL")]\nprivate static extern void LinuxOnly();\n'
                '[DllImport(LibraryConstant)]\nprivate static extern int Unknown();\n',
                encoding="utf-8")
            log = []
            with patch("tvos_compat.exported_symbols", side_effect=[set(), {"FMOD_Actual"}]):
                self.assertEqual(audit_native(root, root / "native", root / "fmod", log.append), (1, 1, 1, 1))
            self.assertTrue(any("MISSING" in line and "Missing" in line for line in log))
            self.assertTrue(any("UNMAPPED" in line and "LinuxOnly" in line for line in log))


    def test_confirmed_nested_tile_visibility_fix(self):
        from tvos_compat import repair_source
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "Monocle").mkdir()
            (root / "Monocle" / "Engine.cs").write_text("class Engine {}\n", encoding="utf-8")
            (root / "Celeste").mkdir()
            animated = root / "Celeste" / "AnimatedTiles.cs"
            animated.write_text("public class AnimatedTiles {\n  private class Tile {}\n  private int value;\n}\n", encoding="utf-8")
            self.assertEqual(repair_source(root), 1)
            self.assertIn("public class Tile", animated.read_text())
            self.assertIn("private int value", animated.read_text())
            self.assertEqual(repair_source(root), 0)
