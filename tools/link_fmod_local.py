"""Experimental SDK-free link trial using imports observed in a public tvOS binary.

Never copies an Apple SDK, uploads inputs, or accepts unresolved symbols.
Unknown symbols fail the link; a successful link still requires device testing.
"""
import argparse
import json
from pathlib import Path
import struct
import subprocess
import tempfile
from link_native import verify_tvos_dylib
from toolchain import environment, find

def observed_imports(path):
    verify_tvos_dylib(path)
    data = path.read_bytes()
    _, _, _, _, count, _, _, _ = struct.unpack_from("<8I", data)
    off = 32
    libs = []
    symtab = None
    # All dylib load commands that occupy a two-level namespace ordinal.
    load_commands = {0xc, 0x80000018, 0x8000001f, 0x20, 0x80000023}
    for _ in range(count):
        cmd, size = struct.unpack_from("<II", data, off)
        if cmd in load_commands:
            nameoff = struct.unpack_from("<I", data, off + 8)[0]
            if nameoff < 24 or nameoff >= size:
                raise ValueError("Invalid dylib load name")
            libs.append(data[off+nameoff:off+size].split(b"\0", 1)[0].decode())
        elif cmd == 2:
            symtab = struct.unpack_from("<4I", data, off+8)
        off += size
    if symtab is None:
        raise ValueError("Reference binary has no symbol table")
    symoff, nsyms, stroff, strsize = symtab
    if symoff + nsyms*16 > len(data) or stroff + strsize > len(data):
        raise ValueError("Invalid symbol/string table")
    imports = {}
    strings = data[stroff:stroff+strsize]
    for i in range(nsyms):
        strx, kind, sect, desc, value = struct.unpack_from("<IBBHQ", data, symoff+16*i)
        if kind != 1 or sect != 0 or value != 0 or not 0 < strx < strsize:
            continue
        ordinal = desc >> 8
        if not 1 <= ordinal <= len(libs):
            continue
        lib = libs[ordinal-1]
        if not lib.startswith(("/usr/lib/", "/System/Library/Frameworks/")):
            continue
        symbol = strings[strx:].split(b"\0",1)[0].decode()
        if symbol:
            imports.setdefault(lib,set()).add(symbol)
    # Standard lazy binding entry, part of the publicly documented Mach-O ABI.
    imports.setdefault("/usr/lib/libSystem.B.dylib",set()).add("dyld_stub_binder")
    return imports

def write_stubs(imports, directory):
    result = []
    for i,(lib,symbols) in enumerate(sorted(imports.items())):
        # JSON string escaping is accepted by YAML (TBD v4).
        path = directory / ("import-%03d.tbd" % i)
        body = "--- !tapi-tbd\ntbd-version: 4\ntargets: [ arm64-tvos ]\n"
        body += "install-name: " + json.dumps(lib) + "\nexports:\n"
        body += "  - targets: [ arm64-tvos ]\n    symbols: [ " + ", ".join(map(json.dumps, sorted(symbols))) + " ]\n...\n"
        path.write_text(body, encoding="utf-8")
        result.append(path)
    return result

def link_trial(reference, core, studio, output):
    output = output.resolve()
    if output.exists() or output.suffix != ".dylib":
        raise ValueError("Choose a new .dylib output path")
    for p in (core,studio):
        with p.open("rb") as stream:
            # Older FMOD packages may use universal static archives. LLD chooses ARM64.
            if stream.read(4) not in (b"!<ar", b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf",
                                     b"\xbe\xba\xfe\xca", b"\xbf\xba\xfe\xca"):
                raise ValueError("Expected static archive: " + str(p))
    linker = find("ld64.lld")
    driver = [linker] if linker else []
    if not driver:
        linker = find("lld")
        if linker:
            driver = [linker, "-flavor", "darwin"]
    if not driver:
        raise ValueError("Portable LLVM not found; run Setup-Toolchain.ps1")
    imports = observed_imports(reference)
    print("Observed", sum(map(len,imports.values())), "system imports across", len(imports), "libraries", flush=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as tmp:
        directory = Path(tmp)
        target = directory / output.name
        stubs = write_stubs(imports, directory)
        args = driver + ["-arch","arm64","-platform_version","tvos","15.0","26.5",
                         "-dylib","-install_name","@rpath/FMODLocal.framework/FMODLocal",
                         "-undefined","error","--error-limit=0","-o",str(target),
                         "-force_load",str(core.resolve()),"-force_load",str(studio.resolve())]
        args += [str(p) for p in stubs]
        result = subprocess.run(args, env=environment(), stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        report = output.with_suffix(".link.txt")
        message = result.stdout.decode("utf-8", errors="replace")
        report.write_text(message, encoding="utf-8")
        if result.returncode:
            # Never publish a partial binary, or bind unknown symbols dynamically.
            raise ValueError("Strict link failed. See " + str(report) + " for missing symbols and errors.")
        verify_tvos_dylib(target, "@rpath/FMODLocal.framework/FMODLocal")
        with output.open("xb") as dst, target.open("rb") as src:
            import shutil
            shutil.copyfileobj(src,dst)
    return output

if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("reference","core","studio","output"):
        p.add_argument(name,type=Path)
    a = p.parse_args()
    try:
        print("Experimental linked framework:", link_trial(a.reference,a.core,a.studio,a.output))
        print("Not device validated. Keep the output private.")
    except (OSError, ValueError, struct.error, subprocess.CalledProcessError) as e:
        p.exit(1, "FMOD local link trial: " + str(e) + "\n")
