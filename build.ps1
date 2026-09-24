param(
    [string]$WorkDirectory = (Join-Path $PSScriptRoot '.build'),
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
[IO.Directory]::CreateDirectory($WorkDirectory) | Out-Null
$names = @('DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'APPDATA', 'LOCALAPPDATA', 'NUGET_PACKAGES')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    # Keep build caches local; do not depend on the user's NuGet configuration.
    $env:DOTNET_CLI_HOME = Join-Path $WorkDirectory 'dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:APPDATA = Join-Path $WorkDirectory 'appdata'
    $env:LOCALAPPDATA = Join-Path $WorkDirectory 'localappdata'
    $env:NUGET_PACKAGES = Join-Path $WorkDirectory 'packages'
    $artifacts = Join-Path $WorkDirectory 'artifacts'
    $config = Join-Path $PSScriptRoot 'NuGet.Config'
    Push-Location $PSScriptRoot
    try {
        & dotnet build 'SubsistenceCustomSettings.sln' -c Release --artifacts-path $artifacts "-p:RestoreConfigFile=$config" -p:TargetPlatformDisplayName=Windows --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
        if (-not $SkipTests) {
            & dotnet (Join-Path $artifacts 'bin/SCS.Tests/release/SCS.Tests.dll') --scratch (Join-Path $WorkDirectory 'tests')
            if ($LASTEXITCODE -ne 0) { throw "Tests failed ($LASTEXITCODE)." }
        }
        Write-Host ('CLI: ' + (Join-Path $artifacts 'bin/SCS.Cli/release/SCS.Cli.dll'))
    }
    finally { Pop-Location }
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
