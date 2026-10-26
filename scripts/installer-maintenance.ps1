param(
    [ValidateSet('CheckInstall', 'Uninstall')][string]$Mode = 'CheckInstall',
    [string]$InstallDirectory,
    [string]$ErrorFile
)
$ErrorActionPreference = 'Stop'

function Assert-NetGuardianMachineInstallPath {
    param([Parameter(Mandatory)][string]$InstallDirectory, [string]$ProgramFilesDirectory = $env:ProgramFiles)
    $trustedRoot = [IO.Path]::GetFullPath($ProgramFilesDirectory).TrimEnd('\') + '\'
    $directory = [IO.Path]::GetFullPath($InstallDirectory)
    if (-not $directory.StartsWith($trustedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'SYSTEM 后台只允许从受保护的 Program Files 子目录启动；请将程序安装到该目录后开启后台。'
    }
}

function Get-NetGuardianInstallerProcesses([string]$Directory) {
    $paths = @((Join-Path $Directory 'NetGuardian.exe'), (Join-Path $Directory 'guardian\NetGuardian.Guardian.exe'))
    @(Get-Process -Name NetGuardian, NetGuardian.Guardian -ErrorAction SilentlyContinue | Where-Object {
        if (-not $_.Path) { throw '无法核对 NetGuardian 进程路径，请先退出程序并停止后台。' }
        $paths -contains $_.Path
    })
}

function Get-NetGuardianInstallerTasks([string]$Directory) {
    $tasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object {
        $_.TaskPath -eq '\' -and $_.TaskName -in @('NetGuardian', 'NetGuardian-Watchdog')
    })
    $guardian = Join-Path $Directory 'guardian\NetGuardian.Guardian.exe'
    $watchdog = '"' + (Join-Path $Directory 'scripts\watch-netguardian.ps1') + '"'
    foreach ($task in $tasks) {
        $owned = if ($task.TaskName -eq 'NetGuardian') {
            @($task.Actions | Where-Object { $_.Execute -and [IO.Path]::GetFullPath($_.Execute) -eq $guardian }).Count -gt 0
        } else {
            @($task.Actions | Where-Object { $_.Arguments -and $_.Arguments.IndexOf($watchdog, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0
        }
        if (-not $owned) { throw '已有 NetGuardian 任务指向其他目录；请先通过原安装目录移除后台任务。' }
    }
    $tasks
}

function Assert-NetGuardianInstallReady([Parameter(Mandatory)][string]$InstallDirectory) {
    $directory = [IO.Path]::GetFullPath($InstallDirectory)
    if (@(Get-NetGuardianInstallerProcesses $directory).Count) { throw '请从托盘退出 NetGuardian，并停止后台守护后重试安装。' }
    if (@(Get-NetGuardianInstallerTasks $directory).Count) {
        throw '请先停止后台并移除后台及监督任务，再重试安装；就绪任务仍可能在更新期间自动启动。'
    }
}

function Invoke-NetGuardianUninstallPreparation([Parameter(Mandatory)][string]$InstallDirectory) {
    $directory = [IO.Path]::GetFullPath($InstallDirectory)
    if (@(Get-NetGuardianInstallerProcesses $directory | Where-Object { [IO.Path]::GetFileName($_.Path) -eq 'NetGuardian.exe' }).Count) {
        throw '请先从托盘退出 NetGuardian，再重试卸载。'
    }
    # 先核验归属，再复用停止监督、请求退出及等待收尾的既有流程。
    $null = @(Get-NetGuardianInstallerTasks $directory)
    & (Join-Path $directory 'scripts\unregister-netguardian-task.ps1')
    if (@(Get-NetGuardianInstallerProcesses $directory).Count) { throw '后台仍在运行，已中止卸载；请停止后台后重试。' }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { throw '必须指定安装目录。' }
        if ($Mode -eq 'Uninstall') { Invoke-NetGuardianUninstallPreparation $InstallDirectory }
        else { Assert-NetGuardianInstallReady $InstallDirectory }
        exit 0
    } catch {
        if ($ErrorFile) { [IO.File]::WriteAllText($ErrorFile, $_.Exception.Message, [Text.UTF8Encoding]::new($true)) }
        Write-Error $_
        exit 1
    }
}
