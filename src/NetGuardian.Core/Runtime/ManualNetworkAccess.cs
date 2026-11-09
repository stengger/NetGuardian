using System.Text.Json;

namespace NetGuardian.Core.Runtime;

public sealed record ManualControlRequest(Guid Id, DateTimeOffset ExpiresAt);

public static class ManualControlRequestStore
{
    public const int ProtocolVersion = 1;
    private static string PathFor(string directory) => Path.Combine(directory, "manual-control.json");

    public static ManualControlRequest? Read(string directory, DateTimeOffset now)
    {
        try
        {
            var request = JsonSerializer.Deserialize<ManualControlRequest>(File.ReadAllText(PathFor(directory)));
            return request is not null && request.Id != Guid.Empty && request.ExpiresAt > now
                && request.ExpiresAt <= now.AddMinutes(1) ? request : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static void Write(string directory, ManualControlRequest request)
    {
        var path = PathFor(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(request));
        File.Move(temporary, path, true);
    }

    public static void Remove(string directory, Guid id)
    {
        try
        {
            var request = JsonSerializer.Deserialize<ManualControlRequest>(File.ReadAllText(PathFor(directory)));
            if (request?.Id == id) File.Delete(PathFor(directory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
}

public enum ManualControlFailure { Busy, UpgradeRequired, Unavailable, TimedOut }

public sealed class ManualControlException(ManualControlFailure reason) : InvalidOperationException(reason.ToString())
{
    public ManualControlFailure Reason { get; } = reason;
}

/// <summary>手动操作先取得请求锁，再等待后台交出监控锁；整个操作期间保持独占。</summary>
public sealed class ManualNetworkAccess : IDisposable
{
    private readonly string _directory;
    private readonly FileStream _requestLease;
    private readonly Guid _id = Guid.NewGuid();
    private readonly object _sync = new();
    private MonitoringLease? _networkLease;
    private Timer? _renewal;
    private bool _disposed;
    private bool _requested;

    private ManualNetworkAccess(string directory, FileStream requestLease)
        => (_directory, _requestLease) = (directory, requestLease);

    public static async Task<ManualNetworkAccess> AcquireAsync(string directory, CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        FileStream requestLease;
        try
        {
            requestLease = new FileStream(Path.Combine(directory, "manual-control.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { throw new ManualControlException(ManualControlFailure.Busy); }
        var access = new ManualNetworkAccess(directory, requestLease);
        try
        {
            access._networkLease = MonitoringLease.TryAcquire(directory);
            if (access._networkLease is not null) return access;
            var status = GuardianStatusStore.Read(directory);
            var age = status is null ? TimeSpan.MaxValue : DateTimeOffset.UtcNow - status.HeartbeatAt;
            if (status is null || status.State == "Stopped" || age < TimeSpan.Zero || age > TimeSpan.FromSeconds(30))
                throw new ManualControlException(ManualControlFailure.Unavailable);
            if (status.ControlProtocolVersion < ManualControlRequestStore.ProtocolVersion)
                throw new ManualControlException(ManualControlFailure.UpgradeRequired);
            access._requested = true;
            ManualControlRequestStore.Write(directory, new(access._id, DateTimeOffset.UtcNow.AddSeconds(20)));
            access._renewal = new Timer(_ => access.RenewRequest(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(40));
            try
            {
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    access._networkLease = MonitoringLease.TryAcquire(directory);
                    if (access._networkLease is not null) return access;
                    await Task.Delay(100, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ManualControlException(ManualControlFailure.TimedOut); }
        }
        catch { access.Dispose(); throw; }
    }

    private void RenewRequest()
    {
        lock (_sync)
        {
            if (_disposed) return;
            try { ManualControlRequestStore.Write(_directory, new(_id, DateTimeOffset.UtcNow.AddSeconds(20))); }
            // 续期失败时监控锁仍保持独占，后台不能与尚未完成的手动操作并发修改网络。
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _renewal?.Dispose();
            _networkLease?.Dispose();
            if (_requested) ManualControlRequestStore.Remove(_directory, _id);
            _requestLease.Dispose();
        }
    }
}

/// <summary>后台保持心跳，等待恢复收尾后暂时交权；请求结束或过期后自动恢复。</summary>
public sealed class GuardianControlSession(string directory, MonitoringLease initialLease,
    Action stop, Func<bool> isChecking, Action resume) : IDisposable
{
    private MonitoringLease? _lease = initialLease;
    public bool IsManualControl { get; private set; }

    public void Poll(DateTimeOffset now)
    {
        if (ManualControlRequestStore.Read(directory, now) is not null)
        {
            if (!IsManualControl) { IsManualControl = true; stop(); }
            if (!isChecking()) { _lease?.Dispose(); _lease = null; }
            return;
        }
        if (!IsManualControl || isChecking()) return;
        _lease ??= MonitoringLease.TryAcquire(directory);
        if (_lease is null) return;
        IsManualControl = false;
        resume();
    }

    public void Dispose() => _lease?.Dispose();
}
