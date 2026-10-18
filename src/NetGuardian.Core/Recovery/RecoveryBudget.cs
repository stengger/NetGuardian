using System.Text.Json;
using NetGuardian.Core.SystemCommands;

namespace NetGuardian.Core.Recovery;

/// <summary>动作执行前持久化预算，进程重启不能绕过冷却；记录损坏时拒绝破坏性恢复。</summary>
public sealed class RecoveryBudget(string directory)
{
    private readonly object _sync = new();

    public bool TryReserve(string action, DateTimeOffset now, TimeSpan cooldown)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "recovery-budget.json");
                var history = File.Exists(path)
                    ? JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(path))
                    : new Dictionary<string, DateTimeOffset>();
                if (history is null) return false;
                if (history.TryGetValue(action, out var previous) && now - previous < cooldown) return false;
                history[action] = now;
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(history));
                File.Move(path + ".tmp", path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
        }
    }
}

public sealed class BudgetedRecoveryService(IHighRiskRecoveryService inner, RecoveryBudget budget, Func<TimeSpan> cooldown) : IHighRiskRecoveryService
{
    public Task<SystemCommandResult> RestartWlanServiceAsync(CancellationToken cancellationToken = default) =>
        RunAsync("WlanService", inner.RestartWlanServiceAsync, cancellationToken);

    public Task<SystemCommandResult> RestartAdapterAsync(string adapterName, CancellationToken cancellationToken = default) =>
        RunAsync("Adapter", token => inner.RestartAdapterAsync(adapterName, token), cancellationToken);

    private Task<SystemCommandResult> RunAsync(string action, Func<CancellationToken, Task<SystemCommandResult>> run, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return budget.TryReserve(action, DateTimeOffset.UtcNow, cooldown())
            ? run(token)
            : Task.FromResult(new SystemCommandResult(75, "动作仍在持久化冷却期，或无法安全保存恢复记录。"));
    }
}

public static class ComputerRestartPlanner
{
    public static bool ShouldRestart(bool enabled, NetGuardian.Core.Models.NetworkState state,
        DateTimeOffset? failedSince, DateTimeOffset now, int minimumMinutes) =>
        enabled && state == NetGuardian.Core.Models.NetworkState.AdapterFault && failedSince is { } since
        && now - since >= TimeSpan.FromMinutes(Math.Max(10, minimumMinutes));
}

public sealed class DeviceFailureTracker
{
    private long _sinceTicks;
    public DateTimeOffset? FailedSince
    {
        get
        {
            var ticks = Interlocked.Read(ref _sinceTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void Record(NetGuardian.Core.Models.NetworkState state, DateTimeOffset now)
    {
        if (state == NetGuardian.Core.Models.NetworkState.AdapterFault)
            Interlocked.CompareExchange(ref _sinceTicks, now.UtcTicks, 0);
        else Interlocked.Exchange(ref _sinceTicks, 0);
    }
}
