# Celeste tvOS local build toolkit

Experimental, Windows-only assembly of an unsigned Apple TV IPA from public
tvOS host/native components and your own game files. No proprietary game code,
assets, FMOD libraries, signing credentials or SDK files are included or uploaded.
Compilation and ZIP validation do not establish that the game works on a device.

## Downloads

Open [Windows local Celeste toolkit](https://github.com/Bovemax/celeste-tvos-build/actions/workflows/windows-local-toolkit.yml)
and download the `celeste-windows-local-toolkit` artifact from a successful run.
Extract the ZIP into a new folder on Windows. It contains:

- `CelestePrepareInputs.exe`: generate a private input ZIP and SHA256 manifest from local folders.
- `CelesteLocalBuild.exe`: local validation, patching, optional FMOD linking and IPA packaging.
- `CelestePackage.exe`: package already-patched assemblies without decompilation.
- `host/GameHost-base.ipa` and `native/CelesteNative.dylib`: public prebuilt components.
- `FNA-probe.ipa`: original synthetic device test (no Celeste or FMOD).
- Source revisions, license notices and executable checksums.

Artifacts expire; use the recorded public component run IDs when rebuilding the
toolkit rather than rerunning macOS builds unnecessarily.

## First test the public probe

Sign the probe with a tool that supports tvOS and recursively signs nested
frameworks, then install it on your Apple TV. A blue screen indicates FNA
graphics started. Connect a controller: green should indicate connection;
holding A should make the screen gold and pressing A should play a short tone.
This exercises SDL/GameController, Metal/FNA3D and FAudio, not FMOD or Celeste.
Report the screen, controller, tone, any signing error and any crash log.

## Prepare local inputs

Keep these files on your own computer. The toolkit does not fetch your game or
patches. The patch pipeline expects the same managed input version that the
local patches were authored against.

- Your local `tvos-build-inputs.zip` containing `manifest.json`, `managed/Celeste.exe`,
  `managed/FNA.dll`, `managed/System.Core.dll`, `managed/System.Xml.dll`,
  `managed/mscorlib.dll`, `managed/Celeste.Content.dll`, and compatible tvOS
  ARM64 `native/libfmod.a` and `native/libfmodstudio.a`.
  Include `managed/Steamworks.NET.dll` if needed by your existing local patch set.
  The manifest maps member names under `inputs` to objects containing `sha256`.
- The original game `Content` folder.
- Your matching local patches directory containing `crash-fixes.patch` and,
  if applicable, `remove-steam.patch` and `remove-steam2.patch`.
- Git, a .NET SDK compatible with the patched project, and
  `ilspycmd` 8.0.0.7246-preview3 available on PATH.
- Either your own already-linked tvOS ARM64 FMOD dynamic library with install
  name `@rpath/FMODLocal.framework/FMODLocal`, or local compatible FMOD static
  archives plus LLVM (`ld64.lld` on PATH) and a lawfully available tvOS SDK
  linker root. Desktop/iOS FMOD binaries cannot be substituted.

Python is not required when using the packaged Windows executables; it is
required to run the source scripts directly.

If you do not already have the input ZIP, the new input-preparation tool creates it from local folders (no uploads):

```powershell
.\CelestePrepareInputs.exe "C:\LocalCeleste\managed" "C:\LocalCeleste\native" "C:\LocalCeleste\Content" "C:\LocalCeleste\tvos-build-inputs.zip"
```

The managed folder must contain the listed managed files and the native folder
must contain the two compatible tvOS FMOD archives. Content is validated but
not duplicated inside the ZIP. This checks hashes and basic file formats, not
runtime compatibility.

## Build on Windows

From PowerShell in the extracted toolkit directory, replace the example paths
with yours. Choose new working and output paths.

If you have a compatible local FMOD framework binary:

```powershell
.\CelesteLocalBuild.exe "C:\LocalCeleste\tvos-build-inputs.zip" "C:\LocalCeleste\Content" "C:\LocalCeleste\patches" ".\host\GameHost-base.ipa" ".\native\CelesteNative.dylib" ".\work" ".\Celeste-unsigned.ipa" --fmod "C:\LocalCeleste\FMODLocal.dylib"
```

Or link local tvOS FMOD archives with your own SDK inputs (replace the example SDK version):

```powershell
.\CelesteLocalBuild.exe "C:\LocalCeleste\tvos-build-inputs.zip" "C:\LocalCeleste\Content" "C:\LocalCeleste\patches" ".\host\GameHost-base.ipa" ".\native\CelesteNative.dylib" ".\work" ".\Celeste-unsigned.ipa" --sdk "C:\LocalSDK\AppleTVOS.sdk" --sdk-version 26.5
```

If you already have the patched `Celeste.dll` and `Celeste.Content.dll`:

```powershell
.\CelestePackage.exe ".\host\GameHost-base.ipa" ".\Celeste-unsigned.ipa" --native ".\native\CelesteNative.dylib" --managed "C:\LocalCeleste\patched" --content "C:\LocalCeleste\Content" --fmod "C:\LocalCeleste\FMODLocal.dylib"
```

The resulting IPA is unsigned. Sign locally, including both nested frameworks,
using your own tvOS provisioning/signing credentials. Do not share the result.
The toolkit does not sign, upload or install anything.

## Status and known limitations

The public interpreter host and native framework compiled, and a Windows job
assembled and checked the synthetic probe IPA. The actual game patching and
FMOD linking path have not been verified end to end with private inputs.
Signing, runtime assembly loading, game startup, graphics, controller, FMOD
audio, content and durable game saves still require real device evidence.
The current host uses writable caches for transient files, which tvOS may purge.
A successful package does not promise successful installation or play.
