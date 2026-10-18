using System.Diagnostics;
using System.Text.Json;
using System.Security.Principal;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.Recovery;
using NetGuardian.Core.Runtime;
using NetGuardian.Core.Settings;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;

// 验证模式不探测或修改网络，供发布和安装前检查。
var settingsIndex = Array.IndexOf(args, "--settings");
var settingsPath = settingsIndex >= 0 && settingsIndex + 1 < args.Length
    ? Path.GetFullPath(args[settingsIndex + 1]) : GuardianPaths.MachineSettingsPath;
var directory = Path.GetDirectoryName(settingsPath)!;
var store = new SettingsStore(settingsPath);
if (!store.TryLoad(out var initialSettings))
{
    Console.Error.WriteLine($"守护配置缺失或损坏：{settingsPath}");
    return 78;
}
if (args.Contains("--validate"))
{
    var eligible = initialSettings.Profiles.Count(profile => profile.Enabled && profile.AutoConnect);
    if (eligible < 2) { Console.Error.WriteLine("无人值守切换至少需要两个启用且允许自动连接的 Wi-Fi 配置；请另用 --diagnose 检查 Windows 中的实际可用性。"); return 78; }
    Console.WriteLine(JsonSerializer.Serialize(new { 配置 = settingsPath, 网络数量 = initialSettings.Profiles.Count, 已配置候选数量 = eligible, 检测间隔秒 = initialSettings.Policy.CheckIntervalSeconds }));
    return 0;
}

