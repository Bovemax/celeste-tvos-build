"""Check that the linked reference contains every requested ABI symbol."""
import argparse
from pathlib import Path
import re
from link_fmod_local import observed_imports

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("assembly", type=Path)
    p.add_argument("reference", type=Path)
    a = p.parse_args()
    requested = set(re.findall(r"^\s*\.quad\s+(\S+)\s*$", a.assembly.read_text(), re.M))
    if not requested:
        p.exit(1, "No requested imports\n")
    imports = observed_imports(a.reference)
    missing = requested - set().union(*imports.values())
    if missing:
        p.exit(1, "Missing verified system imports: " + ", ".join(sorted(missing)) + "\n")
    print("Verified", len(requested), "public ABI imports against Apple TV SDK")
    for lib, symbols in sorted(imports.items()):
        print(lib + ": " + str(len(symbols & requested)))
if __name__ == "__main__":
    main()
