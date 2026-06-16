$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = "E:\Environment\dotnet-sdk-8.0\dotnet.exe"
$project = Join-Path $root "src\NetGuardian.App\NetGuardian.App.csproj"
$out = Join-Path $root "dist\NetGuardian"

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "dotnet SDK not found at $dotnet"
}

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

New-Item -ItemType Directory -Force -Path $out | Out-Null
& $dotnet publish $project `
    --configuration Release `
    --self-contained false `
    --configfile (Join-Path $root "NuGet.Config") `
    --no-restore `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    --output $out
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Output "Published: $(Join-Path $out 'NetGuardian.exe')"
