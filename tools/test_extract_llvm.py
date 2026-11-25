import io
import pathlib
import tarfile
import tempfile
import unittest
from extract_llvm import extract

def fixture(path):
    with tarfile.open(path, "w:xz") as archive:
        for name, payload in [
            ("llvm/bin/lld.exe", b"synthetic linker"),
            ("llvm/bin/libLLVM.dll", b"synthetic library"),
            ("llvm/bin/llvm-nm.exe", b"synthetic symbol reader"),
            ("llvm/LICENSE.TXT", b"license"),
            ("llvm/bin/clang.exe", b"not needed"),
            ("llvm/../../outside.txt", b"not allowed"),
        ]:
            info = tarfile.TarInfo(name)
            info.size = len(payload)
            archive.addfile(info, io.BytesIO(payload))
        info = tarfile.TarInfo("llvm/bin/ld64.lld.exe")
        info.type = tarfile.LNKTYPE
        info.linkname = "llvm/bin/lld.exe"
        archive.addfile(info)

class ExtractTests(unittest.TestCase):
    def test_portable_extraction(self):
        with tempfile.TemporaryDirectory(prefix="toolkit path with spaces ") as tmp:
            root = pathlib.Path(tmp)
            archive = root / "archive.tar.xz"
            fixture(archive)
            output = extract(archive, root / "LLVM")
            self.assertEqual((output / "bin/lld.exe").read_bytes(), b"synthetic linker")
            self.assertEqual((output / "bin/ld64.lld.exe").read_bytes(), b"synthetic linker")
            self.assertEqual((output / "bin/libLLVM.dll").read_bytes(), b"synthetic library")
            self.assertEqual((output / "bin/llvm-nm.exe").read_bytes(), b"synthetic symbol reader")
            self.assertTrue((output / "LICENSE.TXT").exists())
            self.assertFalse((output / "bin/clang.exe").exists())
            self.assertFalse((root / "outside.txt").exists())
            with self.assertRaises(ValueError):
                extract(archive, output)
