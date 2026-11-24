"""Link locally supplied ARM64 tvOS archives using LLVM LLD.

Experimental: requires compatible archives and user-supplied SDK linker inputs
when system frameworks are needed. Does not upload files, sign or install apps.
"""
import argparse
import pathlib
import re
import shutil
import struct
import subprocess
import tempfile

def verify_tvos_dylib(path, expected_install_name=None):
    with path.open("rb") as stream:
        header = stream.read(32)
        if len(header) != 32:
            raise ValueError("Truncated Mach-O header")
        magic, cpu, subtype, kind, count, size, flags, reserved = struct.unpack("<8I", header)
        if (magic, cpu, kind) != (0xfeedfacf, 0x100000c, 6):
            raise ValueError("Expected ARM64 Mach-O dynamic library")
        if size > path.stat().st_size - 32 or count > size // 8:
            raise ValueError("Invalid Mach-O load command bounds")
        commands = stream.read(size)
        offset = 0
        platforms = []
        install_names = []
        for _ in range(count):
            if offset + 8 > len(commands):
                raise ValueError("Invalid load command")
            cmd, length = struct.unpack_from("<2I", commands, offset)
            if length < 8 or offset + length > len(commands):
                raise ValueError("Invalid load command size")
            if cmd == 0x32:
                if length < 24:
                    raise ValueError("Invalid build version command")
                platforms.append(struct.unpack_from("<I", commands, offset + 8)[0])
            if cmd == 0xd:  # LC_ID_DYLIB
                if length < 24:
                    raise ValueError("Invalid dylib identity command")
                name_offset = struct.unpack_from("<I", commands, offset + 8)[0]
                if name_offset < 24 or name_offset >= length:
                    raise ValueError("Invalid dylib identity offset")
                install_names.append(commands[offset + name_offset:offset + length].split(b"\0", 1)[0].decode("utf-8"))
            offset += length
        if platforms != [3]:
            raise ValueError("Expected tvOS device platform, not iOS or simulator")
        if expected_install_name and install_names != [expected_install_name]:
            raise ValueError("Unexpected dylib identity: " + repr(install_names))
        if not flags & 1:
            raise ValueError("Mach-O does not declare resolved symbols")

def link(archives, output, sdk=None, frameworks=(), libraries=(), objects=(), framework_name=None, sdk_version="26.0"):
    output = output.resolve()
    if output.exists() or output.suffix != ".dylib":
        raise ValueError("Choose a new .dylib output")
    if framework_name is not None and not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", framework_name):
        raise ValueError("Invalid framework name")
    if not re.fullmatch(r"\d+(\.\d+){1,2}", sdk_version):
        raise ValueError("Invalid SDK version")
    install_name = "@rpath/" + (framework_name + ".framework/" + framework_name if framework_name else output.name)
    linker = shutil.which("ld64.lld")
    if not linker:
        raise ValueError("Install LLVM with ld64.lld available on PATH")
    if not archives:
        raise ValueError("At least one static archive is required")
    for item in archives:
        with item.open("rb") as stream:
            if stream.read(8) != b"!<arch>\n":
                raise ValueError("Not a standard static archive: " + str(item))
    if (frameworks or libraries) and sdk is None:
        raise ValueError("System library linking requires local SDK linker inputs")
    if sdk is not None and not sdk.is_dir():
        raise ValueError("SDK directory does not exist")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as tmp:
        target = pathlib.Path(tmp) / output.name
        args = [linker, "-arch", "arm64", "-platform_version", "tvos", "15.0", sdk_version,
                "-dylib", "-install_name", install_name,
                "-undefined", "error", "-o", str(target)]
        if sdk:
            args += ["-syslibroot", str(sdk.resolve())]
        args += [str(p.resolve()) for p in objects]
        for item in archives:
            args += ["-force_load", str(item.resolve())]
        for name in frameworks:
            args += ["-framework", name]
        for name in libraries:
            args += ["-l" + name]
        subprocess.run(args, check=True)
        verify_tvos_dylib(target, install_name)
        with output.open("xb") as dest, target.open("rb") as src:
            shutil.copyfileobj(src, dest)
    return output

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("archives", nargs="+", type=pathlib.Path)
    parser.add_argument("--object", action="append", type=pathlib.Path, default=[])
    parser.add_argument("--sdk", type=pathlib.Path)
    parser.add_argument("--framework", action="append", default=[])
    parser.add_argument("--library", action="append", default=[])
    parser.add_argument("--framework-name")
    parser.add_argument("--sdk-version", default="26.0")
    args = parser.parse_args()
    try:
        print("Created experimental native library:", link(args.archives, args.output, args.sdk,
              args.framework, args.library, args.object, args.framework_name, args.sdk_version))
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        parser.exit(1, f"Link failed: {exc}\n")
