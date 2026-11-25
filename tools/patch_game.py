"""Build user-supplied managed game sources using locally supplied patches.

Requires Git, dotnet and ilspycmd 8.0.0.7246-preview3 on PATH.
No game files or patches are downloaded or uploaded by this script.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import hashlib
from toolchain import environment, find

def run(args, cwd=None, check=True, capture=False):
    env = environment()
    args = list(args)
    executable = find(str(args[0]))
    if not executable:
        raise ValueError("Missing portable tool: " + str(args[0]))
    args[0] = executable
    return subprocess.run([str(a) for a in args], cwd=cwd, env=env, check=check,
                          capture_output=capture)

def normalize_lf(path):
    data = path.read_bytes()
    normalized = data.replace(b"\r\n", b"\n")
    if normalized != data:
        path.write_bytes(normalized)

def apply_patch(source, patch):
    # Windows ILSpy writes CRLF; upstream patches were generated on macOS.
    # Only tolerate whitespace in context, never skip a conflicting hunk.
    for options in ([], ["--ignore-space-change"]):
        args = ["git", "-c", "core.autocrlf=false", "apply", *options]
        if run(args + ["--check", patch], source, check=False, capture=True).returncode == 0:
            print("Applying", patch.name, "(whitespace-tolerant)" if options else "(exact)", flush=True)
            run(args + [patch], source)
            return
    if run(["git", "-c", "core.autocrlf=false", "apply", "--reverse", "--check", patch],
           source, check=False, capture=True).returncode == 0:
        print("Already applied:", patch.name, flush=True)
        return
    run(["git", "-c", "core.autocrlf=false", "apply", "--ignore-space-change", "--check", patch], source)

def build(staged, patches, output):
    staged, patches, output = (p.resolve() for p in (staged, patches, output))
    if output.exists():
        raise ValueError("Choose a new output directory")
    for tool in ("git", "dotnet", "ilspycmd"):
        if not find(tool):
            raise ValueError("Missing tool: " + tool + ". Run Setup-Toolchain.ps1 beside the toolkit first.")
    game = staged / "managed"
    for name in ("Celeste.exe", "FNA.dll", "System.Core.dll", "System.Xml.dll",
                 "mscorlib.dll", "Celeste.Content.dll"):
        if not (game / name).is_file():
            raise ValueError("Missing managed input: " + name)
    patch_names = ["crash-fixes.patch"]
    if (game / "Steamworks.NET.dll").is_file():
        patch_names = ["remove-steam.patch", "remove-steam2.patch"] + patch_names
    for name in patch_names:
        if not (patches / name).is_file():
            raise ValueError("Missing local patch: " + name)
    output.parent.mkdir(parents=True, exist_ok=True)
    root = output.parent / "patch-build"
    root.mkdir()
    print("Game SHA256:", hashlib.sha256((game / "Celeste.exe").read_bytes()).hexdigest(), flush=True)
    print("Keeping decompiled source and diagnostics locally in:", root, flush=True)
    source = root / "source"
    source.mkdir()
    run(["ilspycmd", "-o", source, "-p", "-d", "-r", game, game / "Celeste.exe"])
    for item in source.rglob("*"):
        if item.is_file() and item.suffix.lower() in (".cs", ".csproj"):
            normalize_lf(item)
    run(["git", "init", "-q"], source)
    local_patches = root / "patches"
    local_patches.mkdir()
    for name in patch_names:
        patch = local_patches / name
        shutil.copy2(patches / name, patch)
        normalize_lf(patch)
        apply_patch(source, patch)
    projects = list(source.glob("*.csproj")) + list(source.glob("*/*.csproj"))
    if len(projects) != 1:
        raise ValueError("Expected exactly one decompiled project")
    run(["dotnet", "publish", projects[0], "-c", "Release", "--disable-build-servers"], cwd=source)
    binaries = list(projects[0].parent.glob("bin/Release/**/Celeste.exe"))
    if not binaries:
        raise ValueError("No patched Celeste.exe produced")
    published = [p for p in binaries if "publish" in p.relative_to(source).parts]
    candidates = published or binaries
    if len(candidates) != 1:
        raise ValueError("Ambiguous compiled game outputs")
    result = root / "result"
    result.mkdir()
    shutil.copy2(candidates[0], result / "Celeste.dll")
    shutil.copy2(game / "Celeste.Content.dll", result / "Celeste.Content.dll")
    result.rename(output)
    print("Patched managed assemblies staged:", output)
    print("This is not a tvOS executable or a complete IPA.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("staged", "patches", "output"):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    try:
        build(args.staged, args.patches, args.output)
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        parser.exit(1, f"Local patch build failed: {exc}\n")
