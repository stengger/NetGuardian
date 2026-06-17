function Resolve-DotNetSdk {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command -and (Test-DotNetSdk $command.Source)) {
        return $command.Source
    }

    $fallback = "E:\Environment\dotnet-sdk-8.0\dotnet.exe"
    if ((Test-Path -LiteralPath $fallback) -and (Test-DotNetSdk $fallback)) {
        return $fallback
    }

    throw "dotnet SDK not found. Install .NET 8 SDK or set dotnet in PATH."
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
