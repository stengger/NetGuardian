param([string]$TaskName = 'NetGuardian', [Parameter(Mandatory)][string]$GuardianPath)
$ErrorActionPreference = 'Stop'
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -eq $task) { return }
# 只监督目标可执行文件对应的任务，不根据磁盘旧 PID 终止任何进程。
if (-not ($task.Actions | Where-Object { $_.Execute -and [IO.Path]::GetFullPath($_.Execute) -eq [IO.Path]::GetFullPath($GuardianPath) })) { return }
if ($task.State -ne 'Running') { Start-ScheduledTask -TaskName $TaskName; return }
$statusPath = Join-Path $env:ProgramData 'NetGuardian\guardian-status.json'
try { $status = Get-Content -Raw -LiteralPath $statusPath | ConvertFrom-Json }
catch {
    $info = Get-ScheduledTaskInfo -TaskName $TaskName
    if (((Get-Date) - $info.LastRunTime).TotalMinutes -gt 3) {
        Stop-ScheduledTask -TaskName $TaskName
        Start-ScheduledTask -TaskName $TaskName
    }
    return
}
if (([DateTimeOffset]::UtcNow - [DateTimeOffset]$status.HeartbeatAt).TotalMinutes -lt 3) { return }
# 新进程写首次心跳前给予三分钟宽限；旧进程的心跳不能永久屏蔽恢复。
$info = Get-ScheduledTaskInfo -TaskName $TaskName
if (((Get-Date) - $info.LastRunTime).TotalMinutes -lt 3) { return }
Stop-ScheduledTask -TaskName $TaskName
Start-ScheduledTask -TaskName $TaskName
