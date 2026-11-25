"""Extract the Mach-O linker and its DLLs from an official LLVM tar.xz archive."""
import argparse
import pathlib
import shutil
import tarfile

def extract(archive, output):
    output = output.resolve()
    if output.exists():
        raise ValueError("Choose a new extraction directory")
    # Never extract archive paths or symlinks directly. Map only selected
    # regular files (and internal hard links) to fixed relative destinations.
    with tarfile.open(archive, "r:xz") as source:
        selected = {}
        members = source.getmembers()
        for member in members:
            parts = pathlib.PurePosixPath(member.name).parts
            if len(parts) < 2 or any(p in ("..", "/", ".") or "\\" in p or ":" in p for p in parts):
                continue
            relative = parts[1:]
            name = relative[-1].lower()
            if len(relative) == 2 and relative[0] == "bin":
                wanted = name in ("lld.exe", "ld64.lld.exe", "llvm-nm.exe") or name.endswith(".dll")
            else:
                wanted = (len(relative) == 1 or relative[:-1] == ("share", "doc", "llvm")) and name.startswith("license")
            if wanted and (member.isfile() or member.islnk() or member.issym()):
                if relative in selected:
                    raise ValueError("Duplicate selected archive member")
                selected[relative] = member
        if not any(parts in selected for parts in (("bin", "lld.exe"), ("bin", "ld64.lld.exe"))):
            raise ValueError("LLVM archive does not contain a Windows Mach-O linker")
        output.mkdir(parents=True)
        for relative, member in sorted(selected.items(), key=lambda item: item[1].offset_data):
            dest = output.joinpath(*relative)
            # Resolve only within the same archive; write bytes, never filesystem links.
            stream = source.extractfile(member)
            if stream is None:
                raise ValueError("Could not read archive member: " + member.name)
            dest.parent.mkdir(parents=True, exist_ok=True)
            with stream, dest.open("xb") as out:
                shutil.copyfileobj(stream, out)
    return output

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    try:
        print("Extracted LLVM linker:", extract(args.archive, args.output))
    except (OSError, ValueError, KeyError, tarfile.TarError) as exc:
        parser.exit(1, f"LLVM extraction failed: {exc}\n")
