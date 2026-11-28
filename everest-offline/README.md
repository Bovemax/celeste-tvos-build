# Genuine Everest offline patching

Pinned release: [stable-1.6458.0](https://github.com/EverestAPI/Everest/releases/tag/stable-1.6458.0), commit 4bbde91b8dbaaddef2ceec75ca0cd6d59b3b8d00, main.zip SHA256 d1f3ab569c3417a926d24604953ce73d93160c1f748bf5827124ee7c8df41720 (GitHub release asset digest). Same version as the external Apple static-AOT reference.

Prepare-Everest.ps1 creates a disposable desktop patch workspace from privately owned original FNA managed inputs, copies rather than hardlinks, verifies release archive, runs upstream MiniInstaller --fastmode maingame fna hookgen with pre-created orig, and reports output identities/hashes. No desktop game launch, no uploads, no save/content modification. FastMode differs from StandardMode: it intentionally skips apphost, native setup, and recursive NETCoreifier of vanilla dependencies. It may require follow-up compatibility fixes; do not interpret failure as tvOS incompatibility. Original inputs are used because the official IL rules expect vanilla patterns; previously verified tvOS bridge changes must later be reconciled rather than assumed retained.

## Known integration blockers

- Official patched game uses BOOT entry point and patched FNA, not the existing pinned vanilla FNA. Host reference, FNA native imports and API compatibility must be reconciled as one coherent assembly set.
- BOOT.Main handles desktop launcher/restart, native library paths and Environment.Exit. Need an explicit tvOS boot adapter, no desktop process launch.
- Everest.Patches.Celeste.Main performs writable log/config setup and calls Everest.Boot/Initialize during game lifecycle. Separate packaged assets from writable Mods/cache/saves paths.
- Existing v10 save and exit bridge is not present in a freshly patched original assembly. Preserve stable vanilla v10 and use isolated signing identity, no migration until new storage design is verified.
- MonoMod native detours not validated; actual physical interpreter passed simple DynamicMethod and managed module loading. Prefer interpreter-safe managed dispatch; do not assume native detours.
- No actual game files in CI. The Windows CI here checks script syntax only; local official installer execution is the next evidence gate.
