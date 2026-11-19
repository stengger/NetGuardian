using NetGuardian.Core.Models;
using NetGuardian.Core.Monitoring;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.Tests;

public sealed class WifiRadioRecoveryTests
{
    [Fact]
    public async Task 接口命令失败时仍需执行软件无线电恢复()
    {
        var radio = new FakeRadioController();
        var client = new NetshWifiClient(new InterfaceCommands(1), radio);
        Assert.True(await client.TryEnableWifiAdapterAsync("WLAN"));
        Assert.Equal("WLAN", Assert.Single(radio.Attempts));
        Assert.Null(client.LastAdapterEnableFailure);
    }

    [Fact]
    public async Task 接口命令成功后仍需启用软件无线电()
    {
        var commands = new InterfaceCommands(0);
        var radio = new FakeRadioController();
        var client = new NetshWifiClient(commands, radio);
        Assert.True(await client.TryEnableWifiAdapterAsync("WLAN"));
        Assert.Equal("WLAN", Assert.Single(radio.Attempts));
        Assert.Equal(2, commands.Attempts.Count);
        Assert.Equal(new[] { "wlan", "set", "autoconfig", "enabled=yes", "interface=WLAN" },
            commands.Attempts[1].Arguments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 无线电失败不能被接口命令成功掩盖且保留错误码(int interfaceExitCode)
    {
        var radio = new FakeRadioController { Succeeds = false, LastRadioFailure = "WlanSetInterface 错误码 5" };
        var client = new NetshWifiClient(new InterfaceCommands(interfaceExitCode), radio);
        Assert.False(await client.TryEnableWifiAdapterAsync("WLAN"));
        Assert.Contains("错误码 5", client.LastAdapterEnableFailure);
        if (interfaceExitCode != 0) Assert.Contains("退出码 1", client.LastAdapterEnableFailure);
    }

    [Theory]
    [InlineData(@"PCI\VEN_14C3&DEV_7925\TEST")]
    [InlineData(@"USB\VID_1234&PID_5678\TEST")]
    public async Task 禁用设备仅按精确实例启用且不修改其他无线电(string instance)
    {
        var commands = new InterfaceCommands(0);
        var radio = new FakeRadioController();
        var client = new NetshWifiClient(commands, radio);
        Assert.True(await client.TryEnableWifiAdapterAsync(instance));
        var command = Assert.Single(commands.Attempts);
        Assert.Equal("pnputil.exe", command.FileName);
        Assert.Equal(new[] { "/enable-device", instance }, command.Arguments);
        Assert.Empty(radio.Attempts);
    }

    [Fact]
    public async Task 取消后不执行启用命令或无线电写入()
    {
        var commands = new InterfaceCommands(0);
        var radio = new FakeRadioController();
        var client = new NetshWifiClient(commands, radio);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.TryEnableWifiAdapterAsync("WLAN", cancellation.Token));
        Assert.Empty(commands.Attempts);
        Assert.Empty(radio.Attempts);
    }

    [Fact]
    public async Task 接口命令期间取消后不再启用无线电()
    {
        using var cancellation = new CancellationTokenSource();
        var commands = new InterfaceCommands(1) { OnRun = cancellation.Cancel };
        var radio = new FakeRadioController();
        var client = new NetshWifiClient(commands, radio);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.TryEnableWifiAdapterAsync("WLAN", cancellation.Token));
        Assert.Single(commands.Attempts);
        Assert.Empty(radio.Attempts);
    }

    [Fact]
    public async Task 未启动监控时手动检测不得启用无线电()
    {
        var events = new List<EngineEventKind>();
        var wifi = new FakeWifiClient
        {
            Diagnostics = new("WLAN", true, false, true, false, "disconnected", "")
        };
        using var engine = new MonitoringEngine(wifi, new FakeConnectivityTester(), new FakeCommandRunner(),
            () => new MonitorPolicy(), () => [])
        { AdapterEnableSettleDelay = TimeSpan.Zero };
        engine.EngineEvent += (_, args) => events.Add(args.Kind);
        await engine.CheckNowAsync();
        Assert.DoesNotContain(EngineEventKind.TryingEnableWifiAdapter, events);
        Assert.Empty(wifi.AdapterEnableAttempts);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task 只有监控运行且策略允许时才写入无线电(bool monitoring, bool policyEnabled, bool expected)
    {
        var wifi = new FakeWifiClient { Diagnostics = new("WLAN", true, false, true, false, "disconnected", "") };
        using var engine = new MonitoringEngine(wifi, new FakeConnectivityTester(), new FakeCommandRunner(),
            () => new MonitorPolicy { EnableWifiAdapterAutoEnable = policyEnabled }, () => [])
        { AdapterEnableSettleDelay = TimeSpan.Zero };
        if (monitoring) engine.Start(TimeSpan.FromHours(1));
        await engine.CheckNowAsync();
        Assert.Equal(expected ? 1 : 0, wifi.AdapterEnableAttempts.Count);
        if (expected) Assert.Equal("WLAN", wifi.AdapterEnableAttempts[0]);
    }

    [Fact]
    public async Task 启用失败原因穿透无线客户端并写入监控错误事件()
    {
        var wifi = new FakeWifiClient
        {
            Diagnostics = new("WLAN", true, false, true, false, "disconnected", ""),
            AdapterEnableSucceeds = false,
            LastAdapterEnableFailure = "WlanSetInterface 错误码 5"
        };
        var events = new List<EngineEventArgs>();
        using var engine = new MonitoringEngine(new WifiClient(fallback: wifi,
            deviceInspector: new WifiDeviceInspector(new FakeCommandRunner())),
            new FakeConnectivityTester(), new FakeCommandRunner(), () => new MonitorPolicy(), () => [])
        { AdapterEnableSettleDelay = TimeSpan.Zero };
        engine.EngineEvent += (_, args) => events.Add(args);
        engine.Start(TimeSpan.FromHours(1));
        await engine.CheckNowAsync();
        Assert.Contains(events, args => args.Kind == EngineEventKind.WifiAdapterEnableFailed);
        Assert.DoesNotContain(events, args => args.Kind == EngineEventKind.WifiAdapterEnabled);
        Assert.Contains(events, args => args.Kind == EngineEventKind.CheckError
            && args.Args.Contains(wifi.LastAdapterEnableFailure));
    }

    private sealed class InterfaceCommands(int exitCode) : ISystemCommandRunner
    {
        public List<(string FileName, string[] Arguments)> Attempts { get; } = [];
        public Action? OnRun { get; init; }

        public Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add((fileName, arguments.ToArray()));
            OnRun?.Invoke();
            return Task.FromResult(new SystemCommandResult(exitCode, "接口已启用，但软件无线电关闭。"));
        }
    }

    private sealed class FakeRadioController : IWifiRadioController
    {
        public List<string> Attempts { get; } = [];
        public bool Succeeds { get; init; } = true;
        public string? LastRadioFailure { get; init; }

        public bool TryEnableSoftwareRadio(string adapterName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts.Add(adapterName);
            return Succeeds;
        }
    }
}
