param(
    [string]$TaskName = "NetGuardian",
    [switch]$RunNow
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $root "Start-NetGuardian.vbs"

if (-not (Test-Path -LiteralPath $launcher)) {
    throw "Launcher not found: $launcher"
}

$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$quotedLauncher = '"' + $launcher + '"'
$action = New-ScheduledTaskAction -Execute "wscript.exe" -Argument $quotedLauncher -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 5)

$description = "Start NetGuardian at user logon with highest privileges."
$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description $description
Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
}

Write-Output "Registered scheduled task '$TaskName' for $currentUser."
Write-Output "Launcher: $launcher"
