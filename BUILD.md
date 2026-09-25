# Build, test and publish

## Requirements

- Windows x64, PowerShell, Git, and the .NET 9 SDK with Windows Desktop support.
- `global.json` requests SDK 9.0.318 with `latestPatch` roll-forward. The original 2.0.5 artifact used SDK 9.0.318 and runtime packs 9.0.20.
- The projects contain no third-party NuGet package references. Publishing a self-contained executable requires Microsoft's runtime packs, downloaded from NuGet unless already cached.

## Commands

Run from the repository root in PowerShell:

```powershell
./build.ps1
./publish-windows.ps1 -OutputDirectory ./Windows-2.0.5
Get-FileHash './Windows-2.0.5/Subsistence Custom Settings.exe' -Algorithm SHA256
```

`build.ps1` builds all projects in Release and runs the regression executable. Its scratch files and redirected build-tool preferences stay under `.build`. `publish-windows.ps1` emits a self-contained Windows x64 single-file WPF app. Output folders and build caches are excluded from source control.

For cached offline publication:

```powershell
./publish-windows.ps1 -Offline -PackageCache 'C:/path/to/cached/packages' -OutputDirectory ./Windows-2.0.5
```

Never run tests against a real game folder. Optional external INI fixtures are read-only inputs selected through `SCS_INI_FIXTURE` and `SCS_CLEAN_INI_FIXTURE`; they are not required and are not distributed because they can contain personal configuration.

## Release identity

Version: **2.0.5**, source tag: **v2.0.5**, catalog: **2.0.1**.

Published executable SHA-256:

```text
2888d4993492b51e9b6294838afe9fbf9ba9c52290e975681369a01bdaed009b
```

Compare this with the downloaded executable and `SHA256SUMS.txt` in the release. The source is buildable and uses deterministic compilation, but an independent byte-for-byte identical rebuild has not been established; SDK/runtime patch versions and build environment can affect the binary. Do not describe this release as reproducible in that stronger sense.

## License

This project is licensed under the MIT License.

See [LICENSE.md](LICENSE.md) for the full license terms.
