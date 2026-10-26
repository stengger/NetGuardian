param([string]$TaskName = 'NetGuardian')
$ErrorActionPreference = 'Stop'
foreach ($name in @("$TaskName-Watchdog", $TaskName)) {
    $task = Get-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue
    if ($null -ne $task) {
        if ($name -eq $TaskName -and $task.State -eq 'Running') {
            $request = Join-Path $env:ProgramData 'NetGuardian\stop.request'
            Set-Content -LiteralPath $request -Value '停止并等待网络恢复收尾' -Encoding UTF8
            $deadline = (Get-Date).AddSeconds(90)
            do {
                Start-Sleep -Milliseconds 500
                $task = Get-ScheduledTask -TaskName $name -TaskPath '\'
            } while ($task.State -eq 'Running' -and (Get-Date) -lt $deadline)
            if ($task.State -eq 'Running') { throw '后台未能在90秒内安全停止，未强制终止。请检查心跳和恢复日志后在维护窗口处理。' }
        }
        else { Stop-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue }
        Unregister-ScheduledTask -TaskName $name -TaskPath '\' -Confirm:$false
        Write-Output "已停止并移除任务：$name"
    }
}
Write-Output '配置与日志已保留。'
