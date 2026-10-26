$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('NetGuardian-script-' + [guid]::NewGuid().ToString('N'))
$originalProgramData = $env:ProgramData
$originalSdkOverride = $env:NETGUARDIAN_DOTNET
$env:ProgramData = $temporaryDirectory
$statusDirectory = Join-Path $temporaryDirectory 'NetGuardian'
New-Item -ItemType Directory -Path $statusDirectory -Force | Out-Null
$guardianPath = 'C:\Program Files\NetGuardian\guardian\NetGuardian.Guardian.exe'
$startedAt = [DateTimeOffset]::UtcNow.AddHours(-1)
$global:NetGuardianTestState = [pscustomobject]@{ TaskState = 'Running'; ProcessPath = $guardianPath; Starts = 0; Stops = 0; Launches = 0; Removing = $false; Removed = 0 }
function Start-Process { [CmdletBinding()]param($FilePath, $ArgumentList, $WindowStyle, $Verb) $global:NetGuardianTestState.Launches++ }
function Get-ScheduledTask {
    [CmdletBinding()]param($TaskName, $TaskPath)
    if ($global:NetGuardianTestState.Removing -and $TaskPath -ne '\') { throw '安全卸载必须限定根任务文件夹，不能命中其他文件夹的同名任务。' }
    $state = $global:NetGuardianTestState.TaskState
    if ($global:NetGuardianTestState.Removing -and $TaskName -eq 'NetGuardian' -and (Test-Path -LiteralPath (Join-Path $env:ProgramData 'NetGuardian\stop.request'))) { $state = 'Ready' }
    [pscustomobject]@{ State = $state; Actions = @([pscustomobject]@{ Execute = $global:NetGuardianTestState.ProcessPath }) }
}
function Unregister-ScheduledTask {
    [CmdletBinding(SupportsShouldProcess)]param($TaskName, $TaskPath)
    if ($TaskPath -ne '\') { throw '禁止注销其他任务文件夹中的同名任务。' }
    $global:NetGuardianTestState.Removed++
}
function Start-ScheduledTask { param($TaskName) $global:NetGuardianTestState.Starts++ }
function Stop-ScheduledTask {
    param($TaskName, $TaskPath)
    if ($global:NetGuardianTestState.Removing -and $TaskPath -ne '\') { throw '禁止停止其他任务文件夹中的同名任务。' }
    $global:NetGuardianTestState.Stops++
}
function Get-ScheduledTaskInfo { param($TaskName) [pscustomobject]@{ LastRunTime = (Get-Date).AddMinutes(-10) } }
function Get-Process { [CmdletBinding()]param($Id) [pscustomobject]@{ Path = $global:NetGuardianTestState.ProcessPath; StartTime = $startedAt.LocalDateTime } }
function Invoke-Watchdog { & (Join-Path $root 'scripts\watch-netguardian.ps1') -GuardianPath $guardianPath }
function Assert-Counts($expectedStarts, $expectedStops) {
    if ($global:NetGuardianTestState.Starts -ne $expectedStarts -or $global:NetGuardianTestState.Stops -ne $expectedStops) { throw ("监督动作错误：启动={0}，停止={1}" -f $global:NetGuardianTestState.Starts, $global:NetGuardianTestState.Stops) }
    $global:NetGuardianTestState.Starts = 0; $global:NetGuardianTestState.Stops = 0
}
function Write-Heartbeat($heartbeatAt) {
    @{ ProcessId = 123; ProcessStartedAt = $startedAt.ToString('o'); HeartbeatAt = $heartbeatAt.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $statusDirectory 'guardian-status.json') -Encoding utf8
}
try {
    foreach ($scriptFile in Get-ChildItem -LiteralPath (Join-Path $root 'scripts') -Filter '*.ps1') {
        $parseErrors = $null; $tokens = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
    }
    $global:NetGuardianTestState.TaskState = 'Ready'; Invoke-Watchdog; Assert-Counts 1 0
    $global:NetGuardianTestState.TaskState = 'Running'; Write-Heartbeat ([DateTimeOffset]::UtcNow); Invoke-Watchdog; Assert-Counts 0 0
    Write-Heartbeat ([DateTimeOffset]::UtcNow.AddMinutes(-5)); Invoke-Watchdog; Assert-Counts 1 1
    $global:NetGuardianTestState.ProcessPath = 'C:\Windows\System32\other.exe'; Invoke-Watchdog; Assert-Counts 0 0
    $global:NetGuardianTestState.ProcessPath = $guardianPath
    Set-Content -LiteralPath (Join-Path $statusDirectory 'guardian-status.json') -Value '{损坏'
    Invoke-Watchdog; Assert-Counts 1 1
    $fixture = Join-Path $temporaryDirectory '启动入口'
    New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts'), (Join-Path $fixture 'dist\NetGuardian') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'scripts\start-netguardian.ps1') -Destination (Join-Path $fixture 'scripts')
    Set-Content -LiteralPath (Join-Path $fixture 'dist\NetGuardian\NetGuardian.exe') -Value '模拟发布文件'
    $env:NETGUARDIAN_DOTNET = 'C:\不存在的SDK\dotnet.exe'
    & (Join-Path $fixture 'scripts\start-netguardian.ps1')
    if ($global:NetGuardianTestState.Launches -ne 1) { throw '正式版启动不应依赖 SDK。' }
    $global:NetGuardianTestState.Removing = $true
    & (Join-Path $root 'scripts\unregister-netguardian-task.ps1')
    Assert-Counts 0 1
    if ($global:NetGuardianTestState.Removed -ne 2) { throw '卸载必须同时移除主任务和监督任务。' }
    Write-Output '脚本语法、五个监督场景、无 SDK 启动入口及安全卸载全部通过；任务和进程调用均为替身。'
}
finally {
    $env:ProgramData = $originalProgramData
    $env:NETGUARDIAN_DOTNET = $originalSdkOverride
    Remove-Variable -Name NetGuardianTestState -Scope Global -ErrorAction SilentlyContinue
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryDirectory)
    if ($resolvedTemporary.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) -and (Test-Path -LiteralPath $resolvedTemporary)) {
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
