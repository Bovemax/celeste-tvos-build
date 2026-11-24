"""Insert an independently compiled test DLL into an unsigned host IPA.

This is an experimental packaging tool, not a complete Celeste builder.
Requires Python 3.9+; runs on Windows, Linux and macOS.
"""
import argparse
import pathlib
import plistlib
import zipfile

def inject(host, payload, output):
    if output.resolve() in (host.resolve(), payload.resolve()):
        raise ValueError("Output must be distinct from both input files")
    data = payload.read_bytes()
    if not data.startswith(b"MZ"):
        raise ValueError("Expected a managed PE DLL")
    with zipfile.ZipFile(host) as source:
        names = source.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Duplicate ZIP entries")
        roots = {n.split("/")[1] for n in names
                 if n.startswith("Payload/") and len(n.split("/")) >= 3
                 and n.split("/")[1].endswith(".app")}
        if len(roots) != 1:
            raise ValueError("Expected exactly one top-level app")
        root = "Payload/" + roots.pop() + "/"
        metadata = plistlib.loads(source.read(root + "Info.plist"))
        if 3 not in metadata.get("UIDeviceFamily", []):
            raise ValueError("Host is not marked as a tvOS application")
        exe = metadata["CFBundleExecutable"]
        if root + exe not in names:
            raise ValueError("Missing host executable")
        if any(n.startswith(root + "_CodeSignature/") or
               n == root + "embedded.mobileprovision" for n in names):
            raise ValueError("Use an unsigned host; sign the resulting IPA afterward")
        target = root + "Payload.dll"
        with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED) as dest:
            for entry in source.infolist():
                if entry.filename != target:
                    dest.writestr(entry, source.read(entry))
            dest.writestr(target, data)
    print("Created unsigned test IPA:", output)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("host", type=pathlib.Path)
    parser.add_argument("payload", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    inject(args.host, args.payload, args.output)
