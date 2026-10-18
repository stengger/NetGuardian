function Resolve-DotNetSdk {
    # 1) Explicit override via environment variable (path to dotnet.exe or its folder).
    $override = $env:NETGUARDIAN_DOTNET
    if ($override) {
        $candidate = if (Test-Path -LiteralPath $override -PathType Container) {
            Join-Path $override "dotnet.exe"
        } else {
            $override
        }
        if ((Test-Path -LiteralPath $candidate) -and (Test-DotNetSdk $candidate)) {
            return $candidate
        }
        Write-Warning "NETGUARDIAN_DOTNET is set but does not point to a usable dotnet SDK: $override"
    }

    # 2) dotnet on PATH.
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command -and (Test-DotNetSdk $command.Source)) {
        return $command.Source
    }

    throw "dotnet SDK not found. Install .NET 8 SDK, add dotnet to PATH, or set NETGUARDIAN_DOTNET to the SDK location."
}

function Test-DotNetSdk {
    param([string] $Path)

    try {
        $sdks = & $Path --list-sdks 2>$null
        return $LASTEXITCODE -eq 0 -and $null -ne $sdks -and $sdks.Count -gt 0
    } catch {
        return $false
    }
}
