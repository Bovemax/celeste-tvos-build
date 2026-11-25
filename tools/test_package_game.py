import pathlib
import plistlib
import struct
import tempfile
import unittest
import zipfile
from package_game import package

def synthetic_dylib(path, name, platform=3):
    # Original synthetic Mach-O header fixture; not executable.
    identity = ("@rpath/" + name + ".framework/" + name).encode() + b"\0"
    identity += b"\0" * ((-len(identity)) % 8)
    commands = struct.pack("<6I", 0x32, 24, platform, 0xF0000, 0x1A0000, 0)
    commands += struct.pack("<6I", 0xd, 24 + len(identity), 24, 0, 0, 0) + identity
    path.write_bytes(struct.pack("<8I", 0xfeedfacf, 0x100000c, 0, 6, 2, len(commands), 1, 0) + commands)

class GamePackageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = pathlib.Path(self.tmp.name)
        self.host = self.root / "host.ipa"
        self.managed = self.root / "managed"
        self.content = self.root / "Content"
        self.managed.mkdir()
        self.content.mkdir()
        for name in ("Celeste.dll", "Celeste.Content.dll"):
            (self.managed / name).write_bytes(b"MZfixture")
        (self.content / "example.txt").write_bytes(b"fixture")
        (self.content / "Monocle").mkdir()
        (self.content / "Monocle/MonocleDefault.xnb").write_bytes(b"XNBfixture")
        self.native = self.root / "CelesteNative.dylib"
        self.fmod = self.root / "FMODLocal.dylib"
        synthetic_dylib(self.native, "CelesteNative")
        synthetic_dylib(self.fmod, "FMODLocal")
        self.prefix = "Payload/Host.app/"
        self.output = self.root / "output.ipa"

    def make_host(self, version=2):
        with zipfile.ZipFile(self.host, "w") as z:
            z.writestr(self.prefix + "Info.plist", plistlib.dumps({
                "UIDeviceFamily": [3], "CFBundleExecutable": "Host",
                "CelesteLocalPayloadVersion": version}))
            info = zipfile.ZipInfo(self.prefix + "Host")
            info.create_system = 3
            info.external_attr = 0o100755 << 16
            z.writestr(info, b"fixture")

    def test_game_package(self):
        self.make_host()
        package(self.host, self.managed, self.content, self.output, self.native, self.fmod)
        with zipfile.ZipFile(self.output) as z:
            self.assertEqual(z.read(self.prefix + "GameAssemblies/Celeste.dll"), b"MZfixture")
            self.assertEqual(z.read(self.prefix + "GameAssemblies/Content/example.txt"), b"fixture")
            self.assertEqual(z.read(self.prefix + "Content/example.txt"), b"fixture")
            for prefix in ("Content/", "GameAssemblies/Content/"):
                self.assertEqual(z.read(self.prefix + prefix + "Monocle/MonocleDefault.xnb"), b"XNBfixture")
            for name in ("CelesteNative", "FMODLocal"):
                p = self.prefix + "Frameworks/" + name + ".framework/"
                self.assertEqual(z.getinfo(p + name).external_attr >> 16, 0o100755)
                self.assertEqual(plistlib.loads(z.read(p + "Info.plist"))["CFBundleExecutable"], name)
            self.assertEqual(z.getinfo(self.prefix + "Host").external_attr >> 16, 0o100755)
            self.assertIsNone(z.testzip())
        with self.assertRaises(ValueError):
            package(self.host, self.managed, self.content, self.output, self.native, self.fmod)

    def test_probe_package(self):
        self.make_host()
        package(self.host, None, None, self.output, self.native, probe=True)
        with zipfile.ZipFile(self.output) as z:
            self.assertIn(self.prefix + "GameAssemblies/Content/probe.txt", z.namelist())
            self.assertIn(self.prefix + "Content/probe.txt", z.namelist())
            self.assertNotIn(self.prefix + "GameAssemblies/Celeste.dll", z.namelist())

    def test_external_probe_with_optional_fmod(self):
        self.make_host()
        assembly = self.root / "ExternalProbe.dll"
        assembly.write_bytes(b"MZexternal")
        package(self.host, None, None, self.output, self.native, self.fmod,
                probe=True, probe_assembly=assembly, fmod_version="0x00020200")
        with zipfile.ZipFile(self.output) as z:
            self.assertEqual(z.read(self.prefix + "GameAssemblies/ExternalProbe.dll"), b"MZexternal")
            self.assertEqual(z.read(self.prefix + "GameAssemblies/fmod-version.txt"), b"00020200")
            self.assertNotIn(self.prefix + "GameAssemblies/Celeste.dll", z.namelist())

    def test_invalid_host_and_platform(self):
        self.make_host(1)
        with self.assertRaises(ValueError):
            package(self.host, None, None, self.output, self.native, probe=True)
        self.make_host()
        synthetic_dylib(self.native, "CelesteNative", platform=8)
        with self.assertRaises(ValueError):
            package(self.host, None, None, self.output, self.native, probe=True)
        self.assertFalse(self.output.exists())

    def test_missing_startup_asset(self):
        self.make_host()
        (self.content / "Monocle/MonocleDefault.xnb").unlink()
        with self.assertRaisesRegex(ValueError, "Missing exact-case startup asset"):
            package(self.host, self.managed, self.content, self.output, self.native, self.fmod)
        self.assertFalse(self.output.exists())

    def test_invalid_startup_asset_header(self):
        self.make_host()
        (self.content / "Monocle/MonocleDefault.xnb").write_bytes(b"bad")
        with self.assertRaisesRegex(ValueError, "Invalid startup XNB header"):
            package(self.host, self.managed, self.content, self.output, self.native, self.fmod)
        self.assertFalse(self.output.exists())

    def test_missing_fmod(self):
        self.make_host()
        with self.assertRaises(ValueError):
            package(self.host, self.managed, self.content, self.output, self.native)

if __name__ == "__main__":
    unittest.main()
