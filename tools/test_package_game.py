import pathlib
import plistlib
import tempfile
import unittest
import zipfile
from package_game import package

class GamePackageTests(unittest.TestCase):
    def test_local_bundle(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            host = root / "host.ipa"
            managed = root / "managed"
            content = root / "Content"
            managed.mkdir()
            content.mkdir()
            for name in ("Celeste.dll", "Celeste.Content.dll"):
                (managed / name).write_bytes(b"MZfixture")
            (content / "example.txt").write_bytes(b"fixture")
            prefix = "Payload/Host.app/"
            def make_host(version):
                with zipfile.ZipFile(host, "w") as z:
                    z.writestr(prefix + "Info.plist", plistlib.dumps({
                        "UIDeviceFamily": [3], "CFBundleExecutable": "Host",
                        "CelesteLocalPayloadVersion": version}))
                    info = zipfile.ZipInfo(prefix + "Host")
                    info.create_system = 3
                    info.external_attr = 0o100755 << 16
                    z.writestr(info, b"fixture")
            out = root / "output.ipa"
            make_host(0)
            with self.assertRaises(ValueError):
                package(host, managed, content, out)
            self.assertFalse(out.exists())
            make_host(1)
            package(host, managed, content, out)
            with zipfile.ZipFile(out) as z:
                self.assertEqual(z.read(prefix + "GameAssemblies/Celeste.dll"), b"MZfixture")
                self.assertEqual(z.read(prefix + "Content/example.txt"), b"fixture")
                self.assertEqual(z.getinfo(prefix + "Host").external_attr >> 16, 0o100755)
                self.assertIsNone(z.testzip())
            with self.assertRaises(ValueError):
                package(host, managed, content, out)

if __name__ == "__main__":
    unittest.main()
