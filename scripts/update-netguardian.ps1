param([string]$RequestFile)
$ErrorActionPreference = 'Stop'

function Assert-NetGuardianUpdateChildPath([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not [IO.Path]::IsPathRooted($Path) -or -not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or $Path.IndexOf(':', 2) -ge 0) {
        throw '路径必须是受保护根目录的真实子目录。'
    }
    return $full
}

function Assert-NetGuardianUpdateSafePath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "拒绝重解析点路径：$current" }
        }
        $current = Split-Path -Parent $current
    }
}

function Assert-NetGuardianUpdateProtectedDirectory([string]$Path) {
    # 仅 SYSTEM、管理员及 TrustedInstaller 可以写入提升权限读取的请求和脚本。
    $allowed = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
    $acl = Get-Acl -LiteralPath $Path
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($owner -notin $allowed) { throw "更新目录所有者不受信任：$Path" }
    $write = [int][Security.AccessControl.FileSystemRights]::Write -bor [int][Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [int][Security.AccessControl.FileSystemRights]::Delete -bor [int][Security.AccessControl.FileSystemRights]::ChangePermissions -bor [int][Security.AccessControl.FileSystemRights]::TakeOwnership -bor 0x10000000 -bor 0x40000000
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        # 仅继承规则不作用于当前对象；每个实际读取的子对象另行核验。
        if ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) { continue }
        if ($rule.AccessControlType -eq 'Allow' -and ([int]$rule.FileSystemRights -band $write) -and $rule.IdentityReference.Value -notin $allowed) { throw "更新目录允许非管理员写入：$Path" }
    }
}

function Assert-NetGuardianUpdateProtectionChain([string]$Path, [string]$Root) {
    $current = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $protectedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if ($current -ine $protectedRoot) { $null = Assert-NetGuardianUpdateChildPath $current $protectedRoot }
    Assert-NetGuardianUpdateSafePath $current
    while ($true) {
        Assert-NetGuardianUpdateProtectedDirectory $current
        if ($current -ieq $protectedRoot) { break }
        $current = Split-Path -Parent $current
    }
}

