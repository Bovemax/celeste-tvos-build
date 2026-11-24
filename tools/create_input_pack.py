"""Prepare a private local input ZIP from game and tvOS FMOD directories."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import zipfile
from check_game_inputs import MANAGED, NATIVE, check

def create(managed, native, content, output):
    managed, native, content, output = (p.resolve() for p in (managed, native, content, output))
    if output.exists() or any(output.is_relative_to(p) for p in (managed, native, content)):
        raise ValueError("Choose a new output outside the input directories")
    members = {f"managed/{n}": managed / n for n in MANAGED}
    members.update({f"native/{n}": native / n for n in NATIVE})
    if (managed / "Steamworks.NET.dll").is_file():
        members["managed/Steamworks.NET.dll"] = managed / "Steamworks.NET.dll"
    for name, path in members.items():
        if not path.is_file():
            raise ValueError("Missing local input: " + str(path))
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as tmp:
        archive = Path(tmp) / "inputs.zip"
        manifest = {"inputs": {}}
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as z:
            for name, path in members.items():
                digest = hashlib.sha256()
                with path.open("rb") as src, z.open(name, "w") as dst:
                    for chunk in iter(lambda: src.read(1024 * 1024), b""):
                        digest.update(chunk)
                        dst.write(chunk)
                manifest["inputs"][name] = {"sha256": digest.hexdigest()}
            z.writestr("manifest.json", json.dumps(manifest, indent=2))
        errors = check(archive, content)
        if errors:
            raise ValueError("\n".join(errors))
        with output.open("xb") as dst, archive.open("rb") as src:
            shutil.copyfileobj(src, dst)
    return output

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("managed", "native", "content", "output"):
        parser.add_argument(name, type=Path)
    a = parser.parse_args()
    try:
        print("Created private input ZIP:", create(a.managed, a.native, a.content, a.output))
        print("Keep this ZIP local; it contains your game and FMOD files.")
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        parser.exit(1, f"Input preparation failed: {exc}\n")
