# Uninstallation and recovery

## Remove SCS from a game installation

1. If you want to undo active gameplay multipliers, apply the installed **Vanilla** profile in-game before removing its hotkey or file. Deleting SCS files does not execute Vanilla commands or undo values already loaded by the game.
2. In **Installation**, verify the selected game paths. In **Hotkeys**, choose **Remove SCS hotkeys** and confirm. This removes only the five exact SCS profile commands, preserves unrelated bindings and backups, and clears the app's saved assignments after successful removal.
3. In **Installation**, select the profile files to remove, choose **Remove selected profile files**, and confirm. Removal requires SCS hotkeys to be removed first. Only files recorded as managed in `SCS_Manifest.json` whose current hash matches the recorded hash are eligible. Each removed file is backed up.
4. Close the app. The companion EXE can then be removed through the distribution source you used. Keep backups until you are satisfied with your game configuration.

If removal refuses an untracked or externally changed file, preserve it and inspect the difference; do not save over it merely to make removal succeed. The protection is intended to prevent accidental loss of an existing profile.

Local app preferences are separate from the game files. They remain in `%LOCALAPPDATA%/SubsistenceCustomSettings/TXT-v1/workspace.json`. To reset only the app's local preferences, close SCS and move this file to a safe backup location. This does not uninstall game bindings or remove game profiles.

## Find backups

| Folder | Files |
| --- | --- |
| `UDKGame/Config` | `UDKInput.scs-backup.ini`: original backup, never overwritten; accompanying metadata and original SCS-binding snapshot |
| `UDKGame/Config` | `UDKInput.scs-operation-<id>.ini` and `.json`: per-operation INI backups and journals |
| `Binaries` | Profile backups ending in `.scs-save-<id>.bak`, `.scs-upgrade-<id>.bak` or `.scs-removed-<id>.bak` |
| `Binaries` | `SCS_Manifest.json`, Vanilla catalog metadata and `SCS_Install-<id>.json` journals |

## Recover from a problem

- **An ordinary save failed:** read the error, fix invalid values or destination permissions, and retry. Save all can retain successfully written profiles while leaving the remaining work pending. Do not discard pending work unless that is your intention.
- **Keyboard or mouse bindings are missing after an older SCS installation:** verify the game paths. SCS 2.0.6 migrates old SCS entries into matching pairs: `Bindings=` in `Engine.PlayerInput` and `.Bindings=` in `ColdGame.ColdPlayerInput`, preserving unrelated controls and flags. Valid dotted pairs are not treated as shadowing and remain unchanged. If old copies disagree on a key or assignments are incomplete, choose five available keys in Hotkeys and install/update them explicitly. If parsing or concurrent-change checks block repair, preserve the current INI and backups for inspection.
- **A profile needs manual recovery:** close SCS, copy the current file somewhere safe, and inspect the appropriate profile backup before restoring it. A manually restored file can differ from the installation manifest, so managed removal may refuse it until its state is reconciled.
- **The INI needs manual recovery:** close Subsistence and SCS for the recovery operation, first copy the current INI to a safe location, and compare it with the chosen backup. The original backup may predate later legitimate player bindings. Prefer restoring only the damaged portion; replacing the entire INI can discard those later changes.

Closing the game for manual recovery is a precaution for a full configuration restore, not a restriction on normal SCS hotkey edits, which remain allowed while the game runs.

Do not delete backup metadata to bypass a failed backup-integrity check. Report the error with your SCS version and sanitized details. Avoid posting a full private INI or personal filesystem paths in a public issue.
