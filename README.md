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
This exercises SDL/GameController, Metal/FNA3D and FAudio, not Celeste.
Use the newest toolkit that includes host/ExternalProbe.dll. Its diagnostic
probe has seven colored bars (top to bottom): external entry invocation,
FNA math, external SDL call, bundled content read, cache write/read,
FMOD, and FAudio. Green = passed; red = failed; gray = not included;
gold = partial (FMOD exports only or FAudio ready before pressing A).
Background blue/green/gold still shows controller state. Photograph the
screen before and after pressing A and report whether you heard a tone.

Optional local FMOD initialization test, using your own matching framework
and exact FMOD_VERSION macro from its header (example only, replace version):

```powershell
.\CelestePackage.exe .\host\GameHost-base.ipa .\FNA-FMOD-probe.ipa --native .\native\CelesteNative.dylib --probe --probe-assembly .\host\ExternalProbe.dll --fmod "C:\LocalCeleste\FMODLocal.dylib" --fmod-version 0x00020200
```

Without --fmod-version the test only loads the library and checks exports.
It does not load game banks or prove audible FMOD playback. Keep the locally
assembled FMOD probe private. The app bundle identifier remains unchanged.

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
- The portable toolchain below, or compatible Git, .NET SDK and ILSpy on PATH.
- Either your own already-linked tvOS ARM64 FMOD dynamic library with install
  name `@rpath/FMODLocal.framework/FMODLocal`, or local compatible FMOD static
  archives plus LLVM (`ld64.lld` on PATH) and a lawfully available tvOS SDK
  linker root. Desktop/iOS FMOD binaries cannot be substituted.

## Portable Windows compiler dependencies

Run the included setup script once from the extracted toolkit folder:

```powershell
powershell -NoProfile -File .\\Setup-Toolchain.ps1
```

It downloads exact official versions into `toolchain/`: .NET SDK 8.0.419,
ILSpy 8.0.0.7246-preview3, MinGit 2.51.2 and LLVM 20.1.8. It checks upstream
archive hashes, runs version checks, and does not change the system PATH.
Use `-SkipLLVM` if you already have a compatible FMOD framework. LLVM is a
large download. The compiler and linker prefer this local directory over PATH.
These are downloaded locally rather than republished inside the public ZIP.
Setup does not provide FMOD, Apple SDK files, or game files.
LLVM extraction uses the packaged CelesteExtractLLVM.exe, not Windows tar.exe
or a system xz installation. The verified LLVM archive is retained under
toolchain/cache/downloads and reused if extraction needs to be retried.
Existing toolkit users can overlay the portable-toolchain-repair artifact
without deleting toolchain, inputs, work or other files.

The packaged executables use only their portable toolchain, not system PATH.
Keep the accompanying `_runtime-*` directories next to their executables.
The toolchain contains its own NuGet package/HTTP caches, .NET CLI home,
temporary directory and Git config; setup changes only its process environment.
Choose a new work directory and output IPA inside the toolkit for a fully
local build. To remove the toolkit, close its tools, copy your IPA elsewhere,
then delete the extracted toolkit folder. Windows itself may still retain
normal OS/security logs. No permanent installation is intended.

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

### tvOS content layout (v2 host)

Game packaging mirrors local Content at both `GameHost.app/Content/` (FNA TitleContainer) and `GameHost.app/GameAssemblies/Content/` (assembly-relative game content). Both copies are required for compatibility with the current host; the packager validates the exact-case startup XNB path and mirror CRCs. This adds package size but avoids symlinks and host rebuilds. No content is uploaded. Device execution and XNB decoding remain separate tests.
