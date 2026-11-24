"""Package local patched assemblies and Content into an unsigned game host.

This does not compile native code, link FMOD or sign an application.
The interpreter smoke-test host is intentionally not supported.
"""
import argparse
import pathlib
import plistlib
import shutil
import tempfile
import zipfile

def package(host, managed, content, output):
    host, managed, content, output = (p.resolve() for p in (host, managed, content, output))
    if output.exists() or output.is_relative_to(content) or output.is_relative_to(managed):
        raise ValueError("Choose a new output file outside the input directories")
    assemblies = ("Celeste.dll", "Celeste.Content.dll")
    for name in assemblies:
        path = managed / name
        if not path.is_file():
            raise ValueError("Missing patched assembly: " + name)
        with path.open("rb") as f:
            if f.read(2) != b"MZ":
                raise ValueError("Invalid PE assembly: " + name)
    files = sorted(p for p in content.rglob("*") if p.is_file())
    if not files:
        raise ValueError("Missing or empty Content")
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(host) as src:
        names = src.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Duplicate ZIP entries")
        roots = {n.split("/")[1] for n in names if n.startswith("Payload/")
                 and len(n.split("/")) >= 3 and n.split("/")[1].endswith(".app")}
        if len(roots) != 1:
            raise ValueError("Expected exactly one app")
        root = "Payload/" + roots.pop() + "/"
        metadata = plistlib.loads(src.read(root + "Info.plist"))
        if 3 not in metadata.get("UIDeviceFamily", []):
            raise ValueError("Expected tvOS host")
        if root + metadata["CFBundleExecutable"] not in names:
            raise ValueError("Missing host executable")
        if any(n.startswith(root + "_CodeSignature/") or
               n == root + "embedded.mobileprovision" for n in names):
            raise ValueError("Host must be unsigned")
        # Explicit contract for a future game host; not a claim of compatibility.
        if metadata.get("CelesteLocalPayloadVersion") != 1:
            raise ValueError("Host does not declare the Celeste local payload contract")
        additions = {root + "GameAssemblies/" + n: managed / n for n in assemblies}
        additions.update({root + "Content/" + p.relative_to(content).as_posix(): p for p in files})
        if any(n.startswith((root + "GameAssemblies/", root + "Content/")) and not n.endswith("/")
               for n in names):
            raise ValueError("Host already contains game payload files")
        with tempfile.TemporaryDirectory(dir=output.parent) as tmp:
            target = pathlib.Path(tmp) / "game.ipa"
            with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as dest:
                for entry in src.infolist():
                    with src.open(entry) as inp, dest.open(entry, "w") as out:
                        shutil.copyfileobj(inp, out)
                for name, path in additions.items():
                    dest.write(path, name)
            with zipfile.ZipFile(target) as result:
                if result.testzip() is not None:
                    raise ValueError("Archive integrity check failed")
            with output.open("xb") as out, target.open("rb") as inp:
                shutil.copyfileobj(inp, out)
    return output

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("host", "managed", "content", "output"):
        parser.add_argument(name, type=pathlib.Path)
    args = parser.parse_args()
    try:
        print("Created unsigned local package:", package(args.host, args.managed, args.content, args.output))
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        parser.exit(1, f"Packaging failed: {exc}\n")
