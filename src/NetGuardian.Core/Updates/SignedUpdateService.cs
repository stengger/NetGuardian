namespace NetGuardian.Core.Updates;

public enum UpdateRoute { PrimarySystem, MirrorSystem, MirrorDirect }
public enum UpdateSourceMode { Auto, Primary, Mirror }
public enum UpdateStep { Manifest, Download }
public sealed record UpdateRouteFailure(UpdateRoute Route, UpdateStep Step, string Reason);
public sealed record UpdateRouteStatus(UpdateRoute? Route, UpdateStep Step, IReadOnlyList<UpdateRouteFailure> Failures, DateTimeOffset? LastChecked);

public sealed class SignedUpdateService : IUpdateService, IDisposable
{
    private sealed record Line(UpdateEndpoint Endpoint, HttpClient Client, GitHubUpdateService Transfer);
    private sealed record Selection(SignedManifest Manifest, UpdateRelease Release, UpdateRoute Route);
    private readonly Dictionary<UpdateRoute, Line> lines = new();
    private readonly UpdateTrust trust;
    private readonly UpdateSequenceStore store;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<bool> allowDirect;
    private readonly string? configurationError;
    private Selection? selected;
    public UpdateSourceMode Mode { get; set; }
    public UpdateRouteStatus Status { get; private set; } = new(null, UpdateStep.Manifest, [], null);
    public event Action? StatusChanged;

    internal SignedUpdateService(UpdateTrust trust, UpdateSequenceStore store, Func<bool> allowDirect,
        Func<DateTimeOffset> clock, Func<bool, HttpMessageHandler> handler, Action<FileInfo>? validate = null,
        Func<FileInfo, FileStream>? create = null)
    {
        this.trust = trust;
        this.store = store;
        this.clock = clock;
        this.allowDirect = allowDirect;

        foreach (var route in Enum.GetValues<UpdateRoute>())
        {
            bool primary = route == UpdateRoute.PrimarySystem;
            var endpoint = primary ? trust.Primary : trust.Mirror;
            if (!UpdateRouteHandler.Secure(endpoint.Manifest) || !UpdateRouteHandler.Secure(endpoint.PackageRoot) ||
                !endpoint.PackageRoot.AbsolutePath.EndsWith('/') || endpoint.PackageRoot.Query.Length != 0 ||
                !primary && !UpdateRouteHandler.SameOrigin(endpoint.Manifest, endpoint.PackageRoot))
            {
                configurationError = "内置分发地址无效，请联系发布维护者。";
                break;
            }
            var client = new HttpClient(new UpdateRouteHandler(endpoint, primary,
                () => route != UpdateRoute.MirrorDirect || allowDirect(), handler(route != UpdateRoute.MirrorDirect)))
                { Timeout = TimeSpan.FromMinutes(30) };
            lines.Add(route, new(endpoint, client, new(client, validate, create)));
        }
    }

    private IEnumerable<UpdateRoute> Routes(UpdateRoute? preferred = null)
    {
        var available = Enum.GetValues<UpdateRoute>().Where(route => route switch
        {
            UpdateRoute.PrimarySystem => Mode != UpdateSourceMode.Mirror,
            UpdateRoute.MirrorSystem => Mode != UpdateSourceMode.Primary,
            _ => Mode == UpdateSourceMode.Auto && allowDirect()
        });
        return available.OrderByDescending(route => route == preferred);
    }

