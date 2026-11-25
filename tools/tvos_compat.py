"""Local-only source compatibility pass for the tvOS interpreter.
Never uploads or executes game source. Findings are candidates, not proof.
"""
import re
import subprocess
from toolchain import environment, find

# The observed tvOS failure is the GCSettings.LatencyMode setter.
# Only remove standalone assignments of literal enum values; never discard a
# computed right-hand side, and retain a statement for unbraced if/else bodies.
_SETTER = re.compile(
    r"(?m)^([ \t]*)(?:(?:global::)?System\.Runtime\.)?GCSettings[ \t]*\.[ \t]*LatencyMode[ \t]*=[ \t]*"
    r"(?:(?:global::)?System\.Runtime\.)?GCLatencyMode[ \t]*\.[ \t]*"
    r"(?:Batch|Interactive|LowLatency|SustainedLowLatency|NoGCRegion)[ \t]*;[ \t]*(?://[^\r\n]*)?$"
)
_ERROR_OPEN = re.compile(r"(?m)^([ \t]*)(?:System\.Diagnostics\.)?Process\.Start\([ \t]*Filename[ \t]*\);[ \t]*$")
_ENTRY_ASSEMBLY = re.compile(r"\bAssembly\s*\.\s*GetEntryAssembly\s*\(")
_REMAINING_SETTER = re.compile(r"\bGCSettings\s*\.\s*LatencyMode\s*=(?!=)")
_RISK = {
    "GC controls": r"\b(?:GCSettings\s*\.|GC\s*\.\s*(?:TryStartNoGCRegion|EndNoGCRegion|RegisterForFullGCNotification|WaitForFullGCApproach|WaitForFullGCComplete|CancelFullGCNotification))",
    "Dynamic code / legacy runtime": r"\b(?:Reflection\.Emit|DynamicMethod|AssemblyBuilder|AppDomain\s*\.\s*(?:CreateDomain|Unload)|Remoting|BinaryFormatter)\b",
    "Thread control": r"\.\s*(?:Abort|Suspend|Resume|Interrupt)\s*\(",
    "Process / desktop APIs": r"\b(?:Process\s*\.\s*Start|Environment\s*\.\s*Exit|Registry(?:Key)?|System\.Windows|System\.Drawing|Console\s*\.\s*(?:ReadKey|SetWindowSize|SetBufferSize))\b",
    "Reflection / trimming": r"\.\s*(?:GetTypes|GetMethods|GetFields|GetConstructor|CreateDelegate|Compile|GetCustomAttributes|GetType)\s*\(",
    "File / save paths": r"\b(?:Environment\s*\.\s*GetFolderPath|SpecialFolder|Directory\s*\.\s*GetCurrentDirectory|Assembly\s*\.\s*GetEntryAssembly|File\s*\.\s*(?:WriteAllText|WriteAllBytes|Create|OpenWrite)|FileStream)\b",
    "Native imports": r"\b(?:DllImport|LibraryImport|NativeLibrary\s*\.\s*Load)\b",
}
_RISK = {label: re.compile(pattern) for label, pattern in _RISK.items()}


def repair_source(source, log=print):
    """Patch the confirmed setter; enumerate other portability candidates locally."""
    engine = source / "Monocle" / "Engine.cs"
    if not engine.is_file():
        raise ValueError("Expected decompiled Monocle/Engine.cs")
    changes = []
    errorlog = source / "Monocle" / "ErrorLog.cs"
    entry_refs = []
    findings = []
    unexpected = []
    for path in sorted(source.rglob("*.cs")):
        relative = path.relative_to(source)
        if any(part in ("bin", "obj", ".git") for part in relative.parts):
            continue
        # Preserve UTF-8 BOM and line endings in existing sources.
        original = path.read_bytes()
        bom = original.startswith(b"\xef\xbb\xbf")
        text = original.decode("utf-8-sig")
        newline = "\r\n" if "\r\n" in text else "\n"
        normalized = text.replace("\r\n", "\n")
        # Only the observed Monocle engine source is modified. Avoid changing
        # unrelated types with a similar property or unfamiliar syntax.
        updated, count = _SETTER.subn(
            lambda m: m.group(1) + "; // tvOS: keep the runtime's default GC latency mode.",
            normalized
        ) if path == engine else (normalized, 0)
        kind = "GC latency"
        if path == errorlog:
            updated, count = _ERROR_OPEN.subn(lambda m: m.group(1) + 'System.Console.Error.WriteLine("Error log: " + Filename);', updated)
            kind = "desktop error-log launch"
        if count:
            changes.append((path, (b"\xef\xbb\xbf" if bom else b"") +
                            updated.replace("\n", newline).encode("utf-8"), count, kind))
        for match in _REMAINING_SETTER.finditer(updated):
            unexpected.append(f"{relative}:{updated.count(chr(10), 0, match.start()) + 1}")
        for line_no, line in enumerate(updated.splitlines(), 1):
            if line.lstrip().startswith(("//", "*")):
                continue
            if _ENTRY_ASSEMBLY.search(line):
                entry_refs.append(f"{relative}:{line_no}")
            for label, pattern in _RISK.items():
                if pattern.search(line):
                    findings.append(f"  {relative}:{line_no}: {label}")
    if unexpected:
        raise ValueError("Unreviewed GC latency assignment(s): " + ", ".join(unexpected))
    for path, data, count, kind in changes:
        path.write_bytes(data)
        log(f"Repaired {count} {kind} statement(s): {path.relative_to(source)}")
    if not changes:
        log("No recognized compatibility fixes remain (already patched or absent).")
    for reference in entry_refs:
        log("REVIEW loader entry-assembly reference: " + reference)
    log(f"Local source portability audit: {len(findings)} candidate locations; not runtime validation.")
    for finding in findings:
        log(finding)
    return sum(count for _, _, count, _ in changes)


