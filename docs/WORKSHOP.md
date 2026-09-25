# Workshop description draft

This mod includes a companion Windows application used to configure profiles and hotkeys for Subsistence.

It writes SCS profile files to the game's Binaries folder and updates UDKInput.ini to install your selected hotkeys. It also stores local preferences, backups and installation metadata. It does not connect to the internet, collect telemetry, inject code into the game or install a background service.

Source code is publicly available for inspection:
https://github.com/Airam-7/Subsistence-Custom-Settings

Security and file-access details:
https://github.com/Airam-7/Subsistence-Custom-Settings/blob/main/SECURITY.md

## How to open the UI

Steam installs Workshop files under:
`.../Steam/steamapps/workshop/content/418030/<WorkshopID>/`

Open the mod's folder and run `Subsistence Custom Settings.exe`.

SCS 2.0.6. This version installs hotkeys in both input sections, using dotted append entries in ColdPlayerInput to preserve inherited controls. Source and validation details are recorded in the repository's BUILD.md.

Publisher note: publish the 2.0.6 executable separately, record its actual source commit/tag and SHA-256, and replace `<WorkshopID>` with the actual item ID before posting this draft. This text has not been published to Workshop.
