$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "resolve-dotnet.ps1")
$dotnet = Resolve-DotNetSdk
$project = Join-Path $root "src\NetGuardian.App\NetGuardian.App.csproj"
$out = Join-Path $root "dist\NetGuardian"

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:APPDATA = Join-Path $root ".dotnet-home\AppData\Roaming"
$userPackages = Join-Path $env:USERPROFILE ".nuget\packages"
$env:NUGET_PACKAGES = if (Test-Path -LiteralPath $userPackages) { $userPackages } else { Join-Path $root ".dotnet-home\.nuget\packages" }
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
New-Item -ItemType Directory -Force -Path $env:APPDATA | Out-Null
New-Item -ItemType Directory -Force -Path $env:NUGET_PACKAGES | Out-Null

New-Item -ItemType Directory -Force -Path $out | Out-Null
& $dotnet restore $project --configfile (Join-Path $root "NuGet.Config") --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    $assetFiles = @(
        Join-Path $root "src\NetGuardian.Core\obj\project.assets.json"
        Join-Path $root "src\NetGuardian.App\obj\project.assets.json"
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

& $dotnet publish $project `
    --configuration Release `
    --self-contained false `
    --configfile (Join-Path $root "NuGet.Config") `
    --no-restore `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:NuGetAudit=false `
    --output $out
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Output "Published: $(Join-Path $out 'NetGuardian.exe')"
