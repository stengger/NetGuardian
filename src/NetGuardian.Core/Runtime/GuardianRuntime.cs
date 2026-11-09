using System.Text.Json;

namespace NetGuardian.Core.Runtime;

public static class GuardianPaths
{
    public static string MachineDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetGuardian");
    public static string MachineSettingsPath => Path.Combine(MachineDirectory, "settings.json");
    public static string UserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetGuardian");
    public static string DefaultDirectory => File.Exists(MachineSettingsPath) ? MachineDirectory : UserDirectory;
}

/// <summary>文件句柄跨会话独占，进程退出后由操作系统释放，不依赖线程亲和性。</summary>
public sealed class MonitoringLease : IDisposable
{
    private readonly FileStream _handle;
    private MonitoringLease(FileStream handle) => _handle = handle;

    public static MonitoringLease? TryAcquire(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return new MonitoringLease(new FileStream(Path.Combine(directory, "monitor.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Dispose() => _handle.Dispose();
}

public sealed record GuardianStatus(int ProcessId, DateTimeOffset ProcessStartedAt, DateTimeOffset HeartbeatAt, string State,
    bool IsChecking, NetGuardian.Core.Models.ConnectivityReport? Report = null, int FailureStreak = 0, DateTimeOffset? NextCheckAt = null,
    int ControlProtocolVersion = 0);

public static class GuardianStatusStore
{
    public static void Write(string directory, GuardianStatus status)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "guardian-status.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status));
        File.Move(temp, path, overwrite: true);
    }

    public static GuardianStatus? Read(string directory)
    {
        try { return JsonSerializer.Deserialize<GuardianStatus>(File.ReadAllText(Path.Combine(directory, "guardian-status.json"))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
