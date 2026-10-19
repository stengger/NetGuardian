using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NetGuardian.Core.Wifi;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class StartupServicesTests
{
    [Fact]
    public void DesktopServicesResolveWifiClientWithoutCircularDependency()
    {
        // 使用桌面程序的实际注册，验证启动依赖；不扫描、连接或修改网络。
        var buildServices = typeof(App).GetMethod("BuildServices", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var services = (ServiceProvider)buildServices.Invoke(null, null)!;

        var client = services.GetRequiredService<IWifiClient>();

        Assert.IsType<WifiClient>(client);
        Assert.Same(client, services.GetRequiredService<IWifiClient>());
    }
}
