$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = "E:\Environment\dotnet-sdk-8.0\dotnet.exe"
$exe = Join-Path $root "src\NetGuardian.App\bin\Debug\net8.0-windows\NetGuardian.App.exe"

if (-not (Test-Path -LiteralPath $exe)) {
    & (Join-Path $root "scripts\build.ps1")
}

if (Test-Path -LiteralPath $exe) {
    Start-Process -FilePath $exe
} else {
    Start-Process -FilePath $dotnet -ArgumentList @("run", "--project", (Join-Path $root "src\NetGuardian.App\NetGuardian.App.csproj"), "--no-restore") -WindowStyle Hidden
}

