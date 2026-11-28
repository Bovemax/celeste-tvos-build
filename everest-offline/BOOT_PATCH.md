# Boot patch v1

Local-only Cecil transformation locked to the three hashes in LOCAL_PATCH_RESULT.md. Preserve genuine BOOT entry and lifecycle; host handles native loading, desktop restart fails explicitly, BOOT returns to diagnostic host, critical failures propagate for diagnostics, PathEverest and primary/backup save roots are separated from read-only bundle. Never run on stable v10 or original game. No game assembly is in this repo.

The installer-generated UserIO cctor anchors are not yet verified by this script on the user's local assembly; failure must stop packaging. A desktop compile pass is not a test of actual IL transformation. Lua/Discord, runtime detours, FNA API/ABI and asset overlay, core startup, framework retention and mod saves remain unresolved. No user mods should be installed for first boot. Do not claim this is a working Everest build.
