# v10 integration candidate

Not a release. Preserve installed v9 and use a separate signed identity for full-game testing first. The production host retains the existing unsigned normal-game identifier; the signing tool must create a separate identity if testing this candidate. Do not install over v9 until backup/export and full-game integration are verified.

Local source patch only: apply tools/Apply-V10Bridge.ps1 to a copy of the previously working v9 decompiled source. It checks unique anchors, keeps originals and creates an original bridge class. Host requires the matching patched assembly. No proprietary source is included.

At startup the host checks that SDL and the game's actual save path agree. It captures primary files if no snapshot exists; otherwise it restores only missing files after checking for conflicts. Any conflict halts startup with both copies preserved. A successful game save invokes the host after the existing validated backup-to-primary copy; the host verifies bytes and commits the snapshot. Explicit deletion updates preferences before the game removes the file; if preferences fail the deletion is refused. If disk deletion subsequently fails, the disk-only file is preserved and causes startup conflict. These are not atomic cross-store transactions. A failed backup commit after successful primary write can leave an older backup; game reports save failure and conflict is detected if the disk file survives. Cache loss in that interval can still lose newer progress. Off-device export is still required before replacing the working game.

Quit handling requires explicit menu selection, completed game cleanup, and unchanged error log. It displays a normal exit message; it does not call exit() or private Apple APIs. Full device testing pending.
