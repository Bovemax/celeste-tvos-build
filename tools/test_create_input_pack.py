import tempfile
import unittest
import zipfile
from pathlib import Path
from check_game_inputs import MANAGED, NATIVE, check
from create_input_pack import create
from stage_game_inputs import stage

class InputPackTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.managed, self.native, self.content = [self.root / x for x in ("managed", "native", "Content")]
        for folder in (self.managed, self.native, self.content):
            folder.mkdir()
        for name in MANAGED:
            (self.managed / name).write_bytes(b"MZsynthetic")
        for name in NATIVE:
            (self.native / name).write_bytes(b"!<arch>\nsynthetic")
        (self.content / "probe.txt").write_text("synthetic")

    def test_round_trip(self):
        (self.managed / "Steamworks.NET.dll").write_bytes(b"MZsynthetic")
        archive = create(self.managed, self.native, self.content, self.root / "inputs.zip")
        self.assertEqual(check(archive, self.content), [])
        staged = stage(archive, self.content, self.root / "staged")
        self.assertEqual((staged / "managed/Celeste.exe").read_bytes(), b"MZsynthetic")
        self.assertTrue((staged / "managed/Steamworks.NET.dll").is_file())
        self.assertEqual((staged / "Content/probe.txt").read_text(), "synthetic")
        with zipfile.ZipFile(archive) as z:
            self.assertNotIn("Content/probe.txt", z.namelist())

    def test_missing_input(self):
        (self.native / NATIVE[0]).unlink()
        out = self.root / "inputs.zip"
        with self.assertRaises(ValueError):
            create(self.managed, self.native, self.content, out)
        self.assertFalse(out.exists())

    def test_refuses_overwrite(self):
        out = self.root / "inputs.zip"
        out.write_bytes(b"keep")
        with self.assertRaises(ValueError):
            create(self.managed, self.native, self.content, out)
        self.assertEqual(out.read_bytes(), b"keep")
