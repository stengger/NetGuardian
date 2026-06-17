$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "resolve-dotnet.ps1")
$dotnet = Resolve-DotNetSdk

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:APPDATA = Join-Path $root ".dotnet-home\AppData\Roaming"
$userPackages = Join-Path $env:USERPROFILE ".nuget\packages"
$env:NUGET_PACKAGES = if (Test-Path -LiteralPath $userPackages) { $userPackages } else { Join-Path $root ".dotnet-home\.nuget\packages" }
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
New-Item -ItemType Directory -Force -Path $env:APPDATA | Out-Null
New-Item -ItemType Directory -Force -Path $env:NUGET_PACKAGES | Out-Null

& $dotnet restore (Join-Path $root "NetGuardian.sln") --configfile (Join-Path $root "NuGet.Config") --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    $assetFiles = @(
        Join-Path $root "src\NetGuardian.Core\obj\project.assets.json"
        Join-Path $root "src\NetGuardian.App\obj\project.assets.json"
        Join-Path $root "tests\NetGuardian.Tests\obj\project.assets.json"
    )
    $hasAssets = $true
    foreach ($assetFile in $assetFiles) {
        if (-not (Test-Path -LiteralPath $assetFile)) {
            $hasAssets = $false
        }
    }

    if (-not $hasAssets) {
        exit $LASTEXITCODE
    }

    Write-Warning "Restore failed; continuing with existing project.assets.json files."
}
& $dotnet build (Join-Path $root "NetGuardian.sln") --configfile (Join-Path $root "NuGet.Config") --no-restore -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