if (args.Contains("--diagnose"))
{
    using var diagnosticDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var diagnostic = await GuardianDiagnostics.RunAsync(initialSettings, new WifiClient(), new ConnectivityTesterService(), diagnosticDeadline.Token);
    var result = JsonSerializer.Serialize(new
    {
        Timestamp = DateTimeOffset.UtcNow,
        ProcessId = Environment.ProcessId,
        Account = WindowsIdentity.GetCurrent().Name,
        Sid = WindowsIdentity.GetCurrent().User?.Value,
        Diagnostic = diagnostic
    }, new JsonSerializerOptions { WriteIndented = true });
    var outputIndex = Array.IndexOf(args, "--output");
    if (outputIndex >= 0 && outputIndex + 1 < args.Length)
    {
        var outputPath = Path.GetFullPath(args[outputIndex + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath + ".tmp", result);
        File.Move(outputPath + ".tmp", outputPath, overwrite: true);
    }
    Console.WriteLine(result);
    return diagnostic.ReadyForUnattendedRecovery ? 0 : 2;
}

using var lease = MonitoringLease.TryAcquire(GuardianPaths.MachineDirectory);
if (lease is null) { Console.Error.WriteLine("已有监控控制者或无权获取机器级控制锁。"); return 73; }
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var log = new FileLogSink(Path.Combine(directory, "logs"), initialSettings.LogRetentionDays);
var journal = new EventJournal(Path.Combine(directory, "logs"));
var commands = new ProcessCommandRunner();
var settings = initialSettings;
var budget = new RecoveryBudget(directory);
var deviceFailures = new DeviceFailureTracker();
var state = "Starting";
ConnectivityReport? lastReport = null;
var progressTicks = DateTimeOffset.UtcNow.UtcTicks;
var processStartedAt = new DateTimeOffset(Process.GetCurrentProcess().StartTime);
var wifi = new WifiClient();
var profileInventory = new WifiProfileInventory(false, []);
var nextInventoryCheck = DateTimeOffset.MinValue;
using var engine = new MonitoringEngine(wifi, new ConnectivityTesterService(), commands,
    () => Volatile.Read(ref settings).Policy,
    () => Volatile.Read(ref settings).Profiles,
    highRiskService: new BudgetedRecoveryService(new HighRiskRecoveryService(commands), budget,
        () => TimeSpan.FromMinutes(Volatile.Read(ref settings).Policy.HighRiskCooldownMinutes)));
engine.CheckCompleted += (_, e) =>
{
    state = e.Report.State.ToString();
    Volatile.Write(ref lastReport, e.Report);
    Interlocked.Exchange(ref progressTicks, DateTimeOffset.UtcNow.UtcTicks);
    deviceFailures.Record(e.Report.State, DateTimeOffset.UtcNow);
    journal.RecordCheck(e.Report.State, e.Report.Ok);
    log.Write($"检测：{e.Report.State}，{e.Report.Summary}");
};
engine.EngineEvent += (_, e) =>
{
    log.Write($"{e.Kind}：{string.Join(" | ", e.Args.Select(value => value?.ToString()))}");
    if (e.Kind == EngineEventKind.RecoveryStarted) journal.RecordRecoveryStarted(engine.FailureStreak);
    if (e.Kind == EngineEventKind.RecoveryWifiConnected)
    {
        journal.RecordRecoveryResult(true, e.Args[0]?.ToString());
        deviceFailures.Record(NetworkState.Healthy, DateTimeOffset.UtcNow);
        state = NetworkState.Healthy.ToString();
        Volatile.Write(ref lastReport, new ConnectivityReport { State = NetworkState.Healthy, CurrentSsid = e.Args[0]?.ToString(), Summary = "恢复后的互联网连接已验证。" });
    }
    if (e.Kind == EngineEventKind.RecoveryFailed) journal.RecordRecoveryResult(false, null);
    if (e.Kind == EngineEventKind.HighRiskActionCompleted)
    {
        var commandOk = e.Args[1] is true;
        var networkOk = e.Args[2] is true;
        if (Enum.TryParse<NetworkState>(e.Args[4]?.ToString(), out var finalState))
        {
            deviceFailures.Record(finalState, DateTimeOffset.UtcNow);
            state = finalState.ToString();
            Volatile.Write(ref lastReport, new ConnectivityReport { State = finalState, Summary = "设备恢复动作后的检测结果。" });
        }
        journal.RecordHighRisk(e.Args[0]?.ToString() ?? "", commandOk, networkOk);
        HighRiskAlertWriter.Write(new HighRiskAlert(DateTimeOffset.Now, e.Args[0]?.ToString() ?? "", commandOk,
            networkOk, e.Args[3]?.ToString(), e.Args[4]?.ToString()), Path.Combine(directory, "alerts"));
    }
};
engine.StateChanged += (_, _) => Interlocked.Exchange(ref progressTicks, DateTimeOffset.UtcNow.UtcTicks);

try
{
    log.Write($"后台守护启动，进程 {Environment.ProcessId}，配置 {settingsPath}");
    profileInventory = await wifi.GetProfileInventoryAsync(cancellation.Token);
    WifiProfileAvailability.Apply(settings.Profiles, profileInventory);
    nextInventoryCheck = DateTimeOffset.UtcNow.AddMinutes(1);
    engine.Start();
    // 不阻塞心跳；引擎负责单轮超时，监督负责最终发现假死。
    var firstCheck = engine.CheckNowAsync(cancellation.Token);
    while (!cancellation.IsCancellationRequested)
    {
        var stopRequest = Path.Combine(directory, "stop.request");
        if (File.Exists(stopRequest))
        {
            File.Delete(stopRequest);
            cancellation.Cancel();
            break;
        }
        if (store.TryLoad(out var loaded))
        {
            if (DateTimeOffset.UtcNow >= nextInventoryCheck)
            {
                var inventory = await wifi.GetProfileInventoryAsync(cancellation.Token);
                if (inventory.Success) profileInventory = inventory;
                else log.Write($"Windows 无线配置暂不可读取，保留最近有效候选：{inventory.Error}");
                nextInventoryCheck = DateTimeOffset.UtcNow.AddMinutes(1);
            }
            WifiProfileAvailability.Apply(loaded.Profiles, profileInventory);
            var previousInterval = settings.Policy.CheckIntervalSeconds;
            Volatile.Write(ref settings, loaded);
            if (previousInterval != loaded.Policy.CheckIntervalSeconds) engine.UpdateSchedule();
        }
        if (engine.IsChecking && DateTimeOffset.UtcNow.UtcTicks - Interlocked.Read(ref progressTicks) > TimeSpan.FromMinutes(10).Ticks)
            throw new TimeoutException("监控引擎十分钟没有进展，退出供计划任务重新启动。");
        GuardianStatusStore.Write(directory, new GuardianStatus(Environment.ProcessId, processStartedAt,
            DateTimeOffset.UtcNow, state, engine.IsChecking, Volatile.Read(ref lastReport), engine.FailureStreak, engine.NextCheckAt));
        if (firstCheck.IsCompleted) await firstCheck;
        if (!engine.IsChecking && ComputerRestartPlanner.ShouldRestart(settings.EnableComputerRestart,
            Volatile.Read(ref lastReport)?.State ?? NetworkState.Healthy,
            deviceFailures.FailedSince, DateTimeOffset.UtcNow, settings.DeviceFailureMinutesBeforeRestart))
        {
            // 重启前重新核对设备层，禁止依据恢复前的陈旧报告重启电脑。
            var fresh = await wifi.GetAdapterDiagnosticsAsync(cancellation.Token);
            if (fresh.ProblemCode is > 0 && !engine.IsChecking && deviceFailures.FailedSince is not null
                && budget.TryReserve("ComputerRestart", DateTimeOffset.UtcNow, TimeSpan.FromHours(24)))
            {
                log.Write("设备故障持续超过门槛，已预留24小时重启预算；将在60秒后重启电脑。");
                await commands.RunAsync("shutdown.exe", ["/r", "/t", "60", "/c", "NetGuardian：持续无线设备故障，执行已授权的恢复重启"], cancellation.Token);
            }
            else if (fresh.ProblemCode is null or 0) deviceFailures.Record(NetworkState.Healthy, DateTimeOffset.UtcNow);
        }
        await Task.Delay(TimeSpan.FromSeconds(5), cancellation.Token);
    }
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
catch (Exception ex)
{
    log.Write($"后台异常退出：{ex}");
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    engine.Stop();
    // 等待已经发出的网络恢复完成收尾，再释放跨进程控制锁。
    while (engine.IsChecking) await Task.Delay(100);
    try { GuardianStatusStore.Write(directory, new GuardianStatus(Environment.ProcessId, processStartedAt, DateTimeOffset.UtcNow, "Stopped", false)); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Write($"无法记录停止状态：{ex.Message}"); }
}
return 0;
