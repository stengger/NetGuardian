[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$TaskName = 'NetGuardian',
    [string]$SettingsPath,
    [switch]$RunNow
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $root 'dist\NetGuardian'
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'guardian\NetGuardian.Guardian.exe'))) { $packageRoot = $root }
$guardian = Join-Path $packageRoot 'guardian\NetGuardian.Guardian.exe'
$watchdog = Join-Path $PSScriptRoot 'watch-netguardian.ps1'
$machineDirectory = Join-Path $env:ProgramData 'NetGuardian'
$machineSettings = Join-Path $machineDirectory 'settings.json'
if ([string]::IsNullOrWhiteSpace($SettingsPath)) {
    $SettingsPath = if (Test-Path -LiteralPath $machineSettings) { $machineSettings } else { Join-Path $env:APPDATA 'NetGuardian\settings.json' }
}
if (-not (Test-Path -LiteralPath $guardian)) { throw '未找到后台守护，请先运行 scripts\publish.ps1。' }
if (-not (Test-Path -LiteralPath $SettingsPath)) { throw "未找到配置：$SettingsPath。请先在界面保存两个 Wi-Fi 配置。" }
& $guardian --validate --settings $SettingsPath
if ($LASTEXITCODE -ne 0) { throw '配置校验失败，未安装后台。' }
if (-not $PSCmdlet.ShouldProcess($machineDirectory, '安装开机后台守护和心跳监督任务')) { return }
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing -and $existing.State -eq 'Running') { throw '后台正在运行，请在维护窗口停止旧后台后再升级。' }
$installDirectory = if (Test-Path -LiteralPath (Join-Path $root 'guardian\NetGuardian.Guardian.exe')) {
    # 从安装包内运行时，后台沿用用户选择的安装目录。
    $root
} else { Join-Path $env:ProgramFiles 'NetGuardian' }
. (Join-Path $PSScriptRoot 'installer-maintenance.ps1') -InstallDirectory $installDirectory
Assert-NetGuardianMachineInstallPath -InstallDirectory $installDirectory
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
if ([IO.Path]::GetFullPath($packageRoot) -ne [IO.Path]::GetFullPath($installDirectory)) {
    Copy-Item -LiteralPath (Join-Path $packageRoot 'guardian') -Destination $installDirectory -Recurse -Force
    Get-ChildItem -LiteralPath $packageRoot -File | Copy-Item -Destination $installDirectory -Force
}
$installedScripts = Join-Path $installDirectory 'scripts'
New-Item -ItemType Directory -Path $installedScripts -Force | Out-Null
foreach ($scriptName in @('watch-netguardian.ps1','register-netguardian-task.ps1','unregister-netguardian-task.ps1','start-netguardian.ps1','installer-maintenance.ps1')) {
    $sourceScript = Join-Path $PSScriptRoot $scriptName
    $targetScript = Join-Path $installedScripts $scriptName
    if ([IO.Path]::GetFullPath($sourceScript) -ne [IO.Path]::GetFullPath($targetScript)) { Copy-Item -LiteralPath $sourceScript -Destination $targetScript -Force }
}
$sourceLauncher = Join-Path $root 'Start-NetGuardian.vbs'
$targetLauncher = Join-Path $installDirectory 'Start-NetGuardian.vbs'
if ([IO.Path]::GetFullPath($sourceLauncher) -ne [IO.Path]::GetFullPath($targetLauncher)) { Copy-Item -LiteralPath $sourceLauncher -Destination $targetLauncher -Force }
$guardian = Join-Path $installDirectory 'guardian\NetGuardian.Guardian.exe'
$watchdog = Join-Path $installedScripts 'watch-netguardian.ps1'
New-Item -ItemType Directory -Path $machineDirectory -Force | Out-Null
# 机器级配置只允许管理员和 SYSTEM 修改。
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
    $identity = New-Object System.Security.Principal.SecurityIdentifier($sid)
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $machineDirectory -AclObject $acl
if ([IO.Path]::GetFullPath($SettingsPath) -ne [IO.Path]::GetFullPath($machineSettings)) {
    if (Test-Path -LiteralPath $machineSettings) { Copy-Item -LiteralPath $machineSettings -Destination "$machineSettings.bak" -Force }
    Copy-Item -LiteralPath $SettingsPath -Destination $machineSettings -Force
}
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$action = New-ScheduledTaskAction -Execute $guardian -Argument ('--settings "{0}"' -f $machineSettings) -WorkingDirectory (Split-Path -Parent $guardian)
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description '无人登录时监测网络并执行分级恢复。' -Force | Out-Null
$watchAction = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -TaskName "{1}" -GuardianPath "{2}"' -f $watchdog, $TaskName, $guardian)
$watchTriggers = @((New-ScheduledTaskTrigger -AtStartup), (New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)))
$watchSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName "$TaskName-Watchdog" -Action $watchAction -Trigger $watchTriggers -Principal $principal -Settings $watchSettings -Description '心跳超时或进程退出后重新启动网络守护。' -Force | Out-Null
if ($RunNow) { Start-ScheduledTask -TaskName $TaskName }
Write-Output "已安装开机守护：$TaskName；程序：$installDirectory；配置：$machineSettings。请重新打开界面以使用共享配置。"
