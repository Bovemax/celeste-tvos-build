import hashlib
import json
import pathlib
import tempfile
import unittest
import zipfile
from check_game_inputs import MANAGED, NATIVE
from stage_game_inputs import stage

class StagingTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = pathlib.Path(tmp.name)
        self.archive = self.root / "inputs.zip"
        self.content = self.root / "Content"
        self.content.mkdir()
        (self.content / "nested").mkdir()
        (self.content / "nested" / "fixture.txt").write_bytes(b"original content")
        self.output = self.root / "staged"
        self.files = {"managed/" + n: b"MZfixture" for n in MANAGED}
        self.files.update({"native/" + n: b"!<arch>\nfixture" for n in NATIVE})

    def write_archive(self):
        manifest = {"inputs": {n: {"sha256": hashlib.sha256(b).hexdigest()}
                               for n, b in self.files.items()}}
        with zipfile.ZipFile(self.archive, "w") as z:
            for n, b in self.files.items():
                z.writestr(n, b)
            z.writestr("manifest.json", json.dumps(manifest))

    def test_staging_preserves_originals(self):
        self.write_archive()
        before = self.archive.read_bytes()
        self.assertEqual(stage(self.archive, self.content, self.output), self.output.resolve())
        for n, b in self.files.items():
            self.assertEqual((self.output / n).read_bytes(), b)
        self.assertEqual((self.output / "Content/nested/fixture.txt").read_bytes(), b"original content")
        self.assertEqual(self.archive.read_bytes(), before)
        self.assertEqual((self.content / "nested/fixture.txt").read_bytes(), b"original content")

    def test_existing_output_rejected(self):
        self.write_archive()
        self.output.mkdir()
        with self.assertRaises(ValueError):
            stage(self.archive, self.content, self.output)

    def test_nested_output_rejected(self):
        self.write_archive()
        with self.assertRaises(ValueError):
            stage(self.archive, self.content, self.content / "staged")

    def test_missing_input_rejected(self):
        del self.files["managed/Celeste.exe"]
        self.write_archive()
        with self.assertRaises(ValueError):
            stage(self.archive, self.content, self.output)
        self.assertFalse(self.output.exists())

    def test_unexpected_archive_paths_not_extracted(self):
        self.files["../../unexpected.txt"] = b"not a permitted input"
        self.write_archive()
        stage(self.archive, self.content, self.output)
        self.assertFalse((self.root / "unexpected.txt").exists())
        self.assertFalse((self.output / "unexpected.txt").exists())

if __name__ == "__main__":
    unittest.main()