# Deliberately narrow ILSpy C# declaration matcher. Unrecognized attributes
# are counted separately rather than claimed to have passed.
_IMPORT = re.compile(r'\[\s*(?:System\.Runtime\.InteropServices\.)?DllImport(?:Attribute)?\s*\(\s*"([^"]+)"([^\]]*)\]\s*(?:\[[^\]]+\]\s*)*(?:(?:public|private|internal|protected|static|unsafe|extern|new)\s+)+[\w.<>,\[\]*?]+\s+(\w+)\s*\(', re.S)
_IMPORT_MARKER = re.compile(r'\[\s*(?:System\.Runtime\.InteropServices\.)?DllImport(?:Attribute)?\s*\(')
_ENTRY_POINT = re.compile(r'\bEntryPoint\s*=\s*"([^"]+)"')


def native_declarations(source):
    declarations = set()
    unmatched = 0
    for path in sorted(source.rglob("*.cs")):
        if any(part in ("bin", "obj", ".git") for part in path.relative_to(source).parts):
            continue
        text = path.read_text(encoding="utf-8-sig")
        matches = list(_IMPORT.finditer(text))
        unmatched += max(0, len(_IMPORT_MARKER.findall(text)) - len(matches))
        for m in matches:
            entry = _ENTRY_POINT.search(m.group(2))
            declarations.add((m.group(1), entry.group(1) if entry else m.group(3)))
    return declarations, unmatched


def exported_symbols(path):
    nm = find("llvm-nm")
    if not nm:
        raise ValueError("Portable llvm-nm not found; no native export audit performed")
    result = subprocess.run([nm, "--defined-only", "--extern-only", "--just-symbol-name", str(path.resolve())],
                            env=environment(), check=True, capture_output=True, text=True)
    # Mach-O C ABI symbols have an underscore prefix. Remove exactly one.
    return {line.strip()[1:] for line in result.stdout.splitlines()
            if line.strip().startswith("_")}


def audit_native(source, native, fmod, log=print):
    declarations, unmatched = native_declarations(source)
    if not declarations:
        raise ValueError("No native declarations recognized; check the source directory")
    native_exports, fmod_exports = exported_symbols(native), exported_symbols(fmod)
    missing = []
    unknown = []
    found = 0
    for library, symbol in sorted(declarations):
        key = library.lower()
        for suffix in (".dll", ".dylib", ".so"):
            if key.endswith(suffix):
                key = key[:-len(suffix)]
                break
        if key in ("fmod", "fmodstudio", "libfmod", "libfmodstudio"):
            exports = fmod_exports
        elif key in ("sdl2", "lib sdl2", "libsdl2", "fna3d", "libfna3d", "faudio", "libfaudio", "theorafile", "libtheorafile"):
            exports = native_exports
        else:
            unknown.append((library, symbol))
            continue
        if symbol in exports:
            found += 1
        else:
            missing.append((library, symbol))
    for library, symbol in missing:
        log(f"MISSING from defined external symbols: {library}!{symbol}")
    for library, symbol in unknown:
        log(f"UNMAPPED library: {library}!{symbol} (check platform guard)")
    log(f"Native source declaration audit: {found} found, {len(missing)} missing, {len(unknown)} unmapped, {unmatched} unrecognized attributes")
    log("Declaration-only check: not call reachability, export-trie verification, signature/ABI validation, or device execution.")
    return found, len(missing), len(unknown), unmatched
