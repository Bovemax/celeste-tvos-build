"""Orchestrate the experimental Windows-only Celeste preparation and packaging.

Uses user-owned local inputs and a prebuilt version 2 game host. Nothing is
uploaded. FMOD integration and device execution are not yet verified.
"""
import argparse
import pathlib
import subprocess
from stage_game_inputs import stage
from patch_game import build
from link_native import link, verify_tvos_dylib
from package_game import package

def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("archive", "content", "patches", "host", "native", "work", "output"):
        p.add_argument(name, type=pathlib.Path)
    p.add_argument("--fmod", type=pathlib.Path, help="Existing compatible FMODLocal framework binary")
    p.add_argument("--sdk", type=pathlib.Path, help="Lawfully available local Apple TV SDK linker root")
    p.add_argument("--sdk-version")
    a = p.parse_args()
    try:
        work = a.work.resolve()
        if work.exists() or a.output.exists():
            raise ValueError("Choose new working and output paths")
        if a.output.resolve().is_relative_to(work):
            raise ValueError("Output must be outside the working directory")
        verify_tvos_dylib(a.native, "@rpath/CelesteNative.framework/CelesteNative")
        if a.fmod:
            verify_tvos_dylib(a.fmod, "@rpath/FMODLocal.framework/FMODLocal")
        elif not a.sdk or not a.sdk_version:
            raise ValueError("Supply a compatible local FMOD framework, or an SDK linker root and SDK version")
        work.mkdir(parents=True)
        staged = stage(a.archive, a.content, work / "staged")
        fmod = a.fmod
        if not fmod:
            fmod = link([staged / "native/libfmod.a", staged / "native/libfmodstudio.a"],
                        work / "FMODLocal.dylib", a.sdk,
                        ["AudioToolbox", "AVFoundation", "Foundation", "CoreAudio"],
                        ["System", "c++"], framework_name="FMODLocal", sdk_version=a.sdk_version)
        patched = work / "patched"
        build(staged, a.patches, patched)
        result = package(a.host, patched, staged / "Content", a.output, a.native, fmod)
        print("Experimental unsigned package:", result)
        print("Requires recursive signing and Apple TV testing. Keep all local game files private.")
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as e:
        p.exit(1, f"Local build failed: {e}\n")

if __name__ == "__main__":
    main()