    private void Report(UpdateRouteStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    private void Failure(UpdateRoute route, UpdateStep step, string reason) => Report(Status with
    {
        Failures = [..Status.Failures, new(route, step, reason)]
    });

    private static string Reason(Exception error)
    {
        if (error is OperationCanceledException) return "请求超时。";
        // 跳转地址可能包含短时令牌，错误反馈不回显请求 URL。
        if (error is HttpRequestException http) return http.StatusCode is { } code ? $"HTTP {(int)code}" : "无法建立连接或连接中断。";
        return error.InnerException is HttpRequestException inner ? Reason(inner) : error.Message;
    }

    private async Task<SignedManifest> FetchAsync(UpdateRoute route, CancellationToken token, TimeSpan budget)
    {
        var line = lines[route];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(budget);
        using var request = new HttpRequestMessage(HttpMethod.Get, line.Endpoint.Manifest);
        request.Headers.UserAgent.ParseAdd("NetGuardian-Updater/2");
        using var response = await line.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        try
        {
            var bytes = await GitHubUpdateService.ReadLimitedAsync(response.Content, 128 * 1024, deadline.Token);
            return SignedManifest.Verify(bytes, trust.PublicKey);
        }
        catch (IOException ex) { throw new HttpRequestException("读取签名清单时连接中断。", ex); }
    }

    private static bool Recoverable(Exception ex) => ex is HttpRequestException or OperationCanceledException or
        UpdateException { Error: UpdateError.Network or UpdateError.RateLimited or UpdateError.Expired };

    public async Task<UpdateRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        selected = null;
        Report(new(null, UpdateStep.Manifest, [], null));
        if (configurationError is not null) throw SignedManifest.Security(configurationError);
        var previous = store.Read();
        Report(Status with { LastChecked = previous?.Checked });
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(TimeSpan.FromSeconds(12));
        bool expired = false;
        foreach (var route in Routes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total.IsCancellationRequested) break;
            Report(Status with { Route = route });
            try
            {
                // 备用源系统和直连共用一个 5 秒目标，为最后一次直连保留时间。
                var seconds = route switch
                {
                    UpdateRoute.MirrorSystem when Mode == UpdateSourceMode.Auto && allowDirect() => 2,
                    UpdateRoute.MirrorDirect => 3,
                    _ => 5
                };
                var budget = TimeSpan.FromSeconds(seconds);
                var manifest = await FetchAsync(route, total.Token, budget);
                total.Token.ThrowIfCancellationRequested();
                var now = clock();
                var accepted = store.Verify(manifest, now, route, true);
                Report(Status with { LastChecked = accepted.Checked });
                if (manifest.Version <= currentVersion) return null;
                var release = manifest.At(lines[route].Endpoint);
                selected = new(manifest, release, route);
                return release;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && Recoverable(ex))
            {
                expired |= ex is UpdateException { Error: UpdateError.Expired };
                Failure(route, UpdateStep.Manifest, Reason(ex));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new UpdateException(expired ? UpdateError.Expired : UpdateError.Network,
            expired ? "可读取的清单已过期。" : "所选更新线路均未能取得清单。");
    }

    public UpdateRouteStatus? LastSuccessfulRoute()
    {
        var last = store.Read();
        return last is null ? null : new(last.DownloadRoute ?? last.Route,
            last.DownloadRoute.HasValue ? UpdateStep.Download : UpdateStep.Manifest, [], last.Checked);
    }

    private Selection Active(UpdateRelease release)
    {
        if (selected is not { } selection || !ReferenceEquals(release, selection.Release))
            throw SignedManifest.Security("请先重新检查已签名的发行版本。");
        store.Verify(selection.Manifest, clock(), selection.Route, false);
        return selection;
    }

    public void ValidateForInstall(UpdateRelease release) => Active(release);

    public async Task<UpdateCacheState> InspectCacheAsync(UpdateRelease release, string directory, CancellationToken token)
    {
        var current = Active(release);
        var cached = await lines[current.Route].Transfer.InspectVerifiedAsync(release, directory, current.Manifest.Hash, token);
        Active(release);
        return cached;
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release, string directory, IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var current = Active(release);
        var cached = await InspectCacheAsync(release, directory, cancellationToken);
        if (cached.Downloaded is not null) return cached.Downloaded;
        UpdateProgress? saved = cached.Partial;
        Report(Status with { Step = UpdateStep.Download, Failures = [] });
        foreach (var route in Routes(current.Route))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[route];
            var step = route == current.Route ? UpdateStep.Download : UpdateStep.Manifest;
            Report(Status with { Route = route, Step = step });
            try
            {
                if (route != current.Route)
                {
                    var alternate = await FetchAsync(route, cancellationToken, TimeSpan.FromSeconds(5));
                    cancellationToken.ThrowIfCancellationRequested();
                    if (alternate.Sequence < current.Manifest.Sequence)
                    {
                        Failure(route, step, "该源暂未同步所选版本。");
                        continue;
                    }
                    if (alternate.Sequence > current.Manifest.Sequence)
                    {
                        store.Verify(alternate, clock(), route, true);
                        throw SignedManifest.Security("该源已有更新的签名清单，请重新检查版本。");
                    }
                    if (alternate.Identity != current.Manifest.Identity) throw SignedManifest.Security("两源同一序号的签名清单内容不同。");
                }
                Active(release);
                step = UpdateStep.Download;
                Report(Status with { Step = step });
                var result = await line.Transfer.DownloadVerifiedAsync(current.Manifest.At(line.Endpoint), directory,
                    current.Manifest.Hash, true, progress, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                store.Verify(current.Manifest, clock(), route, false, downloaded: true);
                selected = current with { Route = route };
                return result;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && Recoverable(ex))
            {
                if (ex is UpdateException { SavedProgress: { } received }) saved = received;
                Failure(route, step, Reason(ex));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new UpdateException(UpdateError.Network, "所选线路均未完成下载。") { SavedProgress = saved };
    }

    public void Dispose()
    {
        foreach (var line in lines.Values) line.Client.Dispose();
        StatusChanged = null;
    }
}
