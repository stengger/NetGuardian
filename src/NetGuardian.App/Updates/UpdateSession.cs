using System;
using System.Threading;
using System.Threading.Tasks;
using NetGuardian.Core.Updates;

namespace NetGuardian.App.Updates;

internal enum UpdatePhase { Idle, Checking, NoUpdate, Available, Downloading, Ready, Cancelled, Failed, Installing, WaitingForExit }

internal sealed class UpdateSession(IUpdateService service, Version currentVersion,
    Func<UpdateRelease, CancellationToken, Task<UpdateCacheState>>? findCached = null) : IDisposable
{
    private CancellationTokenSource? _cancellation;
    private int _generation;
    public event Action? Changed;
    public UpdatePhase Phase { get; private set; }
    public UpdateRelease? Release { get; private set; }
    public DownloadedUpdate? Downloaded { get; private set; }
    public UpdateProgress? Progress { get; private set; }
    public Exception? Error { get; private set; }
    public bool IsBusy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing or UpdatePhase.WaitingForExit;

    public async Task CheckAsync()
    {
        var (generation, token) = Begin(UpdatePhase.Checking);
        Release = null;
        try
        {
            var release = await service.CheckAsync(currentVersion, token);
            if (generation != _generation) return;
            var cached = release is not null && findCached is not null ? await findCached(release, token) : default;
            if (generation != _generation) return;
            Downloaded = cached.Downloaded;
            Progress = cached.Partial;
            Release = release;
            Phase = release is null ? UpdatePhase.NoUpdate : Downloaded is null ? UpdatePhase.Available : UpdatePhase.Ready;
        }
        catch (Exception ex) { Fail(generation, ex); }
        if (generation == _generation) Changed?.Invoke();
    }

    public async Task DownloadAsync(string directory)
    {
        if (Release is null) throw new InvalidOperationException("尚未检查到新版本。");
        var previous = Progress;
        var (generation, token) = Begin(UpdatePhase.Downloading);
        try
        {
            // 回调在创建会话的 UI 上下文中执行，代数检查屏蔽取消后的排队通知。
            var progress = new Progress<UpdateProgress>(value =>
            {
                if (generation != _generation || Phase != UpdatePhase.Downloading) return;
                Progress = value;
                Changed?.Invoke();
            });
            var downloaded = await service.DownloadAsync(Release, directory, progress, token);
            if (generation != _generation) return;
            Downloaded = downloaded;
            Phase = UpdatePhase.Ready;
        }
        catch (Exception ex) { Fail(generation, ex, previous); }
        if (generation == _generation) Changed?.Invoke();
    }

    public void BeginInstall()
    {
        if (Phase != UpdatePhase.Ready || Downloaded is null) throw new InvalidOperationException("尚无已校验的安装包。");
        Error = null;
        Phase = UpdatePhase.Installing;
        Changed?.Invoke();
    }

    public void DownloadPreparationFailed(Exception error)
    {
        Error = error;
        Progress = null;
        Downloaded = null;
        Phase = UpdatePhase.Failed;
        Changed?.Invoke();
    }

    public void WaitForExit()
    {
        if (Phase != UpdatePhase.Installing) throw new InvalidOperationException("更新尚未开始准备。");
        Phase = UpdatePhase.WaitingForExit;
        Changed?.Invoke();
    }

    public void InstallFailed(Exception error)
    {
        Error = error;
        Phase = UpdatePhase.Ready;
        Changed?.Invoke();
    }

    public void Cancel()
    {
        if (Phase is not (UpdatePhase.Checking or UpdatePhase.Downloading)) return;
        _generation++;
        _cancellation?.Cancel();
        Phase = UpdatePhase.Cancelled;
        Progress = null;
        Downloaded = null;
        Changed?.Invoke();
    }

    private (int Generation, CancellationToken Token) Begin(UpdatePhase phase)
    {
        if (IsBusy) throw new InvalidOperationException("更新操作尚未结束。");
        _cancellation?.Dispose();
        _cancellation = phase == UpdatePhase.Checking ? new(TimeSpan.FromSeconds(30)) : new();
        _generation++;
        Phase = phase;
        Error = null;
        if (phase != UpdatePhase.Downloading) Progress = null;
        Downloaded = null;
        Changed?.Invoke();
        return (_generation, _cancellation.Token);
    }

    private void Fail(int generation, Exception error, UpdateProgress? previous = null)
    {
        if (generation != _generation) return;
        Error = error;
        Progress = error is UpdateException { Error: UpdateError.Network or UpdateError.RateLimited } interrupted
            ? interrupted.SavedProgress ?? previous : null;
        Phase = error is OperationCanceledException ? UpdatePhase.Cancelled : UpdatePhase.Failed;
    }

    public void Dispose()
    {
        _generation++;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        Changed = null;
    }
}
