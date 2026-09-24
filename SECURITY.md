# Security and file access

## Normal application behavior

- No application-level network client, telemetry, advertising, self-updater or account sign-in.
- No code injection, game memory editing, game executable patching, installed background service or startup task.
- No administrator elevation request. The current user must have permission to write the selected folders.
- Steam installation discovery reads local Steam paths, the Steam registry path and library metadata. It does not contact Steam services.
- Windows and the bundled .NET runtime can perform their own platform behavior; the statements above describe SCS application code.

## Files read and written

| Location | Scope |
| --- | --- |
| Selected game `Binaries` | `SCS_Profile1.txt` through `SCS_Profile4.txt`, `SCS_Vanilla.txt`, `SCS_Manifest.json`, Vanilla catalog metadata, installation journals, and SCS backup files |
| Selected game `UDKGame/Config` | `UDKInput.ini`, original non-overwriting `UDKInput.scs-backup.ini` and metadata, original SCS-binding snapshot, operation backups and journals |
| `%LOCALAPPDATA%/SubsistenceCustomSettings/TXT-v1` | `workspace.json`: profile state, friendly names, saved assignments and installation preference |
| Verified destination folders | Temporary permission probes and sibling temporary files for complete atomic replacement |
| OS runtime cache | The self-contained .NET single-file host may extract bundled native runtime files |

Profile removal is an explicit operation limited to eligible SCS-managed files, with backup. Hotkey removal identifies only the exact commands `exec SCS_Profile1.txt` through `exec SCS_Profile4.txt` and `exec SCS_Vanilla.txt`; unrelated bindings are preserved.

## Safeguards and limits

Writes use a complete temporary sibling followed by replacement. INI operations preserve unrelated content, encoding and line endings, and compare the current file against the reviewed version to detect concurrent changes. These checks do not provide a lock against a different process writing afterward.

The original INI backup is not overwritten. Per-operation backups and journals support diagnosis and recovery. A stale generated Vanilla profile is refreshed with a backup; existing custom profiles are not overwritten by installation of missing files.

Startup/verification can repair SCS bindings in the wrong table and refresh generated Vanilla metadata after validating the saved installation. If ColdPlayerInput contains only SCS bindings, those bindings are moved to Engine.PlayerInput so SCS does not introduce a child input array that shadows normal inherited controls.

Save all saves local preferences separately from installing hotkeys. Invalid or conflicting bindings cannot be installed. A failed or partial save is reported and does not silently mark remaining work as saved.

## Test and build behavior

Build tools may download Microsoft SDK/runtime packages from NuGet. This is separate from the application's runtime behavior. Tests create synthetic game folders in their scratch directory. The optional desktop `--smoke-test <folder>` mode creates fixture files, including a two-byte `MZ` placeholder named `Subsistence.exe`; do not point test mode at a real game installation.

An unused legacy process probe remains in the core source for compatibility; current hotkey editing is not gated on the game being closed. The application does not launch hidden game processes.

## Reviewing the implementation

Start with `src/SCS.Core/IO`, `Installation`, and `Hotkeys/IniHotkeys.cs`, then `src/SCS.Desktop/WorkspaceStore.cs` and `MainWindow.xaml.cs`. Search these files for all filesystem operations rather than relying only on this document.

The release checksum establishes artifact identity, not absence of malware. Source inspection and automated tests are not a third-party security audit. Unsigned executables may prompt Windows reputation warnings; do not disable security protections to run the app.
