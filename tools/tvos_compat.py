"""Local-only source compatibility pass for the tvOS interpreter.
Never uploads or executes game source. Findings are candidates, not proof.
"""
import re

# The observed tvOS failure is the GCSettings.LatencyMode setter.
# Only remove standalone assignments of literal enum values; never discard a
# computed right-hand side, and retain a statement for unbraced if/else bodies.
_SETTER = re.compile(
    r"(?m)^([ \t]*)(?:(?:global::)?System\.Runtime\.)?GCSettings[ \t]*\.[ \t]*LatencyMode[ \t]*=[ \t]*"
    r"(?:(?:global::)?System\.Runtime\.)?GCLatencyMode[ \t]*\.[ \t]*"
    r"(?:Batch|Interactive|LowLatency|SustainedLowLatency|NoGCRegion)[ \t]*;[ \t]*(?://[^\r\n]*)?$"
)
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
        if count:
            changes.append((path, (b"\xef\xbb\xbf" if bom else b"") +
                            updated.replace("\n", newline).encode("utf-8"), count))
        for match in _REMAINING_SETTER.finditer(updated):
            unexpected.append(f"{relative}:{updated.count(chr(10), 0, match.start()) + 1}")
        for line_no, line in enumerate(updated.splitlines(), 1):
            if line.lstrip().startswith(("//", "*")):
                continue
            for label, pattern in _RISK.items():
                if pattern.search(line):
                    findings.append(f"  {relative}:{line_no}: {label}")
    if unexpected:
        raise ValueError("Unreviewed GC latency assignment(s): " + ", ".join(unexpected))
    for path, data, count in changes:
        path.write_bytes(data)
        log(f"Removed {count} desktop GC latency assignment(s): {path.relative_to(source)}")
    if not changes:
        log("No recognized GC latency assignments remain (already patched or absent).")
    log(f"Local source portability audit: {len(findings)} candidate locations; not runtime validation.")
    for finding in findings:
        log(finding)
    return sum(count for _, _, count in changes)
