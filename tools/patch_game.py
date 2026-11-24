"""Build user-supplied managed game sources using locally supplied patches.

Requires Git, dotnet and ilspycmd 8.0.0.7246-preview3 on PATH.
No game files or patches are downloaded or uploaded by this script.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from toolchain import environment, find

def run(args, cwd=None):
    env = environment()
    args = list(args)
    executable = find(str(args[0]))
    if not executable:
        raise ValueError("Missing portable tool: " + str(args[0]))
    args[0] = executable
    subprocess.run([str(a) for a in args], cwd=cwd, env=env, check=True)

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
    with tempfile.TemporaryDirectory(dir=output.parent) as tmp:
        root = Path(tmp)
        source = root / "source"
        source.mkdir()
        run(["ilspycmd", "-o", source, "-p", "-d", "-r", game, game / "Celeste.exe"])
        run(["git", "init", "-q"], source)
        for name in patch_names:
            patch = patches / name
            run(["git", "apply", "--check", patch], source)
            run(["git", "apply", patch], source)
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
