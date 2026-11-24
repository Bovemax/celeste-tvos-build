"""Package local game files and native frameworks into a version 2 tvOS host.

All inputs remain local. Output is unsigned; packaging is not runtime validation.
"""
import argparse
import pathlib
import plistlib
import shutil
import tempfile
import zipfile
from link_native import verify_tvos_dylib

FRAMEWORKS = ("CelesteNative", "FMODLocal")

def framework_info(name):
    return plistlib.dumps({
        "CFBundleIdentifier": "org.example.celestelocalhost." + name.lower(),
        "CFBundleExecutable": name, "CFBundleName": name,
        "CFBundlePackageType": "FMWK", "CFBundleVersion": "1",
        "CFBundleShortVersionString": "1.0",
        "CFBundleSupportedPlatforms": ["AppleTVOS"],
        "MinimumOSVersion": "15.0", "UIDeviceFamily": [3],
    })

def package(host, managed, content, output, native, fmod=None, probe=False):
    host, output, native = (p.resolve() for p in (host, output, native))
    inputs = [host, native]
    if output.exists():
        raise ValueError("Choose a new output file")
    if not probe:
        if managed is None or content is None or fmod is None:
            raise ValueError("Game mode requires patched assemblies, Content and FMOD framework")
        managed, content, fmod = (p.resolve() for p in (managed, content, fmod))
        if output.is_relative_to(content) or output.is_relative_to(managed):
            raise ValueError("Output must be outside input directories")
        inputs += [managed, content, fmod]
    if output in inputs:
        raise ValueError("Output cannot overwrite an input")
    frameworks = {"CelesteNative": native}
    if fmod is not None:
        frameworks["FMODLocal"] = fmod
    for name, lib in frameworks.items():
        verify_tvos_dylib(lib, "@rpath/" + name + ".framework/" + name)

    additions = {}
    if not probe:
        for name in ("Celeste.dll", "Celeste.Content.dll"):
            path = managed / name
            with path.open("rb") as f:
                if f.read(2) != b"MZ":
                    raise ValueError("Invalid PE assembly: " + name)
            additions["GameAssemblies/" + name] = path
        files = sorted(p for p in content.rglob("*") if p.is_file())
        if not files:
            raise ValueError("Missing or empty Content")
        for p in files:
            if not p.resolve().is_relative_to(content):
                raise ValueError("Content links outside source tree: " + str(p))
            additions["GameAssemblies/Content/" + p.relative_to(content).as_posix()] = p

    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(host) as src:
        names = src.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Duplicate ZIP entries")
        if any(n.startswith("/") or "\\" in n or ".." in n.split("/") for n in names):
            raise ValueError("Unsafe archive path")
        roots = {n.split("/")[1] for n in names if n.startswith("Payload/")
                 and len(n.split("/")) >= 3 and n.split("/")[1].endswith(".app")}
        if len(roots) != 1:
            raise ValueError("Expected exactly one app")
        root = "Payload/" + roots.pop() + "/"
        metadata = plistlib.loads(src.read(root + "Info.plist"))
        if 3 not in metadata.get("UIDeviceFamily", []) or metadata.get("CelesteLocalPayloadVersion") != 2:
            raise ValueError("Expected version 2 tvOS game host")
        if root + metadata["CFBundleExecutable"] not in names:
            raise ValueError("Missing host executable")
        if any("/_CodeSignature/" in n or n.endswith("/embedded.mobileprovision") for n in names):
            raise ValueError("Host must be unsigned")
        if any(n.startswith(root + "GameAssemblies/") and not n.endswith("/") for n in names):
            raise ValueError("Host already contains a payload")
        for name in frameworks:
            if any(n.startswith(root + "Frameworks/" + name + ".framework/") for n in names):
                raise ValueError("Host already contains framework " + name)
        with tempfile.TemporaryDirectory(dir=output.parent) as tmp:
            target = pathlib.Path(tmp) / "game.ipa"
            with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as dest:
                for entry in src.infolist():
                    with src.open(entry) as inp, dest.open(entry, "w") as out:
                        shutil.copyfileobj(inp, out)
                for name, path in additions.items():
                    dest.write(path, root + name)
                if probe:
                    dest.writestr(root + "GameAssemblies/Content/probe.txt", "Original FNA content probe")
                for name, lib in frameworks.items():
                    prefix = root + "Frameworks/" + name + ".framework/"
                    info = zipfile.ZipInfo(prefix + name)
                    info.create_system = 3
                    info.external_attr = 0o100755 << 16
                    with lib.open("rb") as inp, dest.open(info, "w") as out:
                        shutil.copyfileobj(inp, out)
                    dest.writestr(prefix + "Info.plist", framework_info(name))
            with zipfile.ZipFile(target) as result:
                if result.testzip() is not None:
                    raise ValueError("Archive integrity check failed")
            with output.open("xb") as out, target.open("rb") as inp:
                shutil.copyfileobj(inp, out)
    return output

if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("host", type=pathlib.Path)
    p.add_argument("output", type=pathlib.Path)
    p.add_argument("--native", type=pathlib.Path, required=True)
    p.add_argument("--fmod", type=pathlib.Path)
    p.add_argument("--managed", type=pathlib.Path)
    p.add_argument("--content", type=pathlib.Path)
    p.add_argument("--probe", action="store_true")
    a = p.parse_args()
    try:
        print("Created unsigned local package:", package(a.host, a.managed, a.content, a.output, a.native, a.fmod, a.probe))
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        p.exit(1, f"Packaging failed: {exc}\n")
