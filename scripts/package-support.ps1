function Copy-NetGuardianPackageSupport {
    param([Parameter(Mandatory)][string]$SourceRoot, [Parameter(Mandatory)][string]$OutputDirectory)
    New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'scripts') -Force | Out-Null
    foreach ($name in @('README.md', 'Start-NetGuardian.vbs')) {
        Copy-Item -LiteralPath (Join-Path $SourceRoot $name) -Destination (Join-Path $OutputDirectory $name) -Force
    }
    foreach ($name in @('start-netguardian.ps1', 'register-netguardian-task.ps1', 'unregister-netguardian-task.ps1',
        'watch-netguardian.ps1', 'installer-maintenance.ps1')) {
        # Windows PowerShell 5.1 需 BOM 才能稳定读取中文脚本。
        $text = [IO.File]::ReadAllText((Join-Path $SourceRoot "scripts\$name"))
        [IO.File]::WriteAllText((Join-Path $OutputDirectory "scripts\$name"), $text, [Text.UTF8Encoding]::new($true))
    }
}