function Write-NetGuardianUpdateJson([string]$Path, $Value) {
    Assert-NetGuardianUpdateSafePath $Path
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($true))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function ConvertTo-NetGuardianUpdateVersion([string]$Version) { (($Version -split '\+')[0]).Trim() }
function Get-NetGuardianUpdateVersion([string]$Path) { ConvertTo-NetGuardianUpdateVersion ([Diagnostics.FileVersionInfo]::GetVersionInfo($Path).ProductVersion) }
function Get-NetGuardianUpdateParent([int]$ProcessId) {
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $process) { [pscustomobject]@{ Id = $process.Id; Path = $process.Path; StartedAt = $process.StartTime.ToUniversalTime() } }
}
function Assert-NetGuardianUpdateParent($Request, $Process) {
    # Windows PowerShell JSON 日期可能截断亚毫秒；PID、路径仍严格核对。
    if ($null -eq $Process -or $Process.Id -ne $Request.ParentProcessId -or $Process.Path -ine (Join-Path $Request.InstallDirectory 'NetGuardian.exe') -or [Math]::Abs((([datetime]$Process.StartedAt).ToUniversalTime() - ([datetime]$Request.ParentStartedAt).ToUniversalTime()).TotalMilliseconds) -ge 1) { throw '原界面的 PID、路径或启动时间不匹配。' }
}
function Wait-NetGuardianUpdateParent($Request) {
    $deadline = [datetime]::UtcNow.AddSeconds(120)
    while ($true) {
        Assert-NetGuardianUpdateNotCancelled $Request
        $process = Get-NetGuardianUpdateParent $Request.ParentProcessId
        if ($null -eq $process) { return }
        Assert-NetGuardianUpdateParent $Request $process
        if ([datetime]::UtcNow -ge $deadline) { throw '等待原界面正常退出超时，未强制终止。' }
        Start-Sleep -Milliseconds 250
    }
}
function Assert-NetGuardianUpdateNotCancelled($Request) {
    $marker = Join-Path $Request.JobDirectory 'cancel.request'
    Assert-NetGuardianUpdateSafePath $marker
    if (Test-Path -LiteralPath $marker) { throw '更新请求已取消，未开始安装。' }
}
function Get-NetGuardianUpdateProcesses([string]$Directory) {
    @(Get-Process -Name NetGuardian, NetGuardian.Guardian -ErrorAction SilentlyContinue | Where-Object {
        if (-not $_.Path) { throw '无法核对界面或后台进程路径。' }
        $_.Path -ieq (Join-Path $Directory 'NetGuardian.exe') -or $_.Path -ieq (Join-Path $Directory 'guardian\NetGuardian.Guardian.exe')
    })
}
function Get-NetGuardianUpdateTasks {
    @(Get-ScheduledTask -ErrorAction Stop | Where-Object { $_.TaskPath -eq '\' -and $_.TaskName -in @('NetGuardian', 'NetGuardian-Watchdog') })
}
function Assert-NetGuardianUpdateTaskOwnership($Tasks, [string]$Directory) {
    foreach ($task in @($Tasks)) {
        $actions = @($task.Actions)
        $owned = $false
        if ($actions.Count -eq 1) {
            if ($task.TaskName -eq 'NetGuardian') {
                $owned = $actions[0].Execute -ieq (Join-Path $Directory 'guardian\NetGuardian.Guardian.exe')
            } elseif ($task.TaskName -eq 'NetGuardian-Watchdog') {
                $shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
                $expected = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -TaskName "NetGuardian" -GuardianPath "{1}"' -f (Join-Path $Directory 'scripts\watch-netguardian.ps1'), (Join-Path $Directory 'guardian\NetGuardian.Guardian.exe')
                $owned = $actions[0].Execute -in @($shell, 'powershell.exe') -and $actions[0].Arguments -ieq $expected
            }
        }
        if (-not $owned) { throw '已有外来或无法确认归属的 NetGuardian 同名任务，已拒绝更新。' }
    }
}
function Export-NetGuardianUpdateTask($Task) { Export-ScheduledTask -TaskName $Task.TaskName -TaskPath '\' }
function Get-NetGuardianUpdateTaskDefinition([string]$Xml) {
    $document = New-Object Xml.XmlDocument
    $document.PreserveWhitespace = $false
    $document.LoadXml($Xml)
    # 调度服务可以重写注册日期和 URI；其余任务主体、动作、触发器与设置必须一致。
    foreach ($node in @($document.SelectNodes("/*[local-name()='Task']/*[local-name()='RegistrationInfo']/*[local-name()='Date' or local-name()='URI']"))) { $null = $node.ParentNode.RemoveChild($node) }
    return $document.OuterXml
}
function Restore-NetGuardianUpdateTasks($Tasks, [string]$Directory) {
    $current = @(Get-NetGuardianUpdateTasks)
    Assert-NetGuardianUpdateTaskOwnership $current $Directory
    $kept = @()
    foreach ($task in $current) {
        $saved = @($Tasks | Where-Object { $_.TaskName -eq $task.TaskName })
        if ($saved.Count -eq 1 -and [bool]$task.Settings.Enabled -eq $saved[0].Enabled -and (Get-NetGuardianUpdateTaskDefinition (Export-NetGuardianUpdateTask $task)) -eq (Get-NetGuardianUpdateTaskDefinition $saved[0].Xml)) {
            if ($task.State -eq 'Running' -and -not $saved[0].Running) { throw '原本停止的任务已在运行，无法保证恢复状态。' }
            $kept += $task.TaskName
            continue
        }
        if ($task.State -eq 'Running') { throw '任务仍在运行，拒绝覆盖任务定义。' }
        Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath '\' -Confirm:$false | Out-Null
    }
    foreach ($task in @($Tasks)) {
        if ($task.TaskName -notin $kept) {
            Register-ScheduledTask -TaskName $task.TaskName -TaskPath '\' -Xml $task.Xml -Force | Out-Null
            if ($task.Enabled) { Enable-ScheduledTask -TaskName $task.TaskName -TaskPath '\' | Out-Null }
            else { Disable-ScheduledTask -TaskName $task.TaskName -TaskPath '\' | Out-Null }
        }
        $wasRunning = @($current | Where-Object { $_.TaskName -eq $task.TaskName -and $_.State -eq 'Running' }).Count -gt 0
        if ($task.Running -and -not $wasRunning) { Start-ScheduledTask -TaskName $task.TaskName -TaskPath '\' }
    }
}
function Test-NetGuardianUpdateTasks($Tasks) {
    $deadline = [datetime]::UtcNow.AddSeconds(10)
    do {
        $current = @(Get-NetGuardianUpdateTasks)
        $valid = $current.Count -eq @($Tasks).Count
        foreach ($saved in @($Tasks)) {
            $task = @($current | Where-Object { $_.TaskName -eq $saved.TaskName })
            if ($task.Count -ne 1 -or [bool]$task[0].Settings.Enabled -ne $saved.Enabled -or ($task[0].State -eq 'Running') -ne $saved.Running) { $valid = $false; continue }
            if ((Get-NetGuardianUpdateTaskDefinition (Export-NetGuardianUpdateTask $task[0])) -ne (Get-NetGuardianUpdateTaskDefinition $saved.Xml)) { $valid = $false }
        }
        if ($valid) { return $true }
        Start-Sleep -Milliseconds 250
    } while ([datetime]::UtcNow -lt $deadline)
    return $false
}
function Get-NetGuardianUpdateFreeBytes([string]$Path) {
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Path)))
    $drive.AvailableFreeSpace
}
function Get-NetGuardianUpdateFiles([string]$Directory) {
    Assert-NetGuardianUpdateSafePath $Directory
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        Assert-NetGuardianUpdateSafePath $item.FullName
        if ($item.PSIsContainer) {
            if ($item.Name -notin @('logs', 'config', 'configuration')) { Get-NetGuardianUpdateFiles $item.FullName }
        } elseif ($item.Name -notin @('settings.json', 'config.json', 'stop.request')) { $item }
    }
}
function Backup-NetGuardianUpdateFiles([string]$Directory, [string]$BackupDirectory) {
    New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
    $manifest = @()
    foreach ($file in @(Get-NetGuardianUpdateFiles $Directory)) {
        $relative = $file.FullName.Substring($Directory.TrimEnd('\').Length + 1)
        $target = Join-Path $BackupDirectory $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash) { throw "备份哈希不一致：$relative" }
        $manifest += [pscustomobject]@{ Path = $relative; Sha256 = $hash; Length = $file.Length }
    }
    return $manifest
}
function Restore-NetGuardianUpdateFiles([string]$Directory, [string]$BackupDirectory, $Manifest) {
    # 删除范围仅为已核对安装目录内的新增程序文件，配置与日志不参与事务。
    foreach ($file in @(Get-NetGuardianUpdateFiles $Directory)) {
        $relative = $file.FullName.Substring($Directory.TrimEnd('\').Length + 1)
        if (-not @($Manifest | Where-Object { $_.Path -ieq $relative }).Count) {
            $null = Assert-NetGuardianUpdateChildPath $file.FullName $Directory
            Assert-NetGuardianUpdateSafePath $file.FullName
            Remove-Item -LiteralPath $file.FullName -Force
        }
    }
    foreach ($entry in @($Manifest)) {
        $source = Assert-NetGuardianUpdateChildPath (Join-Path $BackupDirectory $entry.Path) $BackupDirectory
        $target = Assert-NetGuardianUpdateChildPath (Join-Path $Directory $entry.Path) $Directory
        Assert-NetGuardianUpdateSafePath $source; Assert-NetGuardianUpdateSafePath $target
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Sha256) { throw '恢复备份已被修改。' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Force
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.Sha256) { throw "恢复后哈希不一致：$($entry.Path)" }
    }
}

function Export-NetGuardianUpdateUninstall([string]$Destination) {
    $snapshots = @()
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        $key = $base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{5DE3E29B-A247-4A86-B139-B522DAB21872}_is1')
        try {
            $values = @()
            if ($null -ne $key) {
                if ($key.SubKeyCount -ne 0) { throw '卸载项包含不支持恢复的子项，拒绝开始更新。' }
                foreach ($name in $key.GetValueNames()) { $values += [pscustomobject]@{ Name = $name; Kind = [int]$key.GetValueKind($name); Value = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } }
            }
            $snapshots += [pscustomobject]@{ View = [int]$view; Exists = ($null -ne $key); Values = $values }
        } finally { if ($key) { $key.Dispose() }; $base.Dispose() }
    }
    $snapshots | Export-Clixml -LiteralPath $Destination -Depth 10
}
function Restore-NetGuardianUpdateUninstall([string]$Source) {
    foreach ($snapshot in @(Import-Clixml -LiteralPath $Source)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]$snapshot.View)
        $path = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{5DE3E29B-A247-4A86-B139-B522DAB21872}_is1'
        try {
            $base.DeleteSubKeyTree($path, $false)
            if ($snapshot.Exists) {
                $key = $base.CreateSubKey($path)
                try { foreach ($value in @($snapshot.Values)) { $key.SetValue($value.Name, $value.Value, [Microsoft.Win32.RegistryValueKind]$value.Kind) } } finally { $key.Dispose() }
            }
        } finally { $base.Dispose() }
    }
}
function Test-NetGuardianUpdateUninstall([string]$Source) {
    $temporary = $Source + '.verify'
    try {
        Export-NetGuardianUpdateUninstall $temporary
        return [Linq.Enumerable]::SequenceEqual([byte[]][IO.File]::ReadAllBytes($Source), [byte[]][IO.File]::ReadAllBytes($temporary))
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
function Stop-NetGuardianUpdateBackground {
    param([string]$Directory, [string]$StopScriptPath, [Parameter(Mandatory)][string]$ExpectedSha256, [string]$ProgramFilesDirectory = $env:ProgramFiles, [string]$ScriptProtectionRoot = $env:ProgramFiles)
    $null = Assert-NetGuardianUpdateChildPath $Directory $ProgramFilesDirectory
    Assert-NetGuardianUpdateProtectionChain $Directory $ProgramFilesDirectory
    Assert-NetGuardianUpdateProtectionChain $StopScriptPath $ScriptProtectionRoot
    if ((Get-FileHash -LiteralPath $StopScriptPath -Algorithm SHA256).Hash -ine $ExpectedSha256) { throw '安全停止脚本哈希已变化，未执行脚本或修改任务。' }
    Assert-NetGuardianUpdateTaskOwnership @(Get-NetGuardianUpdateTasks) $Directory
    & $StopScriptPath | Out-Null
    if (@(Get-NetGuardianUpdateProcesses $Directory).Count -or @(Get-NetGuardianUpdateTasks).Count) { throw '后台停止后仍有所属进程或任务，拒绝安装。' }
}
function Invoke-NetGuardianUpdateInstaller($Request) {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="' + $Request.InstallDirectory + '" /LOG="' + (Join-Path $Request.JobDirectory 'installer.log') + '"'
    $process = Start-Process -FilePath $Request.InstallerPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    return $process.ExitCode
}
function Start-NetGuardianUpdateInterface([string]$Directory) {
    Start-Process -FilePath (Join-Path $Directory 'NetGuardian.exe') -WorkingDirectory $Directory | Out-Null
}

function New-NetGuardianUpdateMutex {
    return [Threading.Mutex]::new($false, 'Global\NetGuardian.Update')
}

function Save-NetGuardianUpdateResult($Result, [string]$UpdatesDirectory) {
    Write-NetGuardianUpdateJson (Join-Path $Result.JobDirectory 'result.json') $Result
    Write-NetGuardianUpdateJson (Join-Path $UpdatesDirectory 'last-result.json') $Result
    $log = Join-Path $Result.JobDirectory 'update.log'
    Assert-NetGuardianUpdateSafePath $log
    [IO.File]::AppendAllText($log, ([datetime]::UtcNow.ToString('o') + ' ' + ($Result | ConvertTo-Json -Compress) + [Environment]::NewLine), [Text.UTF8Encoding]::new($true))
}

function Invoke-NetGuardianUpdateTransaction {
    param([Parameter(Mandatory)][string]$RequestFile, [string]$ProgramFilesDirectory = $env:ProgramFiles, [string]$UpdatesDirectory = (Join-Path $env:ProgramData 'NetGuardian\updates'))
    $mutex = New-NetGuardianUpdateMutex
    $acquired = $false
    $restartContext = [pscustomobject]@{ Directory = '' }
    try {
        try { $acquired = $mutex.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw '另一个更新作业正在执行，请等待完成后重试。' }
        $result = Invoke-NetGuardianUpdateTransactionCore -RequestFile $RequestFile -ProgramFilesDirectory $ProgramFilesDirectory -UpdatesDirectory $UpdatesDirectory -RestartContext $restartContext
    } finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
    # 应用启动也检查同一维护锁；必须释放后再重开，启动失败不回滚已核验安装。
    if ($restartContext.Directory) {
        $installed = $result.Success
        try {
            Start-NetGuardianUpdateInterface $restartContext.Directory
            if ($installed) { $result.Stage = 'Completed'; $result.Message = '更新成功并已重新打开界面。' }
            else { $result.Message += '；已恢复并重新打开原界面。' }
        } catch {
            $result.Success = $false; $result.Stage = 'RestartFailed'
            $detail = if ($installed) { '新版本已安装且核验完成，但界面启动失败：' } else { '原版本已恢复且核验完成，但界面启动失败：' }
            $result.Message += '；' + $detail + $_.Exception.Message
        }
        Save-NetGuardianUpdateResult $result $UpdatesDirectory
    }
    return $result
}

function Invoke-NetGuardianUpdateTransactionCore {
    param([Parameter(Mandatory)][string]$RequestFile, [string]$ProgramFilesDirectory = $env:ProgramFiles, [string]$UpdatesDirectory = (Join-Path $env:ProgramData 'NetGuardian\updates'), [Parameter(Mandatory)]$RestartContext)
    # 在信任请求之前不写结果，避免把特权输出写入任意用户指定路径。
    $requestPath = Assert-NetGuardianUpdateChildPath $RequestFile (Join-Path $UpdatesDirectory 'jobs')
    Assert-NetGuardianUpdateSafePath $requestPath
    if ([IO.Path]::GetFileName($requestPath) -ine 'request.json') { throw '请求文件必须是 jobs 目录中的 request.json。' }
    $job = Split-Path -Parent $requestPath
    $jobId = [guid]::Empty
    if ((Split-Path -Parent $job) -ine (Join-Path $UpdatesDirectory 'jobs') -or -not [guid]::TryParse((Split-Path -Leaf $job), [ref]$jobId)) { throw '请求必须位于 updates\jobs\GUID 目录。' }
    Assert-NetGuardianUpdateProtectionChain $requestPath (Split-Path -Parent $UpdatesDirectory)
    if ((Get-Item -LiteralPath $requestPath).Length -gt 65536) { throw '更新请求超出长度限制。' }
    $request = Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
    if ([string]$request.JobDirectory -ine $job) { throw '请求 JobDirectory 必须与受保护 jobs 请求目录一致。' }
    $result = [ordered]@{ Success = $false; Stage = 'Preparing'; Message = ''; RollbackSucceeded = $false; Version = [string]$request.Version; JobDirectory = $job }
    $installationStarted = $false; $tasksChanged = $false; $parentExited = $false; $tasks = @(); $manifest = @()
    $backup = Join-Path $job 'backup'; $registry = Join-Path $job 'uninstall.clixml'
    try {
        Assert-NetGuardianUpdateNotCancelled $request
        $request.InstallDirectory = Assert-NetGuardianUpdateChildPath ([string]$request.InstallDirectory) $ProgramFilesDirectory
        $request.InstallerPath = Assert-NetGuardianUpdateChildPath ([string]$request.InstallerPath) $UpdatesDirectory
        Assert-NetGuardianUpdateSafePath $request.InstallDirectory; Assert-NetGuardianUpdateSafePath $request.InstallerPath
        Assert-NetGuardianUpdateProtectionChain $request.InstallDirectory $ProgramFilesDirectory
        Assert-NetGuardianUpdateProtectionChain $request.InstallerPath (Split-Path -Parent $UpdatesDirectory)
        $stopScript = Join-Path $request.InstallDirectory 'scripts\unregister-netguardian-task.ps1'
        Assert-NetGuardianUpdateProtectionChain $stopScript $ProgramFilesDirectory
        if (-not (Test-Path -LiteralPath $stopScript -PathType Leaf)) { throw '缺少安装目录所属的安全停止脚本。' }
        $stopScriptSha256 = (Get-FileHash -LiteralPath $stopScript -Algorithm SHA256).Hash
        if (-not (Test-Path -LiteralPath $request.InstallDirectory -PathType Container)) { throw '安装目录不存在。' }
        $utcStart = if ($request.ParentStartedAt -is [datetime]) { $request.ParentStartedAt.Kind -eq [DateTimeKind]::Utc } else { $request.ParentStartedAt -match '(Z|\+00:00)$' }
        if ($request.Version -notmatch '^\d+\.\d+\.\d+$' -or $request.Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [int]$request.ParentProcessId -le 0 -or -not $utcStart) { throw '更新版本、SHA256 或界面身份格式无效。' }
        if ((Get-FileHash -LiteralPath $request.InstallerPath -Algorithm SHA256).Hash -ine $request.Sha256 -or (Get-NetGuardianUpdateVersion $request.InstallerPath) -ne $request.Version) { throw '安装包 SHA256 或 ProductVersion 不匹配。' }
        Assert-NetGuardianUpdateParent $request (Get-NetGuardianUpdateParent $request.ParentProcessId)
        $currentTasks = @(Get-NetGuardianUpdateTasks)
        Assert-NetGuardianUpdateTaskOwnership $currentTasks $request.InstallDirectory
        $guardians = @(Get-NetGuardianUpdateProcesses $request.InstallDirectory | Where-Object { [IO.Path]::GetFileName($_.Path) -ieq 'NetGuardian.Guardian.exe' })
        if ($guardians.Count -gt 1 -or ($guardians.Count -and -not @($currentTasks | Where-Object { $_.TaskName -eq 'NetGuardian' -and $_.State -eq 'Running' }).Count)) { throw '守护未由本安装任务托管，无法可靠恢复原命令，请先正常停止后台。' }
        $files = @(Get-NetGuardianUpdateFiles $request.InstallDirectory)
        $bytes = [long](($files | Measure-Object Length -Sum).Sum) + [long](Get-Item -LiteralPath $request.InstallerPath).Length
        foreach ($directory in @($job, $request.InstallDirectory)) { if ((Get-NetGuardianUpdateFreeBytes $directory) -lt ($bytes * 3 + 268435456)) { throw '磁盘可用空间不足，无法保证备份和恢复。' } }
        # 原停止脚本没有相对脚本依赖；保留已核验快照，供安装失败后安全停止使用。
        $stopSnapshot = Join-Path $job 'trusted-unregister-netguardian-task.ps1'
        Copy-Item -LiteralPath $stopScript -Destination $stopSnapshot
        Assert-NetGuardianUpdateProtectionChain $stopSnapshot (Split-Path -Parent $UpdatesDirectory)
        if ((Get-FileHash -LiteralPath $stopSnapshot -Algorithm SHA256).Hash -ine $stopScriptSha256) { throw '安全停止脚本快照哈希不一致。' }
        foreach ($task in $currentTasks) { $tasks += [pscustomobject]@{ TaskName = $task.TaskName; Xml = (Export-NetGuardianUpdateTask $task); Enabled = [bool]$task.Settings.Enabled; Running = ($task.State -eq 'Running') } }
        Write-NetGuardianUpdateJson (Join-Path $job 'tasks.json') @($tasks)
        Export-NetGuardianUpdateUninstall $registry
        $manifest = @(Backup-NetGuardianUpdateFiles $request.InstallDirectory $backup)
        Write-NetGuardianUpdateJson (Join-Path $job 'manifest.json') @($manifest)
        Assert-NetGuardianUpdateNotCancelled $request
        Write-NetGuardianUpdateJson (Join-Path $job 'ready.json') @{ Ready = $true; Version = $request.Version }
        $result.Stage = 'WaitingForExit'
        Wait-NetGuardianUpdateParent $request
        $parentExited = $true
        Assert-NetGuardianUpdateNotCancelled $request
        $result.Stage = 'StoppingBackground'; $tasksChanged = $true
        Stop-NetGuardianUpdateBackground -Directory $request.InstallDirectory -StopScriptPath $stopScript -ExpectedSha256 $stopScriptSha256 -ProgramFilesDirectory $ProgramFilesDirectory -ScriptProtectionRoot $ProgramFilesDirectory
        # 等待界面退出后再次核对安装包与目录，避免准备期间发生文件替换。
        Assert-NetGuardianUpdateSafePath $request.InstallDirectory; Assert-NetGuardianUpdateSafePath $request.InstallerPath
        if ((Get-FileHash -LiteralPath $request.InstallerPath -Algorithm SHA256).Hash -ine $request.Sha256) { throw '准备期间安装包发生变化。' }
        $result.Stage = 'Installing'; $installationStarted = $true
        $exitCode = Invoke-NetGuardianUpdateInstaller $request
        if ($exitCode -ne 0) { throw "安装器退出码：$exitCode" }
        if ((Get-NetGuardianUpdateVersion (Join-Path $request.InstallDirectory 'NetGuardian.dll')) -ne $request.Version) { throw '安装后的 NetGuardian.dll ProductVersion 不匹配。' }
        $result.Stage = 'RestoringTasks'
        Restore-NetGuardianUpdateTasks $tasks $request.InstallDirectory
        if (-not (Test-NetGuardianUpdateTasks $tasks)) { throw '任务定义或启用、运行状态恢复核验失败。' }
        $tasksChanged = $false
        $RestartContext.Directory = $request.InstallDirectory
        $result.Success = $true; $result.Stage = 'Installed'; $result.Message = '安装已完成并通过核验，等待重新打开界面。'
    } catch {
        $result.Message = $_.Exception.Message
        if ($installationStarted -or $tasksChanged) {
            try {
                if ($installationStarted) {
                    # 恢复前再次安全停止，避免覆盖仍被后台使用的程序。
                    Stop-NetGuardianUpdateBackground -Directory $request.InstallDirectory -StopScriptPath $stopSnapshot -ExpectedSha256 $stopScriptSha256 -ProgramFilesDirectory $ProgramFilesDirectory -ScriptProtectionRoot (Split-Path -Parent $UpdatesDirectory)
                    Restore-NetGuardianUpdateFiles $request.InstallDirectory $backup $manifest
                    Restore-NetGuardianUpdateUninstall $registry
                    if (-not (Test-NetGuardianUpdateUninstall $registry)) { throw '卸载项恢复核验失败。' }
                }
                Restore-NetGuardianUpdateTasks $tasks $request.InstallDirectory
                if (-not (Test-NetGuardianUpdateTasks $tasks)) { throw '任务恢复核验失败。' }
                $result.RollbackSucceeded = $true
                if ($parentExited) { $RestartContext.Directory = $request.InstallDirectory }
            } catch { $result.Message += '；恢复失败，未启动程序：' + $_.Exception.Message; $result.RollbackSucceeded = $false }
        }
    }
    Save-NetGuardianUpdateResult $result $UpdatesDirectory
    return [pscustomobject]$result
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $result = Invoke-NetGuardianUpdateTransaction -RequestFile $RequestFile
        if ($result.Success) { exit 0 } else { Write-Error $result.Message; exit 1 }
    } catch { Write-Error $_; exit 1 }
}
