param(
    [string]$WorkDirectory = (Join-Path $PSScriptRoot '.build'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'Windows'),
    [string]$PackageCache = '',
    [switch]$Offline
)
$ErrorActionPreference = 'Stop'
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $PackageCache) { $PackageCache = Join-Path $WorkDirectory 'packages' }
$names = @('DOTNET_CLI_HOME', 'APPDATA', 'LOCALAPPDATA', 'NUGET_PACKAGES', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:DOTNET_CLI_HOME = Join-Path $WorkDirectory 'dotnet-home'
    $env:APPDATA = Join-Path $WorkDirectory 'appdata'
    $env:LOCALAPPDATA = Join-Path $WorkDirectory 'localappdata'
    $env:NUGET_PACKAGES = [IO.Path]::GetFullPath($PackageCache)
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    Push-Location $PSScriptRoot
    try {
        $arguments = @('publish', 'src/SCS.Desktop/SCS.Desktop.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:TargetPlatformDisplayName=Windows',
            '--artifacts-path', (Join-Path $WorkDirectory 'publish-artifacts'), '-o', $OutputDirectory, '--nologo')
        if (-not $Offline) { $arguments += @('--source', 'https://api.nuget.org/v3/index.json') }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)." }
        Write-Host ('Executable: ' + (Join-Path $OutputDirectory 'Subsistence Custom Settings.exe'))
    }
    finally { Pop-Location }
}
finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') } }
