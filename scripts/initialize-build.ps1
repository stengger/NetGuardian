$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
$dotnet = Resolve-DotNetSdk
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$userPackages = Join-Path $env:USERPROFILE '.nuget\packages'
$env:NUGET_PACKAGES = if (Test-Path -LiteralPath $userPackages) { $userPackages } else { Join-Path $root '.dotnet-home\.nuget\packages' }
