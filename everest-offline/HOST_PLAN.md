# Isolated genuine Everest host

Native candidate 30916c5f1808c21a5f6506590905bedfac3293b9 passed macOS CI run 46180840435, artifact 15278175114. Twelve new symbols verified, no hardware or full ABI pass.

Separate net10.0-tvos interpreter host has no compile-time FNA dependency and expects the actual local patched FNA 24.1 assembly. New identity org.example.celestelocalhost.everesttest (optional signer suffix), no vanilla save bridge or migration. Hash-checked explicit local payload manifest, no framework replacements, standard ALC and native resolution for local dependencies. Writable isolated cache for initial boot, not durable storage. No downloaded mods yet. CI builds empty unsigned host only.

DO NOT PACKAGE or launch before local boot adaptation: actual BOOT.Main uses desktop native setup/restart and Environment.Exit; UserIO static save path must be checked/redirected; PathEverest must be writable while PathGame remains the bundle. Need patch exact known local game, validate dependency closure excluding obsolete .NET Framework files, handle logging and core boot blockers. Runtime native detours, Lua and Discord support remain unresolved. The new host does not establish actual Everest boot.
