from pathlib import Path
import struct
import tempfile
import unittest
from link_fmod_local import observed_imports, write_stubs

class ImportStubTests(unittest.TestCase):
    def test_two_level_imports(self):
        def dylib(cmd, name):
            b = name.encode()+b"\0"
            b += b"\0"*((-len(b))%8)
            return struct.pack("<6I",cmd,24+len(b),24,0,0,0)+b
        commands = struct.pack("<6I",0x32,24,3,0xF0000,0x1A0500,0)
        commands += dylib(0xd,"@rpath/Reference.framework/Reference")
        commands += dylib(0xc,"/usr/lib/libSystem.B.dylib")
        commands += dylib(0xc,"/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")
        commands += dylib(0xc,"@rpath/Private.framework/Private")
        symoff = 32+len(commands)+24
        strings = b"\0_puts\0_AudioUnitInitialize\0_private\0"
        entries = b"".join(struct.pack("<IBBHQ",strings.index(s),1,0,i<<8,0)
                           for i,s in enumerate((b"_puts",b"_AudioUnitInitialize",b"_private"),1))
        commands += struct.pack("<6I",2,24,symoff,3,symoff+len(entries),len(strings))
        header = struct.pack("<8I",0xfeedfacf,0x100000c,0,6,6,len(commands),1,0)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            reference = root/"reference.dylib"
            reference.write_bytes(header+commands+entries+strings)
            imports = observed_imports(reference)
            self.assertEqual(imports["/usr/lib/libSystem.B.dylib"],{"_puts","dyld_stub_binder"})
            self.assertEqual(imports["/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox"],{"_AudioUnitInitialize"})
            self.assertEqual(len(imports),2)
            stubs = write_stubs(imports,root)
            self.assertEqual(len(stubs),2)
            self.assertTrue(all("arm64-tvos" in p.read_text() for p in stubs))
