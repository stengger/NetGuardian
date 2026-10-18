using System.Text;
using System.Text.Json;
using NetGuardian.Core.SystemCommands;

namespace NetGuardian.Core.Recovery;

/// <summary>
/// Executes the two high-risk recovery actions. Only ever invoked by the engine
/// after <see cref="Monitoring.RecoveryPlanner.EvaluateHighRisk"/> returned Proceed.
/// </summary>
public interface IHighRiskRecoveryService
{
    Task<SystemCommandResult> RestartWlanServiceAsync(CancellationToken cancellationToken = default);
    Task<SystemCommandResult> RestartAdapterAsync(string adapterName, CancellationToken cancellationToken = default);
}

public sealed class HighRiskRecoveryService : IHighRiskRecoveryService
{
    private readonly ISystemCommandRunner _commands;

    public HighRiskRecoveryService(ISystemCommandRunner commands)
    {
        _commands = commands;
    }

    public async Task<SystemCommandResult> RestartWlanServiceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SystemCommandResult stop;
        SystemCommandResult start;
        try { stop = await _commands.RunAsync("net.exe", ["stop", "WlanSvc"], cancellationToken); }
        finally
        {
            // 停止已发出后，无论取消还是异常，都使用独立期限恢复服务。
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            start = await _commands.RunAsync("net.exe", ["start", "WlanSvc"], cleanup.Token);
        }
        var output = string.Join(Environment.NewLine, new[] { stop.Output, start.Output }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new SystemCommandResult(start.ExitCode, output);
    }

    public async Task<SystemCommandResult> RestartAdapterAsync(string adapterName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (adapterName.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) || adapterName.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
            return await _commands.RunAsync("pnputil.exe", ["/restart-device", adapterName], cancellationToken);
        SystemCommandResult disable;
        SystemCommandResult enable;
        try
        {
            disable = await _commands.RunAsync("netsh.exe",
                ["interface", "set", "interface", $"name={adapterName}", "admin=disabled"], cancellationToken);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            enable = await _commands.RunAsync("netsh.exe",
                ["interface", "set", "interface", $"name={adapterName}", "admin=enabled"], cleanup.Token);
        }
        var output = string.Join(Environment.NewLine, new[] { disable.Output, enable.Output }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new SystemCommandResult(enable.ExitCode, output);
    }
}

public sealed record HighRiskAlert(
    DateTimeOffset Timestamp,
    string Action,
    bool CommandOk,
    bool NetworkOkAfter,
    string? StateBefore,
    string? StateAfter);

/// <summary>
/// Writes the "last high-risk action" alert file that remote operators can poll:
/// %APPDATA%\NetGuardian\alerts\last-high-risk.json
/// </summary>
public static class HighRiskAlertWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultDirectory
    {
        get
        {
            return Path.Combine(NetGuardian.Core.Runtime.GuardianPaths.DefaultDirectory, "alerts");
        }
    }

    public static void Write(HighRiskAlert alert, string? directory = null)
    {
        try
        {
            var target = directory ?? DefaultDirectory;
            Directory.CreateDirectory(target);
            File.WriteAllText(
                Path.Combine(target, "last-high-risk.json"),
                JsonSerializer.Serialize(alert, JsonOptions),
                Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Alerting must never break recovery.
        }
    }
}
