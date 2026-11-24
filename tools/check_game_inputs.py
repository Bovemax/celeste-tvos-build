"""Check local game/build inputs without uploading or modifying them."""
import argparse
import pathlib
import zipfile
import json
import hashlib

MANAGED = ("Celeste.exe", "FNA.dll", "System.Core.dll", "System.Xml.dll",
           "mscorlib.dll", "Celeste.Content.dll")
NATIVE = ("libfmod.a", "libfmodstudio.a")

def check(archive, content):
    errors = []
    required = ["managed/" + n for n in MANAGED] + ["native/" + n for n in NATIVE]
    with zipfile.ZipFile(archive) as z:
        names = z.namelist()
        if len(names) != len(set(names)):
            errors.append("Duplicate archive entries")
        manifest = json.loads(z.read("manifest.json"))
        for name in required:
            if name not in names:
                errors.append("Missing: " + name)
                continue
            data = z.read(name)
            expected = manifest.get("inputs", {}).get(name, {}).get("sha256")
            if not expected or hashlib.sha256(data).hexdigest() != expected:
                errors.append("Missing or mismatched SHA256: " + name)
            if name.startswith("managed/") and not data.startswith(b"MZ"):
                errors.append("Not a PE file: " + name)
            if name.startswith("native/") and not data.startswith(b"!<arch>\n"):
                errors.append("Not a standard static archive: " + name)
    if not content.is_dir() or not any(p.is_file() for p in content.rglob("*")):
        errors.append("Content directory is missing or empty")
    return errors

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=pathlib.Path)
    parser.add_argument("content", type=pathlib.Path)
    args = parser.parse_args()
    try:
        errors = check(args.archive, args.content)
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        parser.exit(1, f"Invalid inputs: {exc}\n")
    if errors:
        parser.exit(1, "\n".join(errors) + "\n")
    print("Input preflight passed (not a compatibility or architecture test).")
