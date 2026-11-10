using System.IO;
using NetGuardian.App.Updates;
using NetGuardian.Core.Updates;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class UpdateSessionTests
{
    [Fact]
    public async Task 无更新与有更新时仅开放对应操作()
    {
        using var session = new UpdateSession(new Service(), new Version(1, 1, 0));
        Assert.Equal(UpdatePhase.Idle, session.Phase);
        await session.CheckAsync();
        Assert.Equal(UpdatePhase.NoUpdate, session.Phase);
        using var available = new UpdateSession(new Service { Release = Release }, new Version(1, 1, 0));
        await available.CheckAsync();
        Assert.Equal(UpdatePhase.Available, available.Phase);
        await available.DownloadAsync("缓存");
        Assert.Equal(UpdatePhase.Ready, available.Phase);
        Assert.NotNull(available.Downloaded);
        available.BeginInstall();
        Assert.Equal(UpdatePhase.Installing, available.Phase);
        available.InstallFailed(new IOException("准备失败"));
        Assert.Equal(UpdatePhase.Ready, available.Phase);
    }

    [Fact]
    public async Task 下载取消后忽略迟到的结果和进度()
    {
        var service = new Service { Release = Release, Wait = new TaskCompletionSource<DownloadedUpdate>() };
        using var session = new UpdateSession(service, new Version(1, 1, 0));
        await session.CheckAsync();
        var pending = session.DownloadAsync("缓存");
        Assert.Equal(UpdatePhase.Downloading, session.Phase);
        session.Cancel();
        service.Progress!.Report(new UpdateProgress(100, 100));
        service.Wait.SetResult(new DownloadedUpdate(Release.Version, "安装包", "摘要"));
        await pending;
        Assert.Equal(UpdatePhase.Cancelled, session.Phase);
        Assert.Null(session.Downloaded);
        Assert.Null(session.Progress);
    }

    [Fact]
    public async Task 失败可重试且忙碌时拒绝重复开始()
    {
        using var session = new UpdateSession(new Service { Failure = new IOException("网络错误") }, new Version(1, 1, 0));
        await session.CheckAsync();
        Assert.Equal(UpdatePhase.Failed, session.Phase);
        Assert.IsType<IOException>(session.Error);
        var service = new Service { Release = Release, Wait = new TaskCompletionSource<DownloadedUpdate>() };
        using var busy = new UpdateSession(service, new Version(1, 1, 0));
        await busy.CheckAsync();
        var pending = busy.DownloadAsync("缓存");
        await Assert.ThrowsAsync<InvalidOperationException>(() => busy.CheckAsync());
        busy.Cancel();
        service.Wait.SetCanceled();
        await pending;
    }

    private static readonly UpdateRelease Release = new(new Version(1, 2, 0), "说明", new Uri("https://github.com"),
        new UpdateAsset("包", new Uri("https://github.com"), 100, null), new UpdateAsset("摘要", new Uri("https://github.com"), 100, null));

    private sealed class Service : IUpdateService
    {
        public UpdateRelease? Release;
        public Exception? Failure;
        public TaskCompletionSource<DownloadedUpdate>? Wait;
        public IProgress<UpdateProgress>? Progress;
        public Task<UpdateRelease?> CheckAsync(Version version, CancellationToken token) => Failure is null
            ? Task.FromResult(Release) : Task.FromException<UpdateRelease?>(Failure);
        public Task<DownloadedUpdate> DownloadAsync(UpdateRelease release, string directory, IProgress<UpdateProgress>? progress, CancellationToken token)
        {
            Progress = progress;
            return Wait?.Task ?? Task.FromResult(new DownloadedUpdate(release.Version, "安装包", "摘要"));
        }
    }
}
