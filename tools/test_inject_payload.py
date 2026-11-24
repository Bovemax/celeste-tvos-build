import pathlib
import plistlib
import tempfile
import unittest
import zipfile
from inject_payload import inject

class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = pathlib.Path(self.tmp.name)
        self.host = self.root / "host.ipa"
        self.dll = self.root / "Payload.dll"
        self.output = self.root / "result.ipa"
        self.dll.write_bytes(b"MZ" + b"synthetic fixture, not executable")

    def make_host(self, family=3, signed=False, old_payload=False):
        with zipfile.ZipFile(self.host, "w") as z:
            z.writestr("Payload/Host.app/Info.plist", plistlib.dumps(
                {"UIDeviceFamily": [family], "CFBundleExecutable": "Host"}))
            entry = zipfile.ZipInfo("Payload/Host.app/Host")
            entry.create_system = 3
            entry.external_attr = 0o100755 << 16
            z.writestr(entry, b"synthetic executable")
            if signed:
                z.writestr("Payload/Host.app/_CodeSignature/CodeResources", b"signed")
            if old_payload:
                z.writestr("Payload/Host.app/Payload.dll", b"old")

    def test_replacement_and_permissions(self):
        self.make_host(old_payload=True)
        inject(self.host, self.dll, self.output)
        with zipfile.ZipFile(self.output) as z:
            name = "Payload/Host.app/Payload.dll"
            self.assertEqual(z.namelist().count(name), 1)
            self.assertEqual(z.read(name), self.dll.read_bytes())
            self.assertEqual(z.getinfo("Payload/Host.app/Host").external_attr >> 16, 0o100755)
            self.assertIsNone(z.testzip())

    def test_wrong_platform(self):
        self.make_host(family=1)
        with self.assertRaises(ValueError):
            inject(self.host, self.dll, self.output)
        self.assertFalse(self.output.exists())

    def test_signed_host(self):
        self.make_host(signed=True)
        with self.assertRaises(ValueError):
            inject(self.host, self.dll, self.output)

    def test_preserve_input(self):
        self.make_host()
        with self.assertRaises(ValueError):
            inject(self.host, self.dll, self.host)

    def test_invalid_payload(self):
        self.make_host()
        self.dll.write_bytes(b"invalid")
        with self.assertRaises(ValueError):
            inject(self.host, self.dll, self.output)

if __name__ == "__main__":
    unittest.main()
