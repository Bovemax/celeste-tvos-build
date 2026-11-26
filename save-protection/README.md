# Save protection prototype

The game already writes and validates a backup under SDL's preference path before replacing the primary .celeste file. Both paths are cache-based on tvOS. Do not change the save format or redirect the existing directories.

This independent prototype implements a bounded, checksummed compressed snapshot of primary files. It has no device integration and does not read any user saves. The 256 KiB encoded cap is deliberately below the tvOS defaults warning threshold; the complete persistent defaults domain must also be checked before writing. The checksum is corruption detection, not authentication.

Integration gate: capture existing saves without replacing them, commit updated snapshots only after a verified successful save or explicit deletion, preserve previous snapshot if encoding/writing fails, distinguish a missing cache from an intentional empty save set, and never overwrite a conflicting primary file. Explicit recovery should be tested with synthetic data in the separate test app before enabling it in the normal game. Preferences are not an off-device backup and disappear on app deletion. No new production IPA yet.

For intentional Exit, the local patch should mark the explicit menu callback before Engine.Instance.Exit, preserve waiting for all worker threads and cleanup, and let the host distinguish a marked return with no fresh log from errors or ambiguous returns. No forced termination on an ambiguous return. Use the system UI for returning home instead of private APIs.
