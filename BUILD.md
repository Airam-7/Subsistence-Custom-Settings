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

## CLI examples

After `./build.ps1`, run these from the repository root:

```powershell
$cli = './.build/artifacts/bin/SCS.Cli/release/SCS.Cli.dll'
dotnet $cli demo
dotnet $cli vanilla
dotnet $cli sample
dotnet $cli compile ./examples/profile1.json
dotnet $cli migrate ./examples/profile1.json
```

`demo` compiles a complete profile with weapon upgrade speed x4 and campfire fuel duration x2. `vanilla` prints baseline commands, and `sample` prints a default JSON profile. `compile` prints commands for a JSON profile; `migrate` prints an updated JSON profile and reports added/removed settings on standard error. Neither modifies the input JSON or game files. Errors return a nonzero exit code. The CLI has no installation or hotkey-writing command.

## WPF smoke tests

These are separate from the 95 portable regression cases run by `build.ps1`. Run them on Windows in an interactive desktop session after publishing the EXE. They exercise WPF controls, save/close behavior and a synthetic installation, and produce screenshots for inspection. They do not launch Subsistence or validate gameplay.

Use a new scratch directory on every run. The test intentionally writes profiles, preferences, an INI and a placeholder game executable under that directory. Never pass a real game folder, an existing workspace, or a previous smoke-test directory.

```powershell
$exe = (Resolve-Path './Windows-2.0.5/Subsistence Custom Settings.exe').Path
$scratch = Join-Path ([IO.Path]::GetFullPath('.build')) ('ui-smoke-' + [Guid]::NewGuid().ToString('N'))
$process = Start-Process -FilePath $exe -ArgumentList ('--smoke-test "{0}"' -f $scratch) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "WPF smoke test failed. Inspect $scratch/error.txt"
}
$report = Get-Content (Join-Path $scratch 'ui-test-results.json') -Raw | ConvertFrom-Json
if (-not $report.passed) { throw 'WPF smoke report did not pass.' }
$report
```

Inspect the PNG files in the same scratch directory, including `11-save-all-clean.png` and `12-close-dialog.png`. A successful report checks interaction behavior; visual inspection is still useful for clipping and layout. Failure details are written to `error.txt` when the test harness can report them. Screenshots of the installation page contain the scratch path; sanitize personal paths before sharing.

## Release identity

The existing `2.0.5` tag is retained as the original source snapshot. Its README and build guide predate documentation corrections and mention `v2.0.5` and a separate checksum attachment. The actual tag is `2.0.5`; use this guide and the updated release notes for corrected instructions. These documentation updates do not replace the release executable.

Version: **2.0.5**, release tag: **2.0.5**, catalog: **2.0.1**.

Published executable SHA-256:

```text
2888d4993492b51e9b6294838afe9fbf9ba9c52290e975681369a01bdaed009b
```

Compare this value with the downloaded executable using `Get-FileHash`. The GitHub release asset for 2.0.5 has the same SHA-256 digest. A separate `SHA256SUMS.txt` asset is optional and is not required for verification. The source is buildable and uses deterministic compilation, but an independent byte-for-byte identical rebuild has not been established; SDK/runtime patch versions and build environment can affect the binary. Do not describe this release as reproducible in that stronger sense.

## License

This project is licensed under the MIT License.

See [LICENSE.md](LICENSE.md) for the full license terms.
