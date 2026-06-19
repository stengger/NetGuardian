param(
    [string]$TaskName = "NetGuardian"
)

$ErrorActionPreference = "Stop"

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -eq $task) {
    Write-Output "Scheduled task '$TaskName' is not registered."
    return
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Output "Unregistered scheduled task '$TaskName'."
