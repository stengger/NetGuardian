$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishedExe = Join-Path $root "dist\NetGuardian\NetGuardian.exe"
if (Test-Path -LiteralPath (Join-Path $root 'NetGuardian.exe')) { $publishedExe = Join-Path $root 'NetGuardian.exe' }
$exe = Join-Path $root "src\NetGuardian.App\bin\Debug\net8.0-windows\NetGuardian.exe"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Start-NetGuardianProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$ArgumentList = @(),
        [switch]$Hidden
    )

    $arguments = @{
        FilePath = $FilePath
    }

    if ($ArgumentList.Count -gt 0) {
        $arguments.ArgumentList = $ArgumentList
    }

    if ($Hidden) {
        $arguments.WindowStyle = "Hidden"
    }

    if (-not (Test-IsAdministrator)) {
        $arguments.Verb = "RunAs"
    }

    Start-Process @arguments
}

if (Test-Path -LiteralPath $publishedExe) {
    Start-NetGuardianProcess -FilePath $publishedExe
    return
}

if (-not (Test-Path -LiteralPath $exe)) {
    . (Join-Path $PSScriptRoot "resolve-dotnet.ps1")
    $dotnet = Resolve-DotNetSdk
    & (Join-Path $root "scripts\build.ps1")
}

if (Test-Path -LiteralPath $exe) {
    Start-NetGuardianProcess -FilePath $exe
} else {
    Start-NetGuardianProcess -FilePath $dotnet -ArgumentList @("run", "--project", (Join-Path $root "src\NetGuardian.App\NetGuardian.App.csproj"), "--no-restore") -Hidden
}
