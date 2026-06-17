$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = "E:\Environment\dotnet-sdk-8.0\dotnet.exe"
$publishedExe = Join-Path $root "dist\NetGuardian\NetGuardian.exe"
$exe = Join-Path $root "src\NetGuardian.App\bin\Debug\net8.0-windows\NetGuardian.exe"

if (Test-Path -LiteralPath $publishedExe) {
    Start-Process -FilePath $publishedExe -Verb RunAs
    return
}

if (-not (Test-Path -LiteralPath $exe)) {
    & (Join-Path $root "scripts\build.ps1")
}

if (Test-Path -LiteralPath $exe) {
    Start-Process -FilePath $exe -Verb RunAs
} else {
    Start-Process -FilePath $dotnet -ArgumentList @("run", "--project", (Join-Path $root "src\NetGuardian.App\NetGuardian.App.csproj"), "--no-restore") -Verb RunAs -WindowStyle Hidden
}
