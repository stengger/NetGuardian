$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = "E:\Environment\dotnet-sdk-8.0\dotnet.exe"

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "dotnet SDK not found at $dotnet"
}

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

& $dotnet restore (Join-Path $root "NetGuardian.sln") --configfile (Join-Path $root "NuGet.Config")
& $dotnet build (Join-Path $root "NetGuardian.sln") --configfile (Join-Path $root "NuGet.Config") --no-restore

