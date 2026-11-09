using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NativeWifiConnectionTests
{
    [Fact]
    public async Task Windows明确连接失败时直接报告原因且不能再次发送回退连接()
    {
        var native = new FakeNativeConnector
        {
            ConnectionFailureIsFinal = true,
            LastConnectionFailure = "特定的网络不可用。"
        };
        var fallback = new FakeWifiClient();
        fallback.ConnectSucceedsFor.Add("目标网络");
        var client = new WifiClient(native, fallback);

        Assert.False(await client.ConnectAsync("目标网络"));
        Assert.Equal("特定的网络不可用。", client.LastConnectionFailure);
        Assert.Empty(fallback.ConnectAttempts);
    }

    [Fact]
    public async Task 原生连接运行中取消须传递令牌并等待收尾且不回退()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var native = new FakeNativeConnector { Handler = token =>
        {
            entered.TrySetResult();
            release.Wait();
            token.ThrowIfCancellationRequested();
            return true;
        }};
        var fallback = new FakeWifiClient();
        var client = new WifiClient(native, fallback);
        var connection = client.ConnectAsync("备用", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        try { Assert.False(connection.IsCompleted); }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection);
        Assert.Equal(cancellation.Token, native.LastToken);
        Assert.Empty(fallback.ConnectAttempts);
    }

    [Fact]
    public async Task 原生及回退都失败时保留错误码且后续成功清除错误()
    {
        var native = new FakeNativeConnector { LastNativeError = 5 };
        var client = new WifiClient(native, new FakeWifiClient());
        Assert.False(await client.ConnectAsync("备用"));
        Assert.Contains("5", client.LastConnectionFailure);
        native.Accepted = true;
        Assert.True(await client.ConnectAsync("备用"));
        Assert.Null(client.LastConnectionFailure);
    }

    [Fact]
    public async Task 原生接口异常时允许命令行回退()
    {
        var native = new FakeNativeConnector { Failure = new DllNotFoundException("原生接口不可用") };
        var fallback = new FakeWifiClient();
        fallback.ConnectSucceedsFor.Add("备用");
        var client = new WifiClient(native, fallback);
        Assert.True(await client.ConnectAsync("备用"));
        Assert.Equal(new[] { "备用" }, fallback.ConnectAttempts);
    }

    [Fact]
    public async Task 原生请求接受中文配置后不重复调用命令行()
    {
        var native = new FakeNativeConnector { Accepted = true };
        var fallback = new FakeWifiClient();
        var client = new WifiClient(native, fallback);
        Assert.True(await client.ConnectProfileAsync("实验室 配置", "8b304北"));
        Assert.Equal("实验室 配置", native.LastProfile);
        Assert.Empty(fallback.ConnectAttempts);
        Assert.Null(client.LastConnectionFailure);
    }

    [Fact]
    public async Task 原生请求未接受时才回退到保存配置连接()
    {
        var native = new FakeNativeConnector();
        var fallback = new FakeWifiClient();
        fallback.ConnectSucceedsFor.Add("备用");
        var client = new WifiClient(native, fallback);
        Assert.True(await client.ConnectAsync("备用"));
        Assert.Equal(new[] { "备用" }, fallback.ConnectAttempts);
    }

    [Fact]
    public async Task 已取消的连接不能发起原生或回退请求()
    {
        var native = new FakeNativeConnector { Accepted = true };
        var fallback = new FakeWifiClient();
        var client = new WifiClient(native, fallback);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync("备用", cancellation.Token));
        Assert.Null(native.LastProfile);
        Assert.Empty(fallback.ConnectAttempts);
    }

    private sealed class FakeNativeConnector : INativeWifiConnector
    {
        public bool ConnectionFailureIsFinal { get; set; }
        public string? LastConnectionFailure { get; set; }
        public bool Accepted { get; set; }
        public uint? LastNativeError { get; set; }
        public Exception? Failure { get; set; }
        public string? LastProfile { get; private set; }
        public Func<CancellationToken, bool>? Handler { get; set; }
        public CancellationToken LastToken { get; private set; }
        public bool TryConnectProfile(string profileName, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            if (Handler is not null) return Handler(cancellationToken);
            LastProfile = profileName;
            if (Failure is not null) throw Failure;
            return Accepted;
        }
    }
}
