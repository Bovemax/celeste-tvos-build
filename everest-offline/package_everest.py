"""Assemble a local, unsigned Everest boot-test IPA using the existing packager."""
import argparse
import hashlib
import pathlib
import plistlib
import re
import shutil
import subprocess
import tempfile
import zipfile

REQUIRED = {"Celeste.dll", "Celeste.Content.dll", "FNA.dll", "MMHOOK_Celeste.dll"}
ID = "org.example.celestelocalhost.everesttest"
MANIFEST = "everest-payload.tsv"

def hash_file(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest().upper()

def read_payload(path):
    manifest = path / MANIFEST
    if not manifest.is_file() or manifest.stat().st_size > 32768:
        raise ValueError("Missing or oversized payload manifest")
    lines = manifest.read_text(encoding="utf-8").splitlines()
    if not 5 <= len(lines) <= 100 or lines[0] != "everest-tvos-boot-v1":
        raise ValueError("Unexpected payload manifest")
    result = {}
    seen = set()
    for line in lines[1:]:
        digest, name = line.split("\t")
        if not re.fullmatch(r"[A-Fa-f0-9]{64}", digest) or not re.fullmatch(r"[A-Za-z0-9_.-]{1,96}\.dll", name):
            raise ValueError("Invalid payload entry")
        key = name.casefold()
        if key in seen or key in {"system.dll", "mscorlib.dll", "netstandard.dll", "everesthost.dll"} or key.startswith("system."):
            raise ValueError("Duplicate or framework assembly: " + name)
        seen.add(key)
        f = path / name
        if not f.is_file() or f.resolve().parent != path.resolve() or hash_file(f) != digest.upper():
            raise ValueError("Payload missing or hash differs: " + name)
        with f.open("rb") as stream:
            if stream.read(2) != b"MZ":
                raise ValueError("Not a PE assembly: " + name)
        result[name] = f
    if not REQUIRED <= result.keys():
        raise ValueError("Incomplete payload")
    return result

def root_of(z):
    names = z.namelist()
    if len(names) != len({n.casefold() for n in names}):
        raise ValueError("Duplicate or case-colliding ZIP entries")
    roots = {n.split("/")[1] for n in names if n.startswith("Payload/") and len(n.split("/")) >= 3 and n.split("/")[1].endswith(".app")}
    if len(roots) != 1:
        raise ValueError("Expected one app root")
    root = "Payload/" + roots.pop() + "/"
    p = plistlib.loads(z.read(root + "Info.plist"))
    if p.get("CFBundleIdentifier") != ID or p.get("CelesteEverestBootVersion") != 1 or 3 not in p.get("UIDeviceFamily", []):
        raise ValueError("Only the isolated Everest host is supported")
    if any("/_CodeSignature/" in n or n.endswith("/embedded.mobileprovision") for n in names):
        raise ValueError("Expected unsigned IPA")
    return root

def append_payload(ipa, payload):
    files = read_payload(payload)
    with zipfile.ZipFile(ipa, "a", compression=zipfile.ZIP_DEFLATED) as z:
        root = root_of(z)
        seen = {n.casefold() for n in z.namelist()}
        for name, path in files.items():
            target = root + name
            if name in {"Celeste.dll", "Celeste.Content.dll"}:
                if hashlib.sha256(z.read(target)).hexdigest().upper() != hash_file(path):
                    raise ValueError("Base package game assembly differs: " + name)
            else:
                if target.casefold() in seen:
                    raise ValueError("Assembly collides with host: " + name)
                z.write(path, target)
        if (root + MANIFEST).casefold() in seen:
            raise ValueError("Manifest already exists")
        z.write(payload / MANIFEST, root + MANIFEST)
    with zipfile.ZipFile(ipa) as z:
        root = root_of(z)
        for name, path in files.items():
            if hashlib.sha256(z.read(root + name)).hexdigest().upper() != hash_file(path):
                raise ValueError("Final assembly differs: " + name)
        if z.testzip() is not None:
            raise ValueError("ZIP integrity check failed")

def package(root, host, native, output):
    root, host, native, output = [p.resolve() for p in (root, host, native, output)]
    payload = root / "work4" / "everest-boot-v1"
    read_payload(payload)
    if output.exists():
        raise ValueError("Choose a new output IPA")
    with zipfile.ZipFile(host) as z:
        root_of(z)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as tmp:
        candidate = pathlib.Path(tmp) / "candidate.ipa"
        subprocess.run([str(root / "CelestePackage.exe"), str(host), str(candidate),
            "--native", str(native), "--managed", str(payload),
            "--content", str(root / "inputs" / "Content"), "--fmod",
            str(root / "inputs" / "native" / "FMODLocal.dylib")], check=True, cwd=root)
        append_payload(candidate, payload)
        with output.open("xb") as dst, candidate.open("rb") as src:
            shutil.copyfileobj(src, dst)
    print("Prepared isolated unsigned Everest candidate:", output)
    print("Not a runtime compatibility pass. Do not install over vanilla or runtime-test apps.")

if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--root", type=pathlib.Path, default=pathlib.Path(r"C:\CelesteTV"))
    p.add_argument("--host", type=pathlib.Path, required=True)
    p.add_argument("--native", type=pathlib.Path, required=True)
    p.add_argument("--output", type=pathlib.Path, required=True)
    a = p.parse_args()
    try:
        package(a.root, a.host, a.native, a.output)
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError, zipfile.BadZipFile) as exc:
        p.exit(1, f"Everest package failed: {exc}\n")
