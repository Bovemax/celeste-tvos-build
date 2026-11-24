"""Stage validated user-owned inputs locally for the experimental build.

Never uploads files. Output must be a new directory.
"""
import argparse
import pathlib
import shutil
import tempfile
import zipfile
from check_game_inputs import check, MANAGED, NATIVE

def stage(archive, content, output):
    archive, content, output = (p.resolve() for p in (archive, content, output))
    if output.exists():
        raise ValueError("Output must be a new directory")
    if output.is_relative_to(content) or content.is_relative_to(output):
        raise ValueError("Output must be separate from original Content")
    errors = check(archive, content)
    if errors:
        raise ValueError("\n".join(errors))
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as temp:
        work = pathlib.Path(temp) / "staged"
        (work / "managed").mkdir(parents=True)
        (work / "native").mkdir()
        with zipfile.ZipFile(archive) as z:
            for folder, names in (("managed", MANAGED + ("Steamworks.NET.dll",)), ("native", NATIVE)):
                for name in names:
                    member = folder + "/" + name
                    if member not in z.namelist():
                        continue
                    with z.open(member) as src, (work / folder / name).open("xb") as dst:
                        shutil.copyfileobj(src, dst)
        shutil.copytree(content, work / "Content")
        work.rename(output)
    return output

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("archive", "content", "output"):
        parser.add_argument(name, type=pathlib.Path)
    args = parser.parse_args()
    try:
        result = stage(args.archive, args.content, args.output)
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        parser.exit(1, f"Staging failed: {exc}\n")
    print("Staged local files in", result)
    print("No upload performed. Keep this directory private.")
