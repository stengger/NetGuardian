$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $root 'scripts\update-netguardian.ps1'
if (-not (Test-Path -LiteralPath $scriptPath)) { throw '缺少独立更新协调脚本，安装事务尚未实现。' }
. $scriptPath
$script:ProtectedDirectoryImplementation = ${function:Assert-NetGuardianUpdateProtectedDirectory}
$script:RestoreTasksImplementation = ${function:Restore-NetGuardianUpdateTasks}
$script:WaitParentImplementation = ${function:Wait-NetGuardianUpdateParent}
$script:StopBackgroundImplementation = ${function:Stop-NetGuardianUpdateBackground}

function Assert-Update($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-UpdateThrows([scriptblock]$Action, [string]$Expected) {
    try { & $Action } catch { if ($_.Exception.Message -notlike "*$Expected*") { throw }; return }
    throw "预期拒绝操作：$Expected"
}
foreach ($rawVersion in @('1.1.0                                             ', '1.1.0+hash', ' 1.1.0+hash  ')) {
    Assert-Update ((ConvertTo-NetGuardianUpdateVersion $rawVersion) -eq '1.1.0') '安装包空格和构建元数据必须归一为目标版本。'
}
Assert-Update ((ConvertTo-NetGuardianUpdateVersion '1.2.0     ') -ne '1.1.0') '版本归一化不得掩盖真实版本不匹配。'
$fixture = Join-Path $root ('.dotnet-home\update-test-' + [guid]::NewGuid().ToString('N'))
$script:TestInstall = Join-Path $fixture 'Program Files\NetGuardian'
$script:TestUpdates = Join-Path $fixture 'ProgramData\NetGuardian\updates'
$script:TestProgramFiles = Join-Path $fixture 'Program Files'
$script:TestMode = 'Success'
$script:InstallCalls = 0
$script:LaunchCalls = 0
$script:RestoreCalls = 0
$script:TestTasks = @()
$script:TestParentPresent = $true
$script:MutexReleases = 0
$script:MutexDisposals = 0
$script:MutexHeld = $false
$script:MutexProbesRejected = 0

# 仅替换操作系统边界；路径、文件备份、哈希、事务与恢复仍执行生产实现。
function Assert-NetGuardianUpdateProtectedDirectory([string]$Path) {
    if (($script:TestMode -eq 'UnsafeStopScript' -and $Path -ieq (Join-Path $script:TestInstall 'scripts\unregister-netguardian-task.ps1')) -or ($script:TestMode -eq 'UnsafeStopParent' -and $Path -ieq (Join-Path $script:TestInstall 'scripts'))) { throw '停止脚本或父目录允许非管理员写入。' }
}
function New-NetGuardianUpdateMutex {
    $mutex = New-Object PSObject
    $mutex | Add-Member NoteProperty IsProbe $script:MutexHeld
    $mutex | Add-Member ScriptMethod WaitOne { param($Milliseconds) if ($script:MutexHeld -or $script:TestMode -eq 'MutexBusy') { return $false }; $script:MutexHeld = $true; return $true }
    $mutex | Add-Member ScriptMethod ReleaseMutex { $script:MutexReleases++; $script:MutexHeld = $false }
    $mutex | Add-Member ScriptMethod Dispose { if (-not $this.IsProbe) { $script:MutexDisposals++ } }
    return $mutex
}
function Get-NetGuardianUpdateTasks { $script:TestTasks }
function Get-NetGuardianUpdateProcesses {
    if ($script:TestMode -eq 'UnmanagedGuardian') { [pscustomobject]@{ Path = (Join-Path $script:TestInstall 'guardian\NetGuardian.Guardian.exe') } }
}
function Get-NetGuardianUpdateParent([int]$ProcessId) {
    if ($script:TestParentPresent) { [pscustomobject]@{ Id = 123; Path = (Join-Path $script:TestInstall 'NetGuardian.exe'); StartedAt = ([datetime]'2026-11-10T01:00:00Z').AddTicks(2660) } }
}
function Wait-NetGuardianUpdateParent($Request) {
    if ($script:TestMode -eq 'CancelWhileWaiting') {
        [IO.File]::WriteAllText((Join-Path $Request.JobDirectory 'cancel.request'), '')
        & $script:WaitParentImplementation $Request
    }
    $script:TestParentPresent = $false
    if ($script:TestMode -eq 'CancelAfterExit') { [IO.File]::WriteAllText((Join-Path $Request.JobDirectory 'cancel.request'), '') }
}
function Get-NetGuardianUpdateVersion([string]$Path) {
    if ($Path.EndsWith('Setup.exe')) { if ($script:TestMode -eq 'BadVersion') { '1.2.0' } else { '1.1.0' } } else { (Get-Content -Raw -LiteralPath $Path).Trim() }
}
function Get-NetGuardianUpdateFreeBytes([string]$Path) { [long]10000000000 }
function Export-NetGuardianUpdateUninstall([string]$Destination) { [IO.File]::WriteAllText($Destination, '原卸载信息') }
function Restore-NetGuardianUpdateUninstall([string]$Source) { }
function Test-NetGuardianUpdateUninstall([string]$Source) { $true }
function Export-NetGuardianUpdateTask($Task) { '<原任务 />' }
function Restore-NetGuardianUpdateTasks($Tasks) {
    $script:RestoreCalls++
    if ($script:TestMode -eq 'RollbackFails') { throw '模拟任务恢复失败' }
    $script:TestTasks = @($Tasks)
}
function Test-NetGuardianUpdateTasks($Tasks) {
    return ($script:TestTasks.Count -eq @($Tasks).Count)
}
function Stop-NetGuardianUpdateBackground([string]$Directory) {
    if ($script:TestMode -eq 'StopTimeout') { throw '后台未能在90秒内安全停止' }
    $script:TestTasks = @()
}
function Invoke-NetGuardianUpdateInstaller($Request) {
    Assert-Update $script:MutexHeld '安装期间的界面启动探测必须被维护锁阻止。'
    $startupProbe = New-NetGuardianUpdateMutex
    try {
        Assert-Update (-not $startupProbe.WaitOne(0)) '模拟另一个界面在安装期间启动，维护门必须拒绝。'
        $script:MutexProbesRejected++
    } finally { $startupProbe.Dispose() }
    $script:InstallCalls++
    [IO.File]::WriteAllText((Join-Path $script:TestInstall 'NetGuardian.dll'), '1.1.0')
    [IO.File]::WriteAllText((Join-Path $script:TestInstall '新增.dll'), 'new')
    if ($script:TestMode -in @('InstallerFails', 'RollbackFails')) { return 5 }
    return 0
}
function Start-NetGuardianUpdateInterface([string]$Directory) {
    Assert-Update (-not $script:MutexHeld) '界面必须在释放维护锁后重新启动。'
    $script:LaunchCalls++
    if ($script:TestMode -eq 'RestartFails') { throw '模拟界面启动失败' }
}

function New-TestUpdateRequest([string]$Mode) {
    $script:TestMode = $Mode; $script:InstallCalls = 0; $script:LaunchCalls = 0; $script:RestoreCalls = 0; $script:TestParentPresent = $true
    New-Item -ItemType Directory -Path $script:TestInstall -Force | Out-Null
    $addedFile = Join-Path $script:TestInstall '新增.dll'
    if (Test-Path -LiteralPath $addedFile) { Remove-Item -LiteralPath $addedFile -Force }
    [IO.File]::WriteAllText((Join-Path $script:TestInstall 'NetGuardian.exe'), '原界面')
    [IO.File]::WriteAllText((Join-Path $script:TestInstall 'NetGuardian.dll'), '1.0.1')
    New-Item -ItemType Directory -Path (Join-Path $script:TestInstall 'scripts') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $script:TestInstall 'scripts\unregister-netguardian-task.ps1'), '# 隔离测试占位，不调用系统任务。')
    $script:TestTasks = @([pscustomobject]@{ TaskName = 'NetGuardian'; TaskPath = '\'; State = 'Running'; Settings = [pscustomobject]@{ Enabled = $true }; Actions = @([pscustomobject]@{ Execute = (Join-Path $script:TestInstall 'guardian\NetGuardian.Guardian.exe'); Arguments = '' }) })
    $job = Join-Path $script:TestUpdates ('jobs\' + [guid]::NewGuid().ToString('D'))
    New-Item -ItemType Directory -Path $job -Force | Out-Null
    $installer = Join-Path $script:TestUpdates 'Setup.exe'
    [IO.File]::WriteAllText($installer, '安装包')
    $request = [ordered]@{ InstallDirectory = $script:TestInstall; InstallerPath = $installer; Sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash; Version = '1.1.0'; ParentProcessId = 123; ParentStartedAt = '2026-11-10T01:00:00Z'; JobDirectory = $job }
    $path = Join-Path $job 'request.json'
    [IO.File]::WriteAllText($path, ($request | ConvertTo-Json))
    return $path
}
function Invoke-TestUpdate([string]$Path) {
    Invoke-NetGuardianUpdateTransaction -RequestFile $Path -ProgramFilesDirectory $script:TestProgramFiles -UpdatesDirectory $script:TestUpdates
}
try {
    $path = New-TestUpdateRequest 'MutexBusy'
    Assert-UpdateThrows { Invoke-TestUpdate $path } '另一个更新'
    Assert-Update ($script:InstallCalls -eq 0 -and $script:RestoreCalls -eq 0 -and $script:MutexReleases -eq 0 -and $script:MutexDisposals -eq 1) '互斥忙时不得安装、修改任务或释放未拥有的锁。'
    foreach ($mode in @('CancelBeforeReady', 'CancelWhileWaiting', 'CancelAfterExit')) {
        $path = New-TestUpdateRequest $mode
        if ($mode -eq 'CancelBeforeReady') { [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $path) 'cancel.request'), '') }
        $result = Invoke-TestUpdate $path
        Assert-Update (-not $result.Success -and $result.Message -like '*取消*' -and $script:InstallCalls -eq 0 -and $script:RestoreCalls -eq 0) ("$mode 必须在安装和任务变更之前退出。")
        if ($mode -eq 'CancelBeforeReady') { Assert-Update (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $path) 'ready.json'))) '准备前取消不得生成就绪信号。' }
    }
    Assert-Update ($script:MutexReleases -eq 3 -and $script:MutexDisposals -eq 4) '取消作业必须释放并销毁其拥有的互斥锁。'
    $path = New-TestUpdateRequest 'Success'
    $result = Invoke-TestUpdate $path
    Assert-Update $result.Success ('成功安装没有返回成功：' + ($result | ConvertTo-Json -Compress))
    Assert-Update ($script:InstallCalls -eq 1 -and $script:LaunchCalls -eq 1) '成功安装必须调用一次安装器并启动界面。'
    Assert-Update ($script:MutexProbesRejected -eq 1) '必须实际探测安装期间的模拟界面启动被拒绝。'
    Assert-Update ($script:MutexReleases -eq 4 -and $script:MutexDisposals -eq 5) '成功作业必须释放并销毁互斥锁。'
    Assert-Update (Test-Path -LiteralPath (Join-Path (Split-Path $path) 'ready.json')) '缺少准备握手。'
    Assert-Update (Test-Path -LiteralPath (Join-Path $script:TestUpdates 'last-result.json')) '缺少全局结果。'

    $path = New-TestUpdateRequest 'RestartFails'
    $result = Invoke-TestUpdate $path
    Assert-Update (-not $result.Success -and $result.Stage -eq 'RestartFailed' -and -not $result.RollbackSucceeded) '界面启动失败应单独报告，不能回滚已完成安装。'
    Assert-Update ((Get-Content -Raw (Join-Path $script:TestInstall 'NetGuardian.dll')) -eq '1.1.0') '界面启动失败不能恢复旧程序。'
    $persisted = Get-Content -Raw (Join-Path (Split-Path -Parent $path) 'result.json') | ConvertFrom-Json
    Assert-Update ($persisted.Stage -eq 'RestartFailed' -and $script:LaunchCalls -eq 1) '启动失败结果必须落盘且不得重复启动。'
    $latest = Get-Content -Raw (Join-Path $script:TestUpdates 'last-result.json') | ConvertFrom-Json
    Assert-Update ($latest.Stage -eq 'RestartFailed') '启动失败必须同步写入全局更新结果。'

    foreach ($mode in @('UnsafeStopScript', 'UnsafeStopParent')) {
        $path = New-TestUpdateRequest $mode
        $result = Invoke-TestUpdate $path
        Assert-Update (-not $result.Success -and $result.Message -like '*非管理员写入*' -and $script:InstallCalls -eq 0 -and $script:RestoreCalls -eq 0) '停止脚本或父目录权限宽松时不得修改任务或安装。'
        Assert-Update (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $path) 'ready.json'))) '停止脚本未经保护链核验不得发送就绪信号。'
    }
    $path = New-TestUpdateRequest 'ChangedStopScript'
    $stopScript = Join-Path $script:TestInstall 'scripts\unregister-netguardian-task.ps1'
    $stopHash = (Get-FileHash -LiteralPath $stopScript -Algorithm SHA256).Hash
    [IO.File]::AppendAllText($stopScript, '# 已发生变化')
    Assert-UpdateThrows { & $script:StopBackgroundImplementation -Directory $script:TestInstall -StopScriptPath $stopScript -ExpectedSha256 $stopHash -ProgramFilesDirectory $script:TestProgramFiles -ScriptProtectionRoot $script:TestProgramFiles } '哈希'

    $path = New-TestUpdateRequest 'StopScriptReparse'
    $scriptsDirectory = Join-Path $script:TestInstall 'scripts'
    $stopTarget = Join-Path $fixture 'stop-script-target'
    New-Item -ItemType Directory -Path $stopTarget -Force | Out-Null
    Remove-Item -LiteralPath (Join-Path $scriptsDirectory 'unregister-netguardian-task.ps1') -Force
    [IO.Directory]::Delete($scriptsDirectory)
    New-Item -ItemType Junction -Path $scriptsDirectory -Target $stopTarget | Out-Null
    try {
        $result = Invoke-TestUpdate $path
        Assert-Update (-not $result.Success -and $result.Message -like '*重解析*' -and $script:InstallCalls -eq 0) '停止脚本父目录重解析点必须阻止更新。'
    } finally { [IO.Directory]::Delete($scriptsDirectory) }

    $path = New-TestUpdateRequest 'ForeignTask'
    $script:TestTasks[0].Actions[0].Execute = 'C:\其他安装\NetGuardian.Guardian.exe'
    $result = Invoke-TestUpdate $path
    Assert-Update (-not $result.Success -and $script:InstallCalls -eq 0 -and $script:RestoreCalls -eq 0) '外来任务不得安装或恢复任务。'
    Assert-Update (-not (Test-Path -LiteralPath (Join-Path (Split-Path $path) 'ready.json'))) '外来任务不得发送准备完成信号。'

    $path = New-TestUpdateRequest 'StopTimeout'
    $result = Invoke-TestUpdate $path
    Assert-Update (-not $result.Success -and $script:InstallCalls -eq 0) '后台停止超时必须阻止安装。'
    Assert-Update ((Get-Content -Raw (Join-Path $script:TestInstall 'NetGuardian.dll')) -eq '1.0.1') '停止超时不应修改程序。'

    $path = New-TestUpdateRequest 'InstallerFails'
    $result = Invoke-TestUpdate $path
    Assert-Update (-not $result.Success -and $result.RollbackSucceeded) '安装失败应完成恢复。'
    Assert-Update ((Get-Content -Raw (Join-Path $script:TestInstall 'NetGuardian.dll')) -eq '1.0.1') '未恢复原程序文件。'
    Assert-Update (-not (Test-Path (Join-Path $script:TestInstall '新增.dll'))) '恢复未清理新增文件。'
    Assert-Update ($script:TestTasks.Count -eq 1 -and $script:TestTasks[0].Enabled -and $script:TestTasks[0].Running) '未恢复任务启用与运行状态。'
    Assert-Update ($script:LaunchCalls -eq 1) '恢复成功后没有重新打开原界面。'

    $path = New-TestUpdateRequest 'RollbackFails'
    $result = Invoke-TestUpdate $path
    Assert-Update (-not $result.Success -and -not $result.RollbackSucceeded -and $script:LaunchCalls -eq 0) '恢复失败不能启动半更新程序。'

    foreach ($mode in @('BadHash', 'BadVersion', 'BadIdentity', 'UnmanagedGuardian')) {
        $path = New-TestUpdateRequest $mode
        $request = Get-Content -Raw $path | ConvertFrom-Json
        if ($mode -eq 'BadHash') { $request.Sha256 = '0' * 64 }
        if ($mode -eq 'BadIdentity') { $request.ParentStartedAt = '2026-11-10T02:00:00Z' }
        if ($mode -eq 'UnmanagedGuardian') { $script:TestTasks = @() }
        [IO.File]::WriteAllText($path, ($request | ConvertTo-Json))
        $result = Invoke-TestUpdate $path
        Assert-Update (-not $result.Success -and $script:InstallCalls -eq 0 -and $script:RestoreCalls -eq 0) ("无效的 $mode 请求不得安装或修改任务。")
        Assert-Update (-not (Test-Path -LiteralPath (Join-Path (Split-Path $path) 'ready.json'))) ("无效的 $mode 请求不得通知界面退出。")
    }

    $foreign = [pscustomobject]@{ TaskName = 'NetGuardian-Watchdog'; Actions = @([pscustomobject]@{ Execute = (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'); Arguments = '-File "C:\外来目录\watch-netguardian.ps1"' }) }
    Assert-UpdateThrows { Assert-NetGuardianUpdateTaskOwnership @($foreign) $script:TestInstall } '外来'
    $ownedWatchdog = [pscustomobject]@{ TaskName = 'NetGuardian-Watchdog'; Actions = @([pscustomobject]@{ Execute = 'powershell.exe'; Arguments = ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -TaskName "NetGuardian" -GuardianPath "{1}"' -f (Join-Path $script:TestInstall 'scripts\watch-netguardian.ps1'), (Join-Path $script:TestInstall 'guardian\NetGuardian.Guardian.exe')) }) }
    Assert-NetGuardianUpdateTaskOwnership @($ownedWatchdog) $script:TestInstall

    # ACL 对象仅在内存中构造，验证普通用户读取权限不会误判为写入权限。
    $script:FakeAcl = New-Object Security.AccessControl.DirectorySecurity
    $script:FakeAcl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $readers = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')
    $script:FakeAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($readers, 'ReadAndExecute', 'Allow'))
    function Get-Acl { param($LiteralPath) $script:FakeAcl }
    & $script:ProtectedDirectoryImplementation $script:TestUpdates
    $script:FakeAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($readers, 'Write', 'Allow'))
    Assert-UpdateThrows { & $script:ProtectedDirectoryImplementation $script:TestUpdates } '非管理员写入'
    foreach ($generic in @('GA', 'GW')) {
        $script:FakeAcl = New-Object Security.AccessControl.DirectorySecurity
        $script:FakeAcl.SetSecurityDescriptorSddlForm(('O:BAG:BAD:(A;;{0};;;AU)' -f $generic))
        Assert-UpdateThrows { & $script:ProtectedDirectoryImplementation $script:TestUpdates } '非管理员写入'
    }
    $script:FakeAcl = New-Object Security.AccessControl.DirectorySecurity
    $script:FakeAcl.SetSecurityDescriptorSddlForm('O:BAG:BAD:(A;CIIO;GA;;;CO)(A;;FRFX;;;BU)')
    & $script:ProtectedDirectoryImplementation $script:TestUpdates

    & {
        # 独立作用域替身验证：安全停止超时后保留仍运行的原守护，只补回监督任务。
        $script:DirectUnregisterCalls = 0; $script:DirectRegisterCalls = 0
        $script:DirectTasks = @([pscustomobject]@{ TaskName = 'NetGuardian'; State = 'Running'; Settings = [pscustomobject]@{ Enabled = $true }; Actions = @([pscustomobject]@{ Execute = (Join-Path $script:TestInstall 'guardian\NetGuardian.Guardian.exe') }); Xml = '<Task><Settings><Enabled>true</Enabled></Settings></Task>' })
        function Get-NetGuardianUpdateTasks { $script:DirectTasks }
        function Export-NetGuardianUpdateTask($Task) { $Task.Xml }
        function Unregister-ScheduledTask { param($TaskName, $TaskPath, $Confirm) $script:DirectUnregisterCalls++ }
        function Register-ScheduledTask { param($TaskName, $TaskPath, $Xml, [switch]$Force) $script:DirectRegisterCalls++ }
        function Enable-ScheduledTask { param($TaskName, $TaskPath) }
        function Disable-ScheduledTask { param($TaskName, $TaskPath) }
        function Start-ScheduledTask { param($TaskName, $TaskPath) throw '不应重复启动运行中的守护。' }
        $saved = @([pscustomobject]@{ TaskName = 'NetGuardian'; Xml = $script:DirectTasks[0].Xml; Enabled = $true; Running = $true }, [pscustomobject]@{ TaskName = 'NetGuardian-Watchdog'; Xml = '<Task />'; Enabled = $true; Running = $false })
        & $script:RestoreTasksImplementation $saved $script:TestInstall
        Assert-Update ($script:DirectUnregisterCalls -eq 0 -and $script:DirectRegisterCalls -eq 1) '必须保留运行中原守护并补回监督任务。'
    }

    Assert-UpdateThrows { Assert-NetGuardianUpdateChildPath $script:TestProgramFiles $script:TestProgramFiles } '子目录'
    Assert-UpdateThrows { Assert-NetGuardianUpdateChildPath ($script:TestProgramFiles + '-other\NetGuardian') $script:TestProgramFiles } '子目录'
    $path = New-TestUpdateRequest 'BadRequest'
    $request = Get-Content -Raw $path | ConvertFrom-Json
    $request.JobDirectory = Join-Path $fixture '外部目录'
    [IO.File]::WriteAllText($path, ($request | ConvertTo-Json))
    Assert-UpdateThrows { Invoke-TestUpdate $path } 'jobs'
    $link = Join-Path $script:TestProgramFiles '重解析目录'
    New-Item -ItemType Junction -Path $link -Target $script:TestInstall | Out-Null
    Assert-UpdateThrows { Assert-NetGuardianUpdateSafePath $link } '重解析'
    # Windows PowerShell 5.1 的 Remove-Item 对目录联接有已知异常；仅删除联接本身。
    [IO.Directory]::Delete($link)
    Write-Output '更新互斥、取消、成功安装、任务归属、安全停止、失败恢复、恢复失败阻断、哈希/版本/进程身份/未托管后台拒绝、ACL/路径/重解析点隔离检查全部通过。'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $allowed = [IO.Path]::GetFullPath((Join-Path $root '.dotnet-home')) + '\'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw '测试清理目录越界。' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
