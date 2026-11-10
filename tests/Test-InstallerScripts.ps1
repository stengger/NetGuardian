$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'scripts\installer-maintenance.ps1')
. (Join-Path $root 'scripts\package-support.ps1')

function Assert-Throws([scriptblock]$Action, [string]$Expected) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        return
    }
    throw "预期拒绝操作：$Expected"
}

$fixture = Join-Path $root ('.dotnet-home\installer-test-' + [guid]::NewGuid().ToString('N'))
$install = Join-Path $fixture '中文 安装目录'
$global:InstallerTestProcesses = @()
$global:InstallerTestTasks = @()
$global:InstallerUnregisterCalls = 0
function Get-Process { [CmdletBinding()]param($Name) $global:InstallerTestProcesses }
function Get-ScheduledTask { [CmdletBinding()]param() $global:InstallerTestTasks }
try {
    Assert-NetGuardianMachineInstallPath -InstallDirectory 'C:\Program Files\NetGuardian' -ProgramFilesDirectory 'C:\Program Files'
    Assert-Throws { Assert-NetGuardianMachineInstallPath -InstallDirectory 'C:\Users\Public\NetGuardian' -ProgramFilesDirectory 'C:\Program Files' } 'Program Files'
    Assert-Throws { Assert-NetGuardianMachineInstallPath -InstallDirectory 'C:\Program Files-other\NetGuardian' -ProgramFilesDirectory 'C:\Program Files' } 'Program Files'
    New-Item -ItemType Directory -Path (Join-Path $install 'scripts') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $install 'scripts\unregister-netguardian-task.ps1') -Value '$global:InstallerUnregisterCalls++; $global:InstallerTestTasks=@(); $global:InstallerTestProcesses=@()'
    Assert-NetGuardianInstallReady -InstallDirectory $install
    $global:InstallerTestProcesses = @([pscustomobject]@{ Path = (Join-Path $install 'NetGuardian.exe') })
    Assert-Throws { Assert-NetGuardianInstallReady -InstallDirectory $install } '退出'
    $global:InstallerTestProcesses = @()
    $global:InstallerTestTasks = @([pscustomobject]@{
        TaskName = 'NetGuardian'; TaskPath = '\'; State = 'Running'
        Actions = @([pscustomobject]@{ Execute = (Join-Path $install 'guardian\NetGuardian.Guardian.exe'); Arguments = '' })
    })
    Assert-Throws { Assert-NetGuardianInstallReady -InstallDirectory $install } '停止后台'
    $global:InstallerTestTasks[0].State = 'Ready'
    Assert-Throws { Assert-NetGuardianInstallReady -InstallDirectory $install } '停止后台'
    $global:InstallerTestTasks[0].Actions[0].Execute = 'C:\其他目录\guardian\NetGuardian.Guardian.exe'
    Assert-Throws { Invoke-NetGuardianUninstallPreparation -InstallDirectory $install } '其他目录'
    if ($global:InstallerUnregisterCalls -ne 0) { throw '不能删除其他安装目录的后台任务。' }
    $global:InstallerTestTasks[0].Actions[0].Execute = Join-Path $install 'guardian\NetGuardian.Guardian.exe'
    Invoke-NetGuardianUninstallPreparation -InstallDirectory $install
    if ($global:InstallerUnregisterCalls -ne 1) { throw '卸载没有调用安全停止入口。' }
    Set-Content -LiteralPath (Join-Path $install 'scripts\unregister-netguardian-task.ps1') -Value 'throw "后台未能安全停止"'
    Assert-Throws { Invoke-NetGuardianUninstallPreparation -InstallDirectory $install } '后台未能安全停止'

    Copy-NetGuardianPackageSupport -SourceRoot $root -OutputDirectory $install
    foreach ($relative in @('README.md', 'Start-NetGuardian.vbs', 'scripts\start-netguardian.ps1', 'scripts\watch-netguardian.ps1',
        'scripts\register-netguardian-task.ps1', 'scripts\unregister-netguardian-task.ps1', 'scripts\installer-maintenance.ps1', 'scripts\update-netguardian.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $install $relative))) { throw "缺少发布入口：$relative" }
    }
    foreach ($script in Get-ChildItem (Join-Path $install 'scripts') -Filter '*.ps1') {
        $bytes = [IO.File]::ReadAllBytes($script.FullName)
        if ($bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191) { throw "脚本缺少 UTF-8 BOM：$($script.Name)" }
        $errors = $null; $tokens = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
        if ($errors.Count) { throw ($errors | Out-String) }
    }
    Write-Output '安装运行状态、任务归属、卸载失败阻断、中文路径与发布支持文件检查全部通过；系统调用均为替身。'
}
finally {
    Remove-Variable InstallerTestProcesses, InstallerTestTasks, InstallerUnregisterCalls -Scope Global -ErrorAction SilentlyContinue
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $root '.dotnet-home')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '测试清理目录越界。' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
