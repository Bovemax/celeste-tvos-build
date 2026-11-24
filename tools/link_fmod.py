"""Experimental, local-only conversion of matching FMOD tvOS archives.

Requires a compatible, lawfully available tvOS SDK linker root and LLVM LLD.
Does not distribute FMOD or claim that FMOD supports this framework layout.
"""
import argparse
from pathlib import Path
import subprocess
from link_native import link

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("core", type=Path)
    p.add_argument("studio", type=Path)
    p.add_argument("sdk", type=Path)
    p.add_argument("output", type=Path)
    p.add_argument("--sdk-version", required=True)
    p.add_argument("--framework", action="append", default=[])
    p.add_argument("--library", action="append", default=[])
    a = p.parse_args()
    try:
        print(link([a.core, a.studio], a.output, a.sdk,
                   ["AudioToolbox", "AVFoundation", "Foundation", "CoreAudio"] + a.framework,
                   ["System", "c++"] + a.library,
                   framework_name="FMODLocal", sdk_version=a.sdk_version))
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        p.exit(1, f"FMOD local linking failed: {exc}\n")
if __name__ == "__main__":
    main()
