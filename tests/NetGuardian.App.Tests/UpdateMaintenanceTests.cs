using NetGuardian.App.Updates;
using System.IO;
using System.Security.AccessControl;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class UpdateMaintenanceTests
{
    [Theory]
    [InlineData("O:BAG:BAD:(A;;GA;;;AU)")]
    [InlineData("O:BAG:BAD:(A;;GW;;;AU)")]
    [InlineData("O:BAG:BAD:(A;;FW;;;BU)")]
    public void 通用或具体写权限均不能授予普通用户(string sddl)
    {
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm(sddl);
        Assert.Throws<IOException>(() => UpdateStorage.AssertSecurity(security));
    }

    [Fact]
    public void 仅继承的创建者权限不误判为当前目录允许写入()
    {
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm("O:BAG:BAD:(A;OICIIO;GA;;;CO)(A;;GA;;;BA)(A;;GR;;;BU)");
        UpdateStorage.AssertSecurity(security);
    }

    [Fact]
    public void 维护期间拒绝其他线程启动界面且释放后可重新启动()
    {
        var name = "NetGuardian-UpdateTest-" + Guid.NewGuid();
        using (var gate = UpdateMaintenanceGate.TryEnter(name))
        {
            Assert.NotNull(gate);
            Assert.True(OnOtherThread(() =>
            {
                using var other = UpdateMaintenanceGate.TryEnter(name);
                return other is null;
            }));
        }
        Assert.True(OnOtherThread(() =>
        {
            using var reopened = UpdateMaintenanceGate.TryEnter(name);
            return reopened is not null;
        }));
    }

    private static bool OnOtherThread(Func<bool> action)
    {
        var result = false;
        Exception? failure = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception ex) { failure = ex; } });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
}
