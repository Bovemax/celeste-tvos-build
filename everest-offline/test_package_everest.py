import pathlib
import tempfile
import unittest
import zipfile
import plistlib
import package_everest as p

class PackageTests(unittest.TestCase):
    def test_isolated_manifest_and_collision(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder=pathlib.Path(tmp)
            payload=folder/"payload"
            payload.mkdir()
            for name in p.REQUIRED:
                (payload/name).write_bytes(b"MZ"+name.encode())
            manifest=payload/p.MANIFEST
            manifest.write_text("everest-tvos-boot-v1\n"+"\n".join(p.hash_file(payload/name)+"\t"+name for name in sorted(p.REQUIRED))+"\n")
            ipa=folder/"candidate.ipa"
            root="Payload/EverestHost.app/"
            with zipfile.ZipFile(ipa,"w") as z:
                z.writestr(root+"Info.plist",plistlib.dumps({"CFBundleIdentifier":p.ID,"CelesteEverestBootVersion":1,"UIDeviceFamily":[3]}))
                for name in ("Celeste.dll","Celeste.Content.dll"):
                    z.write(payload/name,root+name)
            p.append_payload(ipa,payload)
            with zipfile.ZipFile(ipa) as z:
                self.assertIn(root+"FNA.dll",z.namelist())
                self.assertIn(root+p.MANIFEST,z.namelist())
            with self.assertRaises(ValueError):
                p.append_payload(ipa,payload)
            (payload/"FNA.dll").write_bytes(b"MZmodified")
            with self.assertRaises(ValueError):
                p.read_payload(payload)
    def test_reject_production_host(self):
        with tempfile.TemporaryDirectory() as tmp:
            ipa=pathlib.Path(tmp)/"wrong.ipa"
            with zipfile.ZipFile(ipa,"w") as z:
                z.writestr("Payload/GameHost.app/Info.plist",plistlib.dumps({"CFBundleIdentifier":"org.example.celestelocalhost","UIDeviceFamily":[3]}))
            with zipfile.ZipFile(ipa) as z, self.assertRaises(ValueError):
                p.root_of(z)

if __name__ == "__main__":
    unittest.main()
