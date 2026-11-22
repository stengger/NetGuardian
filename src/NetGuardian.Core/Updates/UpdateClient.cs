namespace NetGuardian.Core.Updates;

// 协议在启动时选定。旧客户端的构造委托仅在构建完全没有正式信任配置时调用。
public sealed class UpdateClient : IDisposable
{
    private readonly IDisposable lifetime;
    private UpdateClient(IUpdateService service, IDisposable lifetime) => (Service, this.lifetime) = (service, lifetime);
    public IUpdateService Service { get; }
    public SignedUpdateService? Signed => Service as SignedUpdateService;

    public static UpdateClient CreateOfficial(Func<string> directory, Action<FileInfo> validate,
        Func<FileInfo, FileStream> create, Func<bool> allowDirect) => Create(OfficialUpdateFeed.Trust, directory,
        validate, create, allowDirect, () => DateTimeOffset.UtcNow,
        proxy => new HttpClientHandler { UseProxy = proxy, AllowAutoRedirect = false, UseCookies = false },
        () => new HttpClient { Timeout = TimeSpan.FromMinutes(30) });

    internal static UpdateClient Create(UpdateTrust? trust, Func<string> directory, Action<FileInfo>? validate,
        Func<FileInfo, FileStream>? create, Func<bool> allowDirect, Func<DateTimeOffset> clock,
        Func<bool, HttpMessageHandler> handler, Func<HttpClient> legacyClient)
    {
        if (trust is not null)
        {
            var signed = new SignedUpdateService(trust, new(directory, validate, create), allowDirect, clock, handler, validate, create);
            return new(signed, signed);
        }
        var http = legacyClient();
        return new(new GitHubUpdateService(http, validate, create), http);
    }

    public Task<UpdateCacheState> InspectCacheAsync(UpdateRelease release, string directory, CancellationToken token) =>
        Signed is { } signed ? signed.InspectCacheAsync(release, directory, token) :
        ((GitHubUpdateService)Service).InspectCacheAsync(release, directory, token);

    public void Dispose() => lifetime.Dispose();
}
