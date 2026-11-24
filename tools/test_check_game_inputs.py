import hashlib
import json
import pathlib
import tempfile
import unittest
import zipfile
from check_game_inputs import check, MANAGED, NATIVE

class InputTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = pathlib.Path(tmp.name)
        self.archive = root / "inputs.zip"
        self.content = root / "Content"
        self.content.mkdir()
        (self.content / "fixture.txt").write_text("original test data")
        self.files = {"managed/" + n: b"MZfixture" for n in MANAGED}
        self.files.update({"native/" + n: b"!<arch>\nfixture" for n in NATIVE})

    def write(self, corrupt=None):
        manifest = {"inputs": {n: {"sha256": hashlib.sha256(b).hexdigest()}
                               for n, b in self.files.items()}}
        with zipfile.ZipFile(self.archive, "w") as z:
            for n, b in self.files.items():
                z.writestr(n, b + (b"changed" if n == corrupt else b""))
            z.writestr("manifest.json", json.dumps(manifest))

    def test_valid(self):
        self.write()
        self.assertEqual(check(self.archive, self.content), [])

    def test_missing(self):
        del self.files["managed/Celeste.exe"]
        self.write()
        self.assertIn("Missing: managed/Celeste.exe", check(self.archive, self.content))

    def test_corrupt(self):
        self.write("managed/Celeste.exe")
        self.assertIn("Missing or mismatched SHA256: managed/Celeste.exe",
                      check(self.archive, self.content))

    def test_empty_content(self):
        (self.content / "fixture.txt").unlink()
        self.write()
        self.assertIn("Content directory is missing or empty", check(self.archive, self.content))

    def test_wrong_archive(self):
        self.files["native/libfmod.a"] = b"wrong"
        self.write()
        self.assertIn("Not a standard static archive: native/libfmod.a",
                      check(self.archive, self.content))

if __name__ == "__main__":
    unittest.main()
